# credito-saga.ps1 - Arnes de la ruta de credito a 30 dias de la saga.
# Crea ordenes como camino-feliz.ps1 (siembra un SKU de prueba y llama directo a
# Ordenes), pero con medioPago CREDITO y el JWT del cliente, y verifica el
# desenlace en Ordenes, Credito, Inventario y Auditoria.
#
# Escenarios:
#   aprobado      cliente1 compra a credito; la orden llega al menos a CONFIRMADA con
#                 documento fiscal; Mi credito sube el saldo en el total y muestra la
#                 compra con vencimiento a 30 dias.
#   cupo          cliente3 compra por mas de 1500; termina RECHAZADA, la reserva se
#                 libera y el saldo utilizado no cambia.
#   no-elegible   cliente4; termina RECHAZADA con mensaje de pagos con atraso.
#   suplantacion  token de cliente4 con clienteId de cliente1; 403 y no se crea orden.
#   sin-sesion    orden a credito sin token; 401.
#   cancelacion   cliente2 compra a credito, se confirma y cancela; la cuenta queda
#                 ANULADA y el cupo vuelve.
#   timeout       detiene credito-api, cliente1 crea la orden; RECHAZADA con
#                 CREDITO_SIN_RESPUESTA antes de 45 s y stock liberado; levanta
#                 credito-api y el saldo utilizado termina igual que al inicio.
#   reiniciar     POST /interno/credito/demo/reiniciar.
#   iva           orden por API de 7 x Q100: total 700.00, IVA incluido 75.00 calculado
#                 en el servidor aunque el request mande otro impuesto.
#   abono-parcial cliente1 compra a credito Q500 y abona Q200: APLICADO, saldo 300 y
#                 el disponible sube 200.
#   pago-total    paga el resto de la compra anterior: PAGADA, puntual y cupo completo.
#   pago-rechazado PayKlik en modo RECHAZO (se restaura NORMAL): abono RECHAZADO sin
#                 cambio de saldo.
#   pago-pendiente detiene payklik-mock: abono PENDIENTE y 409 abono_en_curso; lo
#                 levanta, reintenta el mismo abono hasta APLICADO y verifica un solo
#                 pago y un solo cargo aprobado en Pagos.
#   recordatorio  vencimiento a 2 dias, procesa dos veces: un solo CuentaCreditoPorVencer
#                 en Auditoria y la compra aparece como "Vence pronto".
#   vencimiento   cliente2: cuenta vencida, linea en pausa, elegibilidad LINEA_BLOQUEADA,
#                 orden nueva a credito RECHAZADA; al pagar se reactiva y cuenta tardio.
#                 Se usa cliente2 (BUENO): con el pago tardio sigue sobre el minimo
#                 de 60; cliente3 (62) quedaria NO_ELEGIBLE por la politica v2.
#   todos         reiniciar y luego todos los anteriores.
#
# Cada paso imprime lo esperado y lo obtenido; cada escenario termina con
# APROBADO o FALLIDO.
#
# Uso:
#   .\scripts\pruebas\credito-saga.ps1 -Escenario todos
#   .\scripts\pruebas\credito-saga.ps1 -Escenario aprobado -TimeoutSeg 90

param(
    [ValidateSet('aprobado', 'cupo', 'no-elegible', 'suplantacion', 'sin-sesion', 'cancelacion', 'timeout', 'reiniciar',
        'iva', 'abono-parcial', 'pago-total', 'pago-rechazado', 'pago-pendiente', 'recordatorio', 'vencimiento', 'todos')]
    [string]$Escenario = 'todos',
    [string]$BaseUrl = 'http://localhost:8083',
    [string]$GatewayUrl = 'http://localhost:8080',
    [string]$CreditoUrl = 'http://localhost:8099',
    [string]$InventarioUrl = 'http://localhost:8094',
    [string]$AuditoriaUrl = 'http://localhost:8093',
    [string]$PagosUrl = 'http://localhost:8085',
    [string]$PayKlikUrl = 'http://localhost:8084',
    # cliente1..cliente3 vienen del seed SQL (1234); cliente4..cliente6 de sembrar-demo.sh (Demo1234!).
    [string[]]$Contrasenas = @('Demo1234!', '1234'),
    [int]$TimeoutSeg = 60,
    [string]$Sku = 'SKU-CREDITO-SAGA',
    [string]$UbicacionId = 'CD-01',
    [string]$DbUsuario = 'sgo_admin'
)

$raiz = (Resolve-Path "$PSScriptRoot\..\..").Path
$compose = Join-Path $raiz 'docker-compose.yml'
$PrecioUnitario = 100.0

# Estados en el orden de la ruta de credito; sirve para esperar "este o uno posterior".
$Estados = @(
    'CREADA', 'RESERVA_SOLICITADA', 'INVENTARIO_RESERVADO', 'EVALUACION_CREDITO_SOLICITADA',
    'CREDITO_APROBADO', 'CONFIRMADA_PENDIENTE_FISCAL', 'CONFIRMADA', 'DESPACHO_SOLICITADO',
    'EN_PREPARACION', 'EN_TRANSITO', 'LISTA_PARA_RECOGER', 'ENTREGADA'
)
$Terminales = @('CANCELADA', 'CANCELADA_RESERVA_VENCIDA', 'RECHAZADA')

$script:Reloj = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------------------
# Funciones auxiliares
# ---------------------------------------------------------------------------

# Devuelve @{ Status; Body } sin lanzar. Body es el JSON ya convertido, o $null.
function Invoke-Http {
    param([string]$Method, [string]$Url, [object]$Body = $null, [string]$Token = $null)

    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{
        Uri             = $Url
        Method          = $Method
        Headers         = $headers
        ContentType     = 'application/json; charset=utf-8'
        UseBasicParsing = $true
        TimeoutSec      = 15
    }
    if ($null -ne $Body) {
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10))
    }

    try {
        $res = Invoke-WebRequest @params
        $contenido = $null
        if (-not [string]::IsNullOrWhiteSpace($res.Content)) {
            $texto = [System.Text.Encoding]::UTF8.GetString($res.RawContentStream.ToArray())
            # Algunas respuestas no son JSON (por ejemplo /health/live): se deja el cuerpo en $null.
            try { $contenido = $texto | ConvertFrom-Json } catch { $contenido = $null }
        }
        return @{ Status = [int]$res.StatusCode; Body = $contenido }
    }
    catch {
        $status = 0
        if ($null -ne $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        # Windows PowerShell 5.1 no llena ErrorDetails con application/problem+json:
        # el cuerpo se lee del stream de la respuesta.
        $texto = $null
        if ($null -ne $_.ErrorDetails -and -not [string]::IsNullOrWhiteSpace($_.ErrorDetails.Message)) {
            $texto = $_.ErrorDetails.Message
        }
        elseif ($null -ne $_.Exception.Response -and $_.Exception.Response -is [System.Net.WebResponse]) {
            try {
                $lector = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream(), [System.Text.Encoding]::UTF8)
                $texto = $lector.ReadToEnd()
            }
            catch { $texto = $null }
        }
        $cuerpo = $null
        if (-not [string]::IsNullOrWhiteSpace($texto)) {
            try { $cuerpo = $texto | ConvertFrom-Json } catch { $cuerpo = $null }
        }
        return @{ Status = $status; Body = $cuerpo }
    }
}

