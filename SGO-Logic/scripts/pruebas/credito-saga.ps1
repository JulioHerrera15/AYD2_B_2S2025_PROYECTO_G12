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
#   todos         reiniciar y luego todos los anteriores.
#
# Cada paso imprime lo esperado y lo obtenido; cada escenario termina con
# APROBADO o FALLIDO.
#
# Uso:
#   .\scripts\pruebas\credito-saga.ps1 -Escenario todos
#   .\scripts\pruebas\credito-saga.ps1 -Escenario aprobado -TimeoutSeg 90

param(
    [ValidateSet('aprobado', 'cupo', 'no-elegible', 'suplantacion', 'sin-sesion', 'cancelacion', 'timeout', 'reiniciar', 'todos')]
    [string]$Escenario = 'todos',
    [string]$BaseUrl = 'http://localhost:8083',
    [string]$GatewayUrl = 'http://localhost:8080',
    [string]$CreditoUrl = 'http://localhost:8099',
    [string]$InventarioUrl = 'http://localhost:8094',
    [string]$AuditoriaUrl = 'http://localhost:8093',
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
    # cancelacion (anula y devuelve): el saldo debe volver al de inicio.
    $igual = Esperar-Condicion { [decimal](Get-MiCredito $token).saldoUtilizado -eq $saldoAntes } 60
    $pasos += Write-Paso 'El saldo utilizado termina igual que al inicio' "$saldoAntes" "$((Get-MiCredito $token).saldoUtilizado)" $igual
    return Resultado 'timeout' $pasos
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
    'todos' {
        $resultados['reiniciar'] = Escenario-Reiniciar
        $resultados['aprobado'] = Escenario-Aprobado
        $resultados['cupo'] = Escenario-Rechazo 'cupo' 'cliente3@sgo.local' 20 'Q1,500.00'
        $resultados['no-elegible'] = Escenario-Rechazo 'no-elegible' 'cliente4@sgo.local' 1 'pagos'
        $resultados['suplantacion'] = Escenario-Suplantacion
        $resultados['sin-sesion'] = Escenario-SinSesion
        $resultados['cancelacion'] = Escenario-Cancelacion
        $resultados['timeout'] = Escenario-Timeout
    }
}

Write-Host "=== Resumen ==="
foreach ($nombre in $resultados.Keys) {
    Write-Host ("  {0,-14} {1}" -f $nombre, $(if ($resultados[$nombre]) { 'APROBADO' } else { 'FALLIDO' }))
}

$fallidos = @($resultados.Values | Where-Object { -not $_ }).Count
Write-Host ""
if ($fallidos -eq 0) {
    Write-Host "RESULTADO=APROBADO"
    exit 0
}
Write-Host "RESULTADO=FALLIDO"
exit 1