# Imprime un paso y devuelve si se cumplio.
function Write-Paso {
    param([string]$Paso, [string]$Esperado, [string]$Obtenido, [bool]$Ok)
    $marca = if ($Ok) { 'OK   ' } else { 'FALLO' }
    Write-Host ("  [{0}] {1}" -f $marca, $Paso)
    Write-Host ("          esperado: {0}" -f $Esperado)
    Write-Host ("          obtenido: {0}" -f $Obtenido)
    return $Ok
}

function Get-Rango { param([string]$Estado) return [array]::IndexOf($script:Estados, $Estado) }

function Iniciar-Sesion {
    param([string]$Email)
    foreach ($contrasena in $Contrasenas) {
        $res = Invoke-Http -Method 'POST' -Url "$GatewayUrl/api/auth/login" -Body @{ email = $Email; contrasena = $contrasena }
        if ($res.Status -eq 200 -and $null -ne $res.Body) { return $res.Body.token }
    }
    return $null
}

function Get-MiCredito {
    param([string]$Token)
    $res = Invoke-Http -Method 'GET' -Url "$CreditoUrl/api/credito/mi-credito" -Token $Token
    if ($res.Status -ne 200) { return $null }
    return $res.Body
}

function Get-Orden {
    param([string]$OrdenId)
    $res = Invoke-Http -Method 'GET' -Url "$BaseUrl/api/ordenes/$OrdenId"
    if ($res.Status -ne 200) { return $null }
    return $res.Body
}

function Get-Disponible {
    $res = Invoke-Http -Method 'GET' -Url "$InventarioUrl/api/inventario/existencias/$Sku"
    if ($res.Status -ne 200) { return -1 }
    $fila = @($res.Body) | Where-Object { $_.ubicacionId -eq $UbicacionId } | Select-Object -First 1
    if ($null -eq $fila) { return -1 }
    return [int]$fila.disponible
}

function Sembrar-Producto {
    $siembra = Invoke-Http -Method 'PUT' -Url "$InventarioUrl/api/inventario/existencias" -Body @{
        sku = $Sku; ubicacionId = $UbicacionId; tipoUbicacion = 'CENTRO_DISTRIBUCION'; cantidadFisica = 500
    }
    $producto = Invoke-Http -Method 'PUT' -Url "$InventarioUrl/api/inventario/productos/$Sku" -Body @{
        nombre = 'Producto de prueba credito'; categoria = 'Pruebas'; precio = $PrecioUnitario; icono = 'inventory_2'; activo = $true
    }
    return ($siembra.Status -in 200, 201) -and ($producto.Status -in 200, 201)
}

# Cuerpo de una orden a credito con impuestos 0: el total es cantidad x 100.
function Cuerpo-Orden {
    param([string]$ClienteId, [int]$Cantidad)
    return @{
        clienteId        = $ClienteId
        canal            = 'WEB'
        metodoEntrega    = 'DOMICILIO'
        moneda           = 'GTQ'
        medioPago        = 'CREDITO'
        impuestos        = 0.0
        direccionEntrega = @{
            pais = 'GT'; departamento = 'Guatemala'; municipio = 'Guatemala'
            direccion = '5a avenida 3-21 zona 2'; referencia = 'Arnes de credito'
        }
        lineas           = @(@{
            sku = $Sku; descripcion = 'Producto de prueba credito'; cantidad = $Cantidad; precioUnitario = $PrecioUnitario
        })
    }
}

# Crea y confirma la orden. Devuelve la orden creada o $null.
function Crear-OrdenCredito {
    param([string]$ClienteId, [int]$Cantidad, [string]$Token)
    $creada = Invoke-Http -Method 'POST' -Url "$BaseUrl/api/ordenes" -Body (Cuerpo-Orden $ClienteId $Cantidad) -Token $Token
    if ($creada.Status -ne 201) { return $null }
    $confirmada = Invoke-Http -Method 'POST' -Url "$BaseUrl/api/ordenes/$($creada.Body.id)/confirmar" -Body @{ motivo = 'arnes de credito' } -Token $Token
    if ($confirmada.Status -ne 200) { return $null }
    return $creada.Body
}

# Espera a que la orden llegue a $Objetivo (o a un estado posterior de la ruta, o
# terminal si $Objetivo es terminal). Devuelve la ultima orden vista.
function Esperar-Orden {
    param([string]$OrdenId, [string]$Objetivo, [int]$Segundos = $TimeoutSeg)
    $limite = $script:Reloj.ElapsedMilliseconds + ($Segundos * 1000)
    $ultima = $null
    while ($script:Reloj.ElapsedMilliseconds -lt $limite) {
        $orden = Get-Orden $OrdenId
        if ($null -ne $orden) {
            $ultima = $orden
            if ($orden.estado -eq $Objetivo) { return $orden }
            if ($script:Terminales -contains $orden.estado) { return $orden }
            if (($script:Terminales -notcontains $Objetivo) -and (Get-Rango $orden.estado) -ge (Get-Rango $Objetivo)) { return $orden }
        }
        Start-Sleep -Milliseconds 500
    }
    return $ultima
}

# Repite $Condicion (scriptblock) hasta que devuelva $true o venza el plazo.
function Esperar-Condicion {
    param([scriptblock]$Condicion, [int]$Segundos = $TimeoutSeg)
    $limite = $script:Reloj.ElapsedMilliseconds + ($Segundos * 1000)
    while ($script:Reloj.ElapsedMilliseconds -lt $limite) {
        if (& $Condicion) { return $true }
        Start-Sleep -Milliseconds 1000
    }
    return $false
}

function Contar-Ordenes {
    $salida = docker compose -f $compose exec -T db-operacional psql -U $DbUsuario -d sgo_ordenes -Atc 'select count(*) from ordenes.orden' 2>$null | Where-Object { $_ -is [string] }
    if ($LASTEXITCODE -ne 0) { return -1 }
    return [int]($salida | Select-Object -First 1)
}

function Ultimo-Motivo {
    param([object]$Orden)
    if ($null -eq $Orden -or $null -eq $Orden.transiciones) { return '' }
    return (@($Orden.transiciones) | Select-Object -Last 1).motivo
}

function Motivo-OrdenCancelada {
    param([string]$OrdenId)
    $res = Invoke-Http -Method 'GET' -Url "$AuditoriaUrl/auditoria/agregados/$OrdenId"
    if ($res.Status -ne 200) { return $null }
    $evento = @($res.Body.items) | Where-Object { $_.eventType -eq 'OrdenCancelada' } | Select-Object -Last 1
    if ($null -eq $evento) { return $null }
    return $evento.data.motivo
}

function Resultado {
    param([string]$Nombre, [bool[]]$Pasos)
    $ok = ($Pasos.Count -gt 0) -and (@($Pasos | Where-Object { -not $_ }).Count -eq 0)
    $texto = if ($ok) { 'APROBADO' } else { 'FALLIDO' }
    Write-Host ("  RESULTADO {0}: {1}" -f $Nombre, $texto)
    Write-Host ""
    return $ok
}

# ---------------------------------------------------------------------------
# Escenarios
# ---------------------------------------------------------------------------

function Escenario-Reiniciar {
    Write-Host "Escenario reiniciar"
    $res = Invoke-Http -Method 'POST' -Url "$CreditoUrl/interno/credito/demo/reiniciar"
    $detalle = if ($null -ne $res.Body) { "cuentas=$($res.Body.cuentasBorradas) decisiones=$($res.Body.decisionesBorradas) lineas=$($res.Body.lineasReiniciadas)" } else { '' }
    $p1 = Write-Paso 'Reinicio de demo' '200' "$($res.Status) $detalle" ($res.Status -eq 200)
    return Resultado 'reiniciar' @($p1)
}

function Escenario-Aprobado {
    Write-Host "Escenario aprobado (cliente1)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente1@sgo.local'
    $pasos += Write-Paso 'Inicio de sesion cliente1' 'token' $(if ($token) { 'token recibido' } else { 'sin token' }) ($null -ne $token)
    if (-not $token) { return Resultado 'aprobado' $pasos }

    $antes = Get-MiCredito $token
    $saldoAntes = [decimal]$antes.saldoUtilizado
    $orden = Crear-OrdenCredito 'cliente1@sgo.local' 3 $token
    $pasos += Write-Paso 'Crear y confirmar orden a credito (3 x Q100)' 'orden creada' $(if ($orden) { $orden.id } else { 'no creada' }) ($null -ne $orden)
    if (-not $orden) { return Resultado 'aprobado' $pasos }

    $final = Esperar-Orden $orden.id 'CONFIRMADA'
    $estado = if ($final) { $final.estado } else { 'desconocido' }
    $pasos += Write-Paso 'La orden llega al menos a CONFIRMADA' 'CONFIRMADA o posterior' $estado `
        (($script:Terminales -notcontains $estado) -and (Get-Rango $estado) -ge (Get-Rango 'CONFIRMADA'))
    $pasos += Write-Paso 'Documento fiscal certificado' 'datosFiscales presente' $(if ($final.datosFiscales) { $final.datosFiscales.documentoFiscalId } else { 'sin datos fiscales' }) ($null -ne $final.datosFiscales)
    $pasos += Write-Paso 'Referencia del cargo en pagoId' 'CRE-...' "$($final.pagoId)" ("$($final.pagoId)".StartsWith('CRE-'))

    $despues = Get-MiCredito $token
    $total = [decimal]$orden.total
    $saldoDespues = [decimal]$despues.saldoUtilizado
    $pasos += Write-Paso 'Saldo utilizado aumenta en el total de la orden' "$saldoAntes + $total" "$saldoDespues" ($saldoDespues -eq ($saldoAntes + $total))

    $compra = @($despues.compras) | Where-Object { $_.ordenId -eq $orden.id } | Select-Object -First 1
    $dias = $null
    if ($compra) { $dias = [int][Math]::Round(([DateTimeOffset]$compra.venceEn - [DateTimeOffset]$compra.emitidaEn).TotalDays) }
    $pasos += Write-Paso 'Mi credito muestra la compra con vencimiento a 30 dias' 'Por pagar, 30 dias' `
        $(if ($compra) { "$($compra.etiquetaEstado), $dias dias, restan $($compra.diasRestantes)" } else { 'compra no listada' }) `
        (($null -ne $compra) -and $dias -eq 30 -and $compra.estado -eq 'VIGENTE')
    return Resultado 'aprobado' $pasos
}

function Escenario-Rechazo {
    param([string]$Nombre, [string]$Email, [int]$Cantidad, [string]$Fragmento)
    Write-Host "Escenario $Nombre ($Email)"
    $pasos = @()
    $token = Iniciar-Sesion $Email
    $pasos += Write-Paso "Inicio de sesion $Email" 'token' $(if ($token) { 'token recibido' } else { 'sin token' }) ($null -ne $token)
    if (-not $token) { return Resultado $Nombre $pasos }

    $antes = Get-MiCredito $token
    $saldoAntes = [decimal]$antes.saldoUtilizado
    $stockAntes = Get-Disponible
    $orden = Crear-OrdenCredito $Email $Cantidad $token
    $pasos += Write-Paso "Crear y confirmar orden a credito ($Cantidad x Q100)" 'orden creada' $(if ($orden) { $orden.id } else { 'no creada' }) ($null -ne $orden)
    if (-not $orden) { return Resultado $Nombre $pasos }

    $final = Esperar-Orden $orden.id 'RECHAZADA'
    $pasos += Write-Paso 'La orden termina RECHAZADA' 'RECHAZADA' "$($final.estado)" ($final.estado -eq 'RECHAZADA')
    $motivo = Ultimo-Motivo $final
    $pasos += Write-Paso 'Detalle visible del rechazo' "contiene '$Fragmento'" "$motivo" ("$motivo" -like "*$Fragmento*")
    $enAuditoria = Esperar-Condicion { (Motivo-OrdenCancelada $orden.id) -eq 'CREDITO_RECHAZADO' } 15
    $pasos += Write-Paso 'Motivo en OrdenCancelada (Auditoria)' 'CREDITO_RECHAZADO' "$(Motivo-OrdenCancelada $orden.id)" $enAuditoria

    $liberada = Esperar-Condicion { (Get-Disponible) -eq $stockAntes } 20
    $pasos += Write-Paso 'La reserva se libera' "disponible = $stockAntes" "$(Get-Disponible)" $liberada

    $despues = Get-MiCredito $token
    $pasos += Write-Paso 'El saldo utilizado no cambia' "$saldoAntes" "$($despues.saldoUtilizado)" ([decimal]$despues.saldoUtilizado -eq $saldoAntes)
    return Resultado $Nombre $pasos
}

function Escenario-Suplantacion {
    Write-Host "Escenario suplantacion (token de cliente4, clienteId de cliente1)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente4@sgo.local'
    $pasos += Write-Paso 'Inicio de sesion cliente4' 'token' $(if ($token) { 'token recibido' } else { 'sin token' }) ($null -ne $token)
    $antes = Contar-Ordenes
    $res = Invoke-Http -Method 'POST' -Url "$BaseUrl/api/ordenes" -Body (Cuerpo-Orden 'cliente1@sgo.local' 1) -Token $token
    $pasos += Write-Paso 'POST /api/ordenes responde 403 cliente_no_coincide' '403 cliente_no_coincide' "$($res.Status) $($res.Body.codigo)" `
        (($res.Status -eq 403) -and ($res.Body.codigo -eq 'cliente_no_coincide'))
    $despues = Contar-Ordenes
    $pasos += Write-Paso 'No se crea ninguna orden' "ordenes = $antes" "ordenes = $despues" (($antes -ge 0) -and ($despues -eq $antes))
    return Resultado 'suplantacion' $pasos
}

function Escenario-SinSesion {
    Write-Host "Escenario sin-sesion"
    $res = Invoke-Http -Method 'POST' -Url "$BaseUrl/api/ordenes" -Body (Cuerpo-Orden 'cliente1@sgo.local' 1)
    $p1 = Write-Paso 'POST /api/ordenes sin token responde 401 credito_requiere_sesion' '401 credito_requiere_sesion' "$($res.Status) $($res.Body.codigo)" `
        (($res.Status -eq 401) -and ($res.Body.codigo -eq 'credito_requiere_sesion'))
    return Resultado 'sin-sesion' @($p1)
}

function Escenario-Cancelacion {
    Write-Host "Escenario cancelacion (cliente2)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente2@sgo.local'
    $pasos += Write-Paso 'Inicio de sesion cliente2' 'token' $(if ($token) { 'token recibido' } else { 'sin token' }) ($null -ne $token)
    if (-not $token) { return Resultado 'cancelacion' $pasos }

    $saldoAntes = [decimal](Get-MiCredito $token).saldoUtilizado
    $orden = Crear-OrdenCredito 'cliente2@sgo.local' 5 $token
    $pasos += Write-Paso 'Crear y confirmar orden a credito (5 x Q100)' 'orden creada' $(if ($orden) { $orden.id } else { 'no creada' }) ($null -ne $orden)
    if (-not $orden) { return Resultado 'cancelacion' $pasos }

    $confirmada = Esperar-Orden $orden.id 'CONFIRMADA_PENDIENTE_FISCAL'
    $pasos += Write-Paso 'La orden se confirma' 'CONFIRMADA_PENDIENTE_FISCAL o posterior' "$($confirmada.estado)" `
        (($script:Terminales -notcontains $confirmada.estado) -and (Get-Rango $confirmada.estado) -ge (Get-Rango 'CONFIRMADA_PENDIENTE_FISCAL'))
    $conCargo = Esperar-Condicion { [decimal](Get-MiCredito $token).saldoUtilizado -eq ($saldoAntes + [decimal]$orden.total) } 15
    $pasos += Write-Paso 'El cargo aparta el cupo' "$($saldoAntes + [decimal]$orden.total)" "$((Get-MiCredito $token).saldoUtilizado)" $conCargo

    $cancelar = Invoke-Http -Method 'POST' -Url "$BaseUrl/api/ordenes/$($orden.id)/cancelar" -Body @{ clienteId = 'cliente2@sgo.local'; motivo = 'Arnes de credito' } -Token $token
    $pasos += Write-Paso 'El cliente cancela la orden' '200' "$($cancelar.Status)" ($cancelar.Status -eq 200)

    $anulada = Esperar-Condicion {
        $credito = Get-MiCredito $token
        $compra = @($credito.compras) | Where-Object { $_.ordenId -eq $orden.id } | Select-Object -First 1
        ($null -ne $compra) -and ($compra.estado -eq 'ANULADA') -and ([decimal]$credito.saldoUtilizado -eq $saldoAntes)
    } 30
    $credito = Get-MiCredito $token
    $compra = @($credito.compras) | Where-Object { $_.ordenId -eq $orden.id } | Select-Object -First 1
    $pasos += Write-Paso 'La cuenta queda ANULADA y el cupo vuelve' "ANULADA, saldo $saldoAntes" "$($compra.estado), saldo $($credito.saldoUtilizado)" $anulada
    return Resultado 'cancelacion' $pasos
}

function Escenario-Timeout {
    Write-Host "Escenario timeout (credito-api detenido, cliente1)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente1@sgo.local'
    $pasos += Write-Paso 'Inicio de sesion cliente1' 'token' $(if ($token) { 'token recibido' } else { 'sin token' }) ($null -ne $token)
    if (-not $token) { return Resultado 'timeout' $pasos }

    $saldoAntes = [decimal](Get-MiCredito $token).saldoUtilizado
    $stockAntes = Get-Disponible

    # docker escribe su progreso en stderr; se descarta para no ensuciar la salida.
    $null = docker compose -f $compose stop credito-api 2>&1
    $pasos += Write-Paso 'Detener credito-api' 'detenido' $(if ($LASTEXITCODE -eq 0) { 'detenido' } else { "codigo $LASTEXITCODE" }) ($LASTEXITCODE -eq 0)

    try {
        $inicio = $script:Reloj.ElapsedMilliseconds
        $orden = Crear-OrdenCredito 'cliente1@sgo.local' 2 $token
        $pasos += Write-Paso 'Crear y confirmar orden a credito (2 x Q100)' 'orden creada' $(if ($orden) { $orden.id } else { 'no creada' }) ($null -ne $orden)
        if ($orden) {
            $final = Esperar-Orden $orden.id 'RECHAZADA' 45
            $segundos = [Math]::Round(($script:Reloj.ElapsedMilliseconds - $inicio) / 1000.0, 1)
            $pasos += Write-Paso 'RECHAZADA antes de 45 s' 'RECHAZADA, < 45 s' "$($final.estado), $segundos s" (($final.estado -eq 'RECHAZADA') -and $segundos -lt 45)
            $enAuditoria = Esperar-Condicion { (Motivo-OrdenCancelada $orden.id) -eq 'CREDITO_SIN_RESPUESTA' } 15
            $pasos += Write-Paso 'Motivo en OrdenCancelada (Auditoria)' 'CREDITO_SIN_RESPUESTA' "$(Motivo-OrdenCancelada $orden.id)" $enAuditoria
            $pasos += Write-Paso 'Detalle visible del rechazo' 'No pudimos confirmar tu credito a tiempo...' "$(Ultimo-Motivo $final)" ("$(Ultimo-Motivo $final)" -like 'No pudimos confirmar tu cr*dito a tiempo*')
            $liberada = Esperar-Condicion { (Get-Disponible) -eq $stockAntes } 20
            $pasos += Write-Paso 'Stock liberado' "disponible = $stockAntes" "$(Get-Disponible)" $liberada
        }
    }
    finally {
        $null = docker compose -f $compose start credito-api 2>&1
    }

    $arriba = Esperar-Condicion { (Invoke-Http -Method 'GET' -Url "$CreditoUrl/health/live").Status -eq 200 } 90
    $pasos += Write-Paso 'Levantar credito-api' 'sano' $(if ($arriba) { 'sano' } else { 'sin respuesta' }) $arriba

    # Credito procesa la evaluacion pendiente (aprueba y aparta) y despues la
    # cancelacion (anula y devuelve): el saldo debe volver al de inicio. Antes de
    # comparar se espera la decision registrada y la cola credito.ordenes vacia.
    if ($orden) {
        $decidida = Esperar-Condicion { "$(Consultar-Sql 'db-finanzas' 'sgo_credito' "select count(*) from credito.decision_credito where orden_id = '$($orden.id)'")" -eq '1' } 60
        $pasos += Write-Paso 'Credito registra la decision de la orden' 'decision = 1' $(if ($decidida) { 'decision = 1' } else { 'sin decision' }) $decidida
    }
    $vacia = Esperar-Condicion { (Mensajes-Cola 'credito.ordenes') -eq 0 } 60
    $pasos += Write-Paso 'Cola credito.ordenes sin pendientes' '0' "$(Mensajes-Cola 'credito.ordenes')" $vacia
    $igual = Esperar-Condicion { [decimal](Get-MiCredito $token).saldoUtilizado -eq $saldoAntes } 60
    $pasos += Write-Paso 'El saldo utilizado termina igual que al inicio' "$saldoAntes" "$((Get-MiCredito $token).saldoUtilizado)" $igual
    return Resultado 'timeout' $pasos
}

function Mensajes-Cola {
    param([string]$Cola)
    $salida = docker compose -f $compose exec -T rabbitmq rabbitmqctl list_queues -q name messages 2>$null | Where-Object { $_ -is [string] }
    if ($LASTEXITCODE -ne 0) { return -1 }
    $fila = @($salida) | Where-Object { ($_ -split '\s+')[0] -eq $Cola } | Select-Object -First 1
    if ($null -eq $fila) { return -1 }
    return [int](($fila -split '\s+')[1])
}

# ---------------------------------------------------------------------------
# Cobranza (bloque 3): IVA, abonos, rechazo, pendiente, recordatorio y vencimiento
# ---------------------------------------------------------------------------

# Token de la boveda de Pagos para el cliente de la sesion (tarjeta de prueba Visa).
function Tokenizar-Tarjeta {
    param([string]$Token)
    $res = Invoke-Http -Method 'POST' -Url "$PagosUrl/api/pagos/tokens" -Token $Token -Body @{
        numero = '4111111111111111'; mesExp = 12; anioExp = (Get-Date).Year + 3; cvv = '123'
        titular = 'Cliente Arnes Credito'; guardar = $false
    }
    if ($res.Status -ne 201 -or $null -eq $res.Body) { return $null }
    return $res.Body.token
}

function Get-Compra {
    param([string]$Token, [string]$OrdenId)
    $credito = Get-MiCredito $Token
    if ($null -eq $credito) { return $null }
    return @($credito.compras) | Where-Object { $_.ordenId -eq $OrdenId } | Select-Object -First 1
}

# Compra a credito confirmada; devuelve @{ Orden; Compra } o $null.
function Compra-Confirmada {
    param([string]$Email, [int]$Cantidad, [string]$Token)
    $orden = Crear-OrdenCredito $Email $Cantidad $Token
    if ($null -eq $orden) { return $null }
    $final = Esperar-Orden $orden.id 'CONFIRMADA_PENDIENTE_FISCAL'
    if ($null -eq $final -or ($script:Terminales -contains $final.estado)) { return $null }
    $null = Esperar-Condicion { $null -ne (Get-Compra $Token $orden.id) } 15
    return @{ Orden = $orden; Compra = (Get-Compra $Token $orden.id) }
}

function Crear-Abono {
    param([string]$Token, [string]$CuentaId, [decimal]$Monto, [string]$TokenPago)
    return Invoke-Http -Method 'POST' -Url "$CreditoUrl/api/credito/abonos" -Token $Token -Body @{
        cuentaId = $CuentaId; monto = $Monto.ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture); tokenMedioPago = $TokenPago
    }
}

# Espera a que el abono salga de SOLICITADO; devuelve el ultimo abono visto.
function Esperar-Abono {
    param([string]$Token, [string]$AbonoId, [int]$Segundos = $TimeoutSeg)
    $limite = $script:Reloj.ElapsedMilliseconds + ($Segundos * 1000)
    $ultimo = $null
    while ($script:Reloj.ElapsedMilliseconds -lt $limite) {
        $res = Invoke-Http -Method 'GET' -Url "$CreditoUrl/api/credito/abonos/$AbonoId" -Token $Token
        if ($res.Status -eq 200) {
            $ultimo = $res.Body
            if ($ultimo.estado -ne 'SOLICITADO') { return $ultimo }
        }
        Start-Sleep -Milliseconds 1000
    }
    return $ultimo
}

function Contar-EventoAuditoria {
    param([string]$AggregateId, [string]$EventType)
    $res = Invoke-Http -Method 'GET' -Url "$AuditoriaUrl/auditoria/agregados/$AggregateId"
    if ($res.Status -ne 200) { return 0 }
    return @(@($res.Body.items) | Where-Object { $_.eventType -eq $EventType }).Count
}

function Evento-Auditoria {
    param([string]$AggregateId, [string]$EventType)
    $res = Invoke-Http -Method 'GET' -Url "$AuditoriaUrl/auditoria/agregados/$AggregateId"
    if ($res.Status -ne 200) { return $null }
    return @($res.Body.items) | Where-Object { $_.eventType -eq $EventType } | Select-Object -Last 1
}

function Consultar-Sql {
    param([string]$Servicio, [string]$Base, [string]$Sql)
    $salida = docker compose -f $compose exec -T $Servicio psql -U $DbUsuario -d $Base -Atc $Sql 2>$null | Where-Object { $_ -is [string] }
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($salida | Select-Object -First 1)
}

function Modo-PayKlik {
    param([string]$Modo)
    return (Invoke-Http -Method 'PUT' -Url "$PayKlikUrl/admin/modo" -Body @{ modo = $Modo }).Status -eq 200
}

function Escenario-Iva {
    Write-Host "Escenario iva (orden por API, total = suma de lineas)"
    $pasos = @()
    $cuerpo = Cuerpo-Orden 'iva-arnes@sgo.local' 7
    $cuerpo.medioPago = 'TRANSFERENCIA'
    # Ordenes debe ignorar el impuesto que mande el cliente.
    $cuerpo.impuestos = 999.99
    $res = Invoke-Http -Method 'POST' -Url "$BaseUrl/api/ordenes" -Body $cuerpo
    $pasos += Write-Paso 'Crear orden de 7 x Q100.00' '201' "$($res.Status)" ($res.Status -eq 201)
    if ($res.Status -ne 201) { return Resultado 'iva' $pasos }
    $orden = Get-Orden $res.Body.id
    $pasos += Write-Paso 'Total igual a la suma de las lineas' '700.00' "$($orden.total)" ([decimal]$orden.total -eq 700)
    $pasos += Write-Paso 'IVA incluido calculado en el servidor (impuestos del request ignorados)' '75.00' "$($orden.impuestos)" ([decimal]$orden.impuestos -eq 75)
    $pasos += Write-Paso 'Base sin IVA' '625.00' "$($orden.subtotal)" ([decimal]$orden.subtotal -eq 625)
    return Resultado 'iva' $pasos
}

function Escenario-AbonoParcial {
    Write-Host "Escenario abono-parcial (cliente1)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente1@sgo.local'
    $pasos += Write-Paso 'Inicio de sesion cliente1' 'token' $(if ($token) { 'token recibido' } else { 'sin token' }) ($null -ne $token)
    if (-not $token) { return Resultado 'abono-parcial' $pasos }

    $script:DisponibleAntesCompra = [decimal](Get-MiCredito $token).disponible
    $compra = Compra-Confirmada 'cliente1@sgo.local' 5 $token
    $pasos += Write-Paso 'Compra a credito de Q500.00 confirmada' 'cuenta por cobrar' $(if ($compra) { $compra.Orden.id } else { 'sin cuenta' }) ($null -ne $compra -and $null -ne $compra.Compra)
    if (-not $compra -or -not $compra.Compra) { return Resultado 'abono-parcial' $pasos }
    $script:CompraParcial = $compra

    $disponibleAntes = [decimal](Get-MiCredito $token).disponible
    $tokenPago = Tokenizar-Tarjeta $token
    $res = Crear-Abono $token $compra.Compra.cuentaId 200 $tokenPago
    $pasos += Write-Paso 'Abono de Q200.00 aceptado' '202' "$($res.Status)" ($res.Status -eq 202)
    if ($res.Status -ne 202) { return Resultado 'abono-parcial' $pasos }

    $abono = Esperar-Abono $token $res.Body.id
    $pasos += Write-Paso 'El abono se aplica' 'APLICADO' "$($abono.estado) - $($abono.mensaje)" ($abono.estado -eq 'APLICADO')
    $despues = Get-Compra $token $compra.Orden.id
    $pasos += Write-Paso 'Baja el saldo de la cuenta' '300.00' "$($despues.saldoPendiente)" ([decimal]$despues.saldoPendiente -eq 300)
    $disponible = [decimal](Get-MiCredito $token).disponible
    $pasos += Write-Paso 'Sube el disponible en el mismo monto' "$($disponibleAntes + 200)" "$disponible" ($disponible -eq ($disponibleAntes + 200))
    return Resultado 'abono-parcial' $pasos
}

function Escenario-PagoTotal {
    Write-Host "Escenario pago-total (cliente1)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente1@sgo.local'
    if ($null -eq $script:CompraParcial) {
        $null = Escenario-AbonoParcial
    }
    $compra = $script:CompraParcial
    $pasos += Write-Paso 'Compra con saldo pendiente' 'cuenta' $(if ($compra) { $compra.Orden.id } else { 'sin compra' }) ($null -ne $compra)
    if (-not $compra) { return Resultado 'pago-total' $pasos }

    $saldo = [decimal](Get-Compra $token $compra.Orden.id).saldoPendiente
    $res = Crear-Abono $token $compra.Compra.cuentaId $saldo (Tokenizar-Tarjeta $token)
    $pasos += Write-Paso "Pago del resto (Q$saldo)" '202' "$($res.Status)" ($res.Status -eq 202)
    if ($res.Status -ne 202) { return Resultado 'pago-total' $pasos }

    $abono = Esperar-Abono $token $res.Body.id
    $pasos += Write-Paso 'Mensaje de compra pagada' 'Tu compra quedo pagada' "$($abono.titulo)" ($abono.estado -eq 'APLICADO' -and "$($abono.titulo)" -like '*pagada*')
    $final = Get-Compra $token $compra.Orden.id
    $pasos += Write-Paso 'La cuenta queda PAGADA' 'PAGADA' "$($final.estado)" ($final.estado -eq 'PAGADA')
    $null = Esperar-Condicion { $null -ne (Evento-Auditoria $compra.Compra.cuentaId 'CuentaCreditoPagada') } 15
    $evento = Evento-Auditoria $compra.Compra.cuentaId 'CuentaCreditoPagada'
    $pasos += Write-Paso 'Pago puntual (CuentaCreditoPagada en Auditoria)' 'puntual = True' "puntual = $($evento.data.puntual)" ($null -ne $evento -and $evento.data.puntual -eq $true)
    $disponible = [decimal](Get-MiCredito $token).disponible
    $pasos += Write-Paso 'El cupo vuelve completo' "$($script:DisponibleAntesCompra)" "$disponible" ($disponible -eq $script:DisponibleAntesCompra)
    return Resultado 'pago-total' $pasos
}

function Escenario-PagoRechazado {
    Write-Host "Escenario pago-rechazado (cliente2, PayKlik en modo RECHAZO)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente2@sgo.local'
    $compra = Compra-Confirmada 'cliente2@sgo.local' 2 $token
    $pasos += Write-Paso 'Compra a credito de Q200.00' 'cuenta' $(if ($compra) { $compra.Orden.id } else { 'sin cuenta' }) ($null -ne $compra -and $null -ne $compra.Compra)
    if (-not $compra -or -not $compra.Compra) { return Resultado 'pago-rechazado' $pasos }

    try {
        $pasos += Write-Paso 'PayKlik rechaza todo (modo RECHAZO)' 'modo cambiado' $(if (Modo-PayKlik 'RECHAZO') { 'modo cambiado' } else { 'sin cambio' }) $true
        $res = Crear-Abono $token $compra.Compra.cuentaId 200 (Tokenizar-Tarjeta $token)
        $abono = $null
        if ($res.Status -eq 202) { $abono = Esperar-Abono $token $res.Body.id }
        $pasos += Write-Paso 'El abono queda RECHAZADO' 'RECHAZADO' "$($abono.estado)" ($null -ne $abono -and $abono.estado -eq 'RECHAZADO')
        $pasos += Write-Paso 'Mensaje sin cargo' 'no se hizo ningun cargo' "$($abono.titulo). $($abono.mensaje)" ("$($abono.mensaje)" -like '*ning*n cargo*')
    }
    finally {
        $null = Modo-PayKlik 'NORMAL'
    }

    $final = Get-Compra $token $compra.Orden.id
    $pasos += Write-Paso 'El saldo no cambia' '200.00' "$($final.saldoPendiente)" ([decimal]$final.saldoPendiente -eq 200)
    $script:CompraRechazo = $compra
    return Resultado 'pago-rechazado' $pasos
}

function Escenario-PagoPendiente {
    Write-Host "Escenario pago-pendiente (cliente2, payklik-mock detenido)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente2@sgo.local'
    $compra = Compra-Confirmada 'cliente2@sgo.local' 2 $token
    $pasos += Write-Paso 'Compra a credito de Q200.00' 'cuenta' $(if ($compra) { $compra.Orden.id } else { 'sin cuenta' }) ($null -ne $compra -and $null -ne $compra.Compra)
    if (-not $compra -or -not $compra.Compra) { return Resultado 'pago-pendiente' $pasos }

    $abono = $null
    try {
        $null = docker compose -f $compose stop payklik-mock 2>&1
        $res = Crear-Abono $token $compra.Compra.cuentaId 200 (Tokenizar-Tarjeta $token)
        if ($res.Status -eq 202) { $abono = Esperar-Abono $token $res.Body.id 90 }
        $pasos += Write-Paso 'Con el proveedor caido el abono queda PENDIENTE' 'PENDIENTE' "$($abono.estado) - $($abono.mensaje)" ($null -ne $abono -and $abono.estado -eq 'PENDIENTE')

        $otro = Crear-Abono $token $compra.Compra.cuentaId 50 (Tokenizar-Tarjeta $token)
        $pasos += Write-Paso 'No se puede crear otro abono' '409 abono_en_curso' "$($otro.Status) $($otro.Body.codigo)" (($otro.Status -eq 409) -and ($otro.Body.codigo -eq 'abono_en_curso'))
    }
    finally {
        $null = docker compose -f $compose start payklik-mock 2>&1
    }

    if ($null -eq $abono) { return Resultado 'pago-pendiente' $pasos }
    $null = Esperar-Condicion { (Invoke-Http -Method 'GET' -Url "$PayKlikUrl/health/live").Status -eq 200 } 90

    # El disyuntor puede seguir abierto unos segundos: se reintenta hasta que aplique.
    $null = Esperar-Condicion {
        $r = Invoke-Http -Method 'POST' -Url "$CreditoUrl/api/credito/abonos/$($abono.id)/reintentar" -Token $token -Body @{ tokenMedioPago = (Tokenizar-Tarjeta $token) }
        if ($r.Status -ne 202) { return $false }
        $script:abonoReintento = Esperar-Abono $token $abono.id 60
        if ($script:abonoReintento.estado -eq 'APLICADO') { return $true }
        Start-Sleep -Seconds 5
        return $false
    } 120
    $final = $script:abonoReintento
    $pasos += Write-Paso 'Al reintentar queda APLICADO (mismo abono)' "APLICADO, id $($abono.id)" "$($final.estado), id $($final.id)" ($null -ne $final -and $final.estado -eq 'APLICADO' -and $final.id -eq $abono.id)

    $cargos = Consultar-Sql 'db-finanzas' 'sgo_pagos' "select count(*) from pagos.intento_pago i join pagos.pago p on p.id = i.pago_id where p.orden_id = '$($abono.referencia)' and i.estado = 'EXITOSO'"
    $pagos = Consultar-Sql 'db-finanzas' 'sgo_pagos' "select count(*) from pagos.pago where orden_id = '$($abono.referencia)'"
    $pasos += Write-Paso 'En Pagos hay un solo pago y un solo cargo aprobado para el abono' 'pagos = 1, cargos = 1' "pagos = $pagos, cargos = $cargos" (("$pagos" -eq '1') -and ("$cargos" -eq '1'))
    return Resultado 'pago-pendiente' $pasos
}

function Escenario-Recordatorio {
    Write-Host "Escenario recordatorio (cliente1, vence en 2 dias)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente1@sgo.local'
    $compra = Compra-Confirmada 'cliente1@sgo.local' 1 $token
    $pasos += Write-Paso 'Compra a credito de Q100.00' 'cuenta' $(if ($compra) { $compra.Orden.id } else { 'sin cuenta' }) ($null -ne $compra -and $null -ne $compra.Compra)
    if (-not $compra -or -not $compra.Compra) { return Resultado 'recordatorio' $pasos }

    $fijar = Invoke-Http -Method 'POST' -Url "$CreditoUrl/interno/credito/demo/cuentas/$($compra.Orden.id)/vencimiento" -Body @{ diasDesdeHoy = 2 }
    $pasos += Write-Paso 'Vencimiento a 2 dias' '200' "$($fijar.Status)" ($fijar.Status -eq 200)
    $primero = Invoke-Http -Method 'POST' -Url "$CreditoUrl/interno/credito/demo/procesar-vencimientos"
    $segundo = Invoke-Http -Method 'POST' -Url "$CreditoUrl/interno/credito/demo/procesar-vencimientos"
    $pasos += Write-Paso 'Procesar dos veces' 'recordadas >= 1 y luego 0' "$($primero.Body.recordadas) y $($segundo.Body.recordadas)" (([int]$primero.Body.recordadas -ge 1) -and ([int]$segundo.Body.recordadas -eq 0))

    $null = Esperar-Condicion { (Contar-EventoAuditoria $compra.Compra.cuentaId 'CuentaCreditoPorVencer') -ge 1 } 15
    Start-Sleep -Seconds 3
    $cantidad = Contar-EventoAuditoria $compra.Compra.cuentaId 'CuentaCreditoPorVencer'
    $pasos += Write-Paso 'Un solo CuentaCreditoPorVencer en Auditoria' '1' "$cantidad" ($cantidad -eq 1)
    $actual = Get-Compra $token $compra.Orden.id
    $pasos += Write-Paso 'Mi credito la muestra como "Vence pronto"' 'Vence pronto' "$($actual.etiquetaEstado)" ($actual.etiquetaEstado -eq 'Vence pronto')
    return Resultado 'recordatorio' $pasos
}

function Escenario-Vencimiento {
    Write-Host "Escenario vencimiento (cliente2)"
    $pasos = @()
    $token = Iniciar-Sesion 'cliente2@sgo.local'
    $compra = Compra-Confirmada 'cliente2@sgo.local' 1 $token
    $pasos += Write-Paso 'Compra a credito de Q100.00' 'cuenta' $(if ($compra) { $compra.Orden.id } else { 'sin cuenta' }) ($null -ne $compra -and $null -ne $compra.Compra)
    if (-not $compra -or -not $compra.Compra) { return Resultado 'vencimiento' $pasos }

    $null = Invoke-Http -Method 'POST' -Url "$CreditoUrl/interno/credito/demo/cuentas/$($compra.Orden.id)/vencimiento" -Body @{ diasDesdeHoy = -1 }
    $proceso = Invoke-Http -Method 'POST' -Url "$CreditoUrl/interno/credito/demo/procesar-vencimientos"
    $pasos += Write-Paso 'Procesar vencimientos' 'vencidas >= 1' "$($proceso.Body.vencidas)" ([int]$proceso.Body.vencidas -ge 1)

    $actual = Get-Compra $token $compra.Orden.id
    $pasos += Write-Paso 'La cuenta queda vencida' 'VENCIDA' "$($actual.estado)" ($actual.estado -eq 'VENCIDA')
    $credito = Get-MiCredito $token
    $pasos += Write-Paso 'La linea queda en pausa' 'LINEA_BLOQUEADA' "$($credito.estado) - $($credito.titulo)" ($credito.estado -eq 'LINEA_BLOQUEADA')
    $oferta = Invoke-Http -Method 'GET' -Url "$CreditoUrl/api/credito/elegibilidad?monto=100" -Token $token
    $pasos += Write-Paso 'Elegibilidad LINEA_BLOQUEADA' 'LINEA_BLOQUEADA, no ofrecer' "$($oferta.Body.estado), ofrecer=$($oferta.Body.ofrecer)" ($oferta.Body.estado -eq 'LINEA_BLOQUEADA' -and $oferta.Body.ofrecer -eq $false)

    $nueva = Crear-OrdenCredito 'cliente2@sgo.local' 1 $token
    $rechazada = $null
    if ($nueva) { $rechazada = Esperar-Orden $nueva.id 'RECHAZADA' }
    $pasos += Write-Paso 'Una orden nueva a credito termina RECHAZADA' 'RECHAZADA' "$($rechazada.estado) - $(Ultimo-Motivo $rechazada)" ($null -ne $rechazada -and $rechazada.estado -eq 'RECHAZADA')

    $res = Crear-Abono $token $compra.Compra.cuentaId ([decimal]$actual.saldoPendiente) (Tokenizar-Tarjeta $token)
    $abono = $null
    if ($res.Status -eq 202) { $abono = Esperar-Abono $token $res.Body.id }
    $pasos += Write-Paso 'Pagar la cuenta vencida' 'APLICADO' "$($abono.estado)" ($null -ne $abono -and $abono.estado -eq 'APLICADO')
    $credito = Get-MiCredito $token
    $pasos += Write-Paso 'La linea se reactiva' 'DISPONIBLE' "$($credito.estado)" ($credito.estado -eq 'DISPONIBLE')
    $null = Esperar-Condicion { $null -ne (Evento-Auditoria $compra.Compra.cuentaId 'CuentaCreditoPagada') } 15
    $evento = Evento-Auditoria $compra.Compra.cuentaId 'CuentaCreditoPagada'
    $pasos += Write-Paso 'El pago cuenta como tardio' 'puntual = False' "puntual = $($evento.data.puntual)" ($null -ne $evento -and $evento.data.puntual -eq $false)
    return Resultado 'vencimiento' $pasos
}

# ---------------------------------------------------------------------------
# Ejecucion
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== Arnes de la ruta de credito ==="
Write-Host "  Ordenes:     $BaseUrl"
Write-Host "  Gateway:     $GatewayUrl"
Write-Host "  Credito:     $CreditoUrl"
Write-Host "  Inventario:  $InventarioUrl"
Write-Host "  Auditoria:   $AuditoriaUrl"
Write-Host "  Pagos:       $PagosUrl"
Write-Host "  Escenario:   $Escenario"
Write-Host ""

if ($Escenario -notin @('reiniciar', 'sin-sesion', 'suplantacion')) {
    if (-not (Sembrar-Producto)) {
        Write-Host "  FALLO: no se pudo sembrar $Sku en $InventarioUrl"
        exit 1
    }
}

$resultados = [ordered]@{}
switch ($Escenario) {
    'reiniciar' { $resultados['reiniciar'] = Escenario-Reiniciar }
    'aprobado' { $resultados['aprobado'] = Escenario-Aprobado }
    'cupo' { $resultados['cupo'] = Escenario-Rechazo 'cupo' 'cliente3@sgo.local' 20 'Q1,500.00' }
    'no-elegible' { $resultados['no-elegible'] = Escenario-Rechazo 'no-elegible' 'cliente4@sgo.local' 1 'pagos' }
    'suplantacion' { $resultados['suplantacion'] = Escenario-Suplantacion }
    'sin-sesion' { $resultados['sin-sesion'] = Escenario-SinSesion }
    'cancelacion' { $resultados['cancelacion'] = Escenario-Cancelacion }
    'timeout' { $resultados['timeout'] = Escenario-Timeout }
    'iva' { $resultados['iva'] = Escenario-Iva }
    'abono-parcial' { $resultados['abono-parcial'] = Escenario-AbonoParcial }
    'pago-total' { $resultados['pago-total'] = Escenario-PagoTotal }
    'pago-rechazado' { $resultados['pago-rechazado'] = Escenario-PagoRechazado }
    'pago-pendiente' { $resultados['pago-pendiente'] = Escenario-PagoPendiente }
    'recordatorio' { $resultados['recordatorio'] = Escenario-Recordatorio }
    'vencimiento' { $resultados['vencimiento'] = Escenario-Vencimiento }
    'todos' {
        $resultados['reiniciar'] = Escenario-Reiniciar
        $resultados['aprobado'] = Escenario-Aprobado
        $resultados['cupo'] = Escenario-Rechazo 'cupo' 'cliente3@sgo.local' 20 'Q1,500.00'
        $resultados['no-elegible'] = Escenario-Rechazo 'no-elegible' 'cliente4@sgo.local' 1 'pagos'
        $resultados['suplantacion'] = Escenario-Suplantacion
        $resultados['sin-sesion'] = Escenario-SinSesion
        $resultados['cancelacion'] = Escenario-Cancelacion
        $resultados['timeout'] = Escenario-Timeout
        $resultados['iva'] = Escenario-Iva
        $resultados['abono-parcial'] = Escenario-AbonoParcial
        $resultados['pago-total'] = Escenario-PagoTotal
        $resultados['pago-rechazado'] = Escenario-PagoRechazado
        $resultados['pago-pendiente'] = Escenario-PagoPendiente
        $resultados['recordatorio'] = Escenario-Recordatorio
        $resultados['vencimiento'] = Escenario-Vencimiento
    }
}

Write-Host "=== Resumen ==="
foreach ($nombre in $resultados.Keys) {
    Write-Host ("  {0,-16} {1}" -f $nombre, $(if ($resultados[$nombre]) { 'APROBADO' } else { 'FALLIDO' }))
}

$fallidos = @($resultados.Values | Where-Object { -not $_ }).Count
Write-Host ""
if ($fallidos -eq 0) {
    Write-Host "RESULTADO=APROBADO"
    exit 0
}
Write-Host "RESULTADO=FALLIDO"
exit 1
