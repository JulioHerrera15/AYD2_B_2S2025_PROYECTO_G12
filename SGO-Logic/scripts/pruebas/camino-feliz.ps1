# camino-feliz.ps1 - Guion de demo y evidencia del camino feliz de la saga.
# Siembra existencias, crea una orden a domicilio, la confirma y sigue la saga
# hasta ENTREGADA. Al final consulta el despacho en Despacho.
#
# Origen de cada uno de los 10 pasos (CREADA -> ENTREGADA):
#   1  RESERVA_SOLICITADA             [AUTO]               POST confirmar
#   2  INVENTARIO_RESERVADO           [AUTO]               evento InventarioReservado
#   3  COBRO_SOLICITADO               [AUTO]               saga
#   4  PAGO_APLICADO                  [AUTO]               evento PagoAplicado
#   5  CONFIRMADA_PENDIENTE_FISCAL    [AUTO]               la saga, en la misma transaccion que PagoAplicado
#   6  CONFIRMADA                     [AUTO]               evento DocumentoFiscalCertificado
#   7  DESPACHO_SOLICITADO            [AUTO]               la saga, en la misma transaccion que CONFIRMADA
#   8  EN_PREPARACION                 [PROVEEDOR SIMULADO] el script hace de LogiExpress
#   9  EN_TRANSITO                    [PROVEEDOR SIMULADO] idem
#   10 ENTREGADA                      [PROVEEDOR SIMULADO] idem
#
# Proveedor simulado: en un despliegue real LogiExpress avisa cada cambio de
# entrega al webhook de Despacho (POST /api/despacho/{ordenId}/estados). El mock
# no lo hace solo, y su endpoint admin POST /admin/guias/{guiaId}/estado solo
# cambia el estado de rastreo en su memoria, no empuja nada a Despacho. Por eso
# el script llama a los dos: el admin del mock (para que su rastreo quede
# coherente) y el webhook de Despacho (lo que de verdad mueve la orden).
#
# RESULTADO=APROBADO solo si se llego a ENTREGADA y ningun paso fue [FORZADO]. Este
# script no fuerza ninguno: toda la saga de Ordenes corre sola.
# Los pasos [PROVEEDOR SIMULADO] no cuentan como forzados: solo hacen de transportista.
#
# Uso:
#   .\scripts\pruebas\camino-feliz.ps1
#   .\scripts\pruebas\camino-feliz.ps1 -BaseUrl http://localhost:8083
#   .\scripts\pruebas\camino-feliz.ps1 -TimeoutSeg 120
#
# TimeoutSeg es la espera maxima de cada tramo, no de la corrida completa.

param(
    [string]$BaseUrl = 'http://localhost:8083',
    [int]$TimeoutSeg = 60,
    [string]$InventarioUrl = 'http://localhost:8094',
    [string]$DespachoUrl = 'http://localhost:8097',
    [string]$LogiExpressUrl = 'http://localhost:8096',
    [string]$Sku = 'SKU-CAMINO-FELIZ',
    [string]$UbicacionId = 'CD-01'
)

# ---------------------------------------------------------------------------
# Definicion de los pasos
# ---------------------------------------------------------------------------

# Todos los estados de la orden en el orden del camino feliz. El indice sirve
# para esperar "este estado o uno posterior": una transicion rapida puede pasar
# de largo el estado que se esta esperando entre dos consultas.
$Estados = @(
    'CREADA', 'RESERVA_SOLICITADA', 'INVENTARIO_RESERVADO', 'COBRO_SOLICITADO',
    'PAGO_APLICADO', 'CONFIRMADA_PENDIENTE_FISCAL', 'CONFIRMADA',
    'DESPACHO_SOLICITADO', 'EN_PREPARACION', 'EN_TRANSITO', 'ENTREGADA'
)

$Terminales = @('CANCELADA', 'CANCELADA_RESERVA_VENCIDA', 'RECHAZADA')

# Origen esperado de cada paso. Se muestra en la tabla final.
$OrigenPorEstado = @{
    'RESERVA_SOLICITADA'          = 'AUTO'
    'INVENTARIO_RESERVADO'        = 'AUTO'
    'COBRO_SOLICITADO'            = 'AUTO'
    'PAGO_APLICADO'               = 'AUTO'
    'CONFIRMADA_PENDIENTE_FISCAL' = 'AUTO'
    'CONFIRMADA'                  = 'AUTO'
    'DESPACHO_SOLICITADO'         = 'AUTO'
    'EN_PREPARACION'              = 'PROVEEDOR SIMULADO'
    'EN_TRANSITO'                 = 'PROVEEDOR SIMULADO'
    'ENTREGADA'                   = 'PROVEEDOR SIMULADO'
}

# ---------------------------------------------------------------------------
# Funciones auxiliares
# ---------------------------------------------------------------------------

# Reloj de la corrida. Se usa Stopwatch y no [Environment]::TickCount64 porque
# Windows PowerShell 5.1 (.NET Framework) no tiene TickCount64: devuelve $null y
# las esperas con limite de tiempo nunca vencerian.
$script:Reloj = [System.Diagnostics.Stopwatch]::StartNew()

function Write-Step {
    param([string]$Paso, [string]$Detalle, [string]$Origen)
    Write-Host ("  {0,-22} {1,-32} {2}" -f "[$Origen]", $Paso, $Detalle)
}

function Invoke-HttpJson {
    param(
        [string]$Method,
        [string]$Url,
        [object]$Body = $null
    )
    $headers = @{ 'Content-Type' = 'application/json' }
    $params = @{
        Uri             = $Url
        Method          = $Method
        Headers         = $headers
        UseBasicParsing = $true
        TimeoutSec      = 10
    }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        return $null
    }
}

function Get-Orden {
    param([string]$OrdenId)
    return Invoke-HttpJson -Method 'GET' -Url "$BaseUrl/api/ordenes/$OrdenId"
}

function Get-Rango {
    param([string]$Estado)
    return [array]::IndexOf($script:Estados, $Estado)
}

# Espera hasta que la orden este en $Objetivo o en un estado posterior del
# camino feliz. Devuelve el estado ultimo visto y si se alcanzo el objetivo.
function Esperar-Estado {
    param([string]$OrdenId, [string]$Objetivo)

    $limite = $script:Reloj.ElapsedMilliseconds + ($TimeoutSeg * 1000)
    $rangoObjetivo = Get-Rango $Objetivo
    $ultimo = 'desconocido'

    while ($true) {
        $orden = Get-Orden -OrdenId $OrdenId
        if ($null -ne $orden) {
            $ultimo = $orden.estado
            if ((Get-Rango $ultimo) -ge $rangoObjetivo) {
                return @{ Alcanzado = $true; Estado = $ultimo }
            }
            if ($script:Terminales -contains $ultimo) {
                return @{ Alcanzado = $false; Estado = $ultimo }
            }
        }
        if ($script:Reloj.ElapsedMilliseconds -ge $limite) {
            return @{ Alcanzado = $false; Estado = $ultimo }
        }
        Start-Sleep -Milliseconds 250
    }
}

# Hace de LogiExpress: informa un estado de entrega. El admin del mock es
# best-effort (solo mantiene su rastreo); el webhook de Despacho es lo que mueve
# la orden, y si falla la funcion devuelve $false.
function Simular-Proveedor {
    param([string]$OrdenId, [string]$GuiaId, [string]$Estado, [string]$Detalle)

    if (-not [string]::IsNullOrWhiteSpace($GuiaId)) {
        $null = Invoke-HttpJson -Method 'POST' -Url "$LogiExpressUrl/admin/guias/$GuiaId/estado" `
            -Body @{ estado = $Estado; detalle = $Detalle }
    }

    $respuesta = Invoke-HttpJson -Method 'POST' -Url "$DespachoUrl/api/despacho/$OrdenId/estados" `
        -Body @{ estado = $Estado; detalle = $Detalle }
    return ($null -ne $respuesta)
}

function ConvertTo-Instante {
    param([object]$Valor)
    if ($Valor -is [datetime]) { return [DateTimeOffset]$Valor }
    return [DateTimeOffset]::Parse([string]$Valor, [System.Globalization.CultureInfo]::InvariantCulture)
}

# ---------------------------------------------------------------------------
# Cabecera
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== Camino feliz de la saga ==="
Write-Host "  Ordenes:      $BaseUrl"
Write-Host "  Inventario:   $InventarioUrl"
Write-Host "  Despacho:     $DespachoUrl"
Write-Host "  LogiExpress:  $LogiExpressUrl"
Write-Host "  Espera max.:  ${TimeoutSeg}s por tramo"
Write-Host ""

$inicioMs = $script:Reloj.ElapsedMilliseconds
$forzados = @()
$estadoFinal = 'desconocido'
$motivoFallo = $null
$ordenId = $null

# ---------------------------------------------------------------------------
# Paso 0: Siembra de existencias
# ---------------------------------------------------------------------------

Write-Host "Paso 0: Siembra de existencias"
$seedBody = @{
    sku            = $Sku
    ubicacionId    = $UbicacionId
    tipoUbicacion  = 'CENTRO_DISTRIBUCION'
    cantidadFisica = 100
}
$seedRes = Invoke-HttpJson -Method 'PUT' -Url "$InventarioUrl/api/inventario/existencias" -Body $seedBody
if ($null -eq $seedRes) {
    Write-Host "  FALLO: no se pudo sembrar existencias en $InventarioUrl"
    exit 1
}
Write-Step "Siembra" "SKU=$Sku cant=100 en $UbicacionId" 'AUTO'

# ---------------------------------------------------------------------------
# Paso 1: Crear y confirmar la orden (CREADA -> RESERVA_SOLICITADA)
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Paso 1: Crear y confirmar la orden"
$crearBody = @{
    clienteId        = "CAMINO-FELIZ-$(Get-Date -Format 'yyyyMMddHHmmss')"
    canal            = 'WEB'
    metodoEntrega    = 'DOMICILIO'
    moneda           = 'GTQ'
    medioPago        = 'TARJETA'
    impuestos        = 12.0
    direccionEntrega = @{
        pais         = 'GT'
        departamento = 'Guatemala'
        municipio    = 'Guatemala'
        direccion    = '5a avenida 3-21 zona 2'
        referencia   = 'Camino feliz'
    }
    lineas           = @(
        @{
            sku            = $Sku
            descripcion    = 'Producto de prueba camino feliz'
            cantidad       = 1
            precioUnitario = 100.0
        }
    )
}
$orden = Invoke-HttpJson -Method 'POST' -Url "$BaseUrl/api/ordenes" -Body $crearBody
if ($null -eq $orden) {
    Write-Host "  FALLO: no se pudo crear la orden"
    exit 1
}
$ordenId = $orden.id
Write-Step "Crear orden" "id=$ordenId" 'AUTO'

$confirmarRes = Invoke-HttpJson -Method 'POST' -Url "$BaseUrl/api/ordenes/$ordenId/confirmar" `
    -Body @{ motivo = 'camino feliz - demo' }
if ($null -eq $confirmarRes) {
    Write-Host "  FALLO: no se pudo confirmar la orden"
    exit 1
}
Write-Step "Confirmar" "-> RESERVA_SOLICITADA" 'AUTO'

# ---------------------------------------------------------------------------
# Paso 2: la saga avanza sola hasta PAGO_APLICADO
#   RESERVA_SOLICITADA -> INVENTARIO_RESERVADO -> COBRO_SOLICITADO -> PAGO_APLICADO
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Paso 2: Esperando que la saga avance sola hasta PAGO_APLICADO..."
$espera = Esperar-Estado -OrdenId $ordenId -Objetivo 'PAGO_APLICADO'
$estadoFinal = $espera.Estado
if (-not $espera.Alcanzado) {
    $motivoFallo = "la saga no llego a PAGO_APLICADO; estado actual: $estadoFinal"
}
else {
    Write-Step "Saga automatica" "-> PAGO_APLICADO" 'AUTO'
}

# ---------------------------------------------------------------------------
# Paso 3: certificacion fiscal. Tras PAGO_APLICADO la orden pasa sola a
# CONFIRMADA_PENDIENTE_FISCAL (Facturacion recibe la solicitud) y, al certificar la
# SAT, a CONFIRMADA.
# ---------------------------------------------------------------------------

if ($null -eq $motivoFallo) {
    Write-Host ""
    Write-Host "Paso 3: Esperando certificacion fiscal (CONFIRMADA)..."
    $espera = Esperar-Estado -OrdenId $ordenId -Objetivo 'CONFIRMADA'
    $estadoFinal = $espera.Estado
    if (-not $espera.Alcanzado) {
        $motivoFallo = "la certificacion fiscal no termino; estado actual: $estadoFinal"
    }
    else {
        Write-Step "Certificacion fiscal" "-> CONFIRMADA" 'AUTO'
    }
}

# ---------------------------------------------------------------------------
# Paso 4: despacho solicitado. Sale solo de la saga al certificar.
# ---------------------------------------------------------------------------

if ($null -eq $motivoFallo) {
    Write-Host ""
    Write-Host "Paso 4: Esperando DESPACHO_SOLICITADO..."
    $espera = Esperar-Estado -OrdenId $ordenId -Objetivo 'DESPACHO_SOLICITADO'
    $estadoFinal = $espera.Estado
    if (-not $espera.Alcanzado) {
        $motivoFallo = "la orden no paso de $estadoFinal a DESPACHO_SOLICITADO por si sola"
    }
    else {
        Write-Step "Despacho" "-> DESPACHO_SOLICITADO" 'AUTO'
    }
}

# ---------------------------------------------------------------------------
# Paso 5: Despacho crea su registro y resuelve guia (GUIA_ASIGNADA o GUIA_PENDIENTE)
# ---------------------------------------------------------------------------

$guiaId = $null
if ($null -eq $motivoFallo) {
    Write-Host ""
    Write-Host "Paso 5: Esperando que Despacho resuelva origen y guia..."
    $limite = $script:Reloj.ElapsedMilliseconds + ($TimeoutSeg * 1000)
    $despacho = $null
    while ($true) {
        $despacho = Invoke-HttpJson -Method 'GET' -Url "$DespachoUrl/api/despacho/$ordenId"
        if ($null -ne $despacho -and $despacho.estado -in @('GUIA_ASIGNADA', 'GUIA_PENDIENTE')) { break }
        if ($script:Reloj.ElapsedMilliseconds -ge $limite) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $despacho -or $despacho.estado -notin @('GUIA_ASIGNADA', 'GUIA_PENDIENTE')) {
        $vista = if ($null -eq $despacho) { 'sin despacho' } else { $despacho.estado }
        $motivoFallo = "Despacho no resolvio la guia a tiempo (estado del despacho: $vista)"
    }
    else {
        $guiaId = $despacho.guiaId
        Write-Step "Despacho" "estado=$($despacho.estado) guia=$guiaId" 'AUTO'
    }
}

# ---------------------------------------------------------------------------
# Paso 6: el "proveedor" avanza la entrega. Cada estado se informa al webhook de
# Despacho y se espera a que la orden lo refleje.
# ---------------------------------------------------------------------------

if ($null -eq $motivoFallo) {
    Write-Host ""
    Write-Host "Paso 6: LogiExpress simulado avanza la entrega..."
    foreach ($estado in @('EN_PREPARACION', 'EN_TRANSITO', 'ENTREGADA')) {
        $enviado = Simular-Proveedor -OrdenId $ordenId -GuiaId $guiaId -Estado $estado `
            -Detalle "camino feliz: proveedor simulado informa $estado"
        if (-not $enviado) {
            $motivoFallo = "Despacho rechazo el estado $estado"
            break
        }
        $espera = Esperar-Estado -OrdenId $ordenId -Objetivo $estado
        $estadoFinal = $espera.Estado
        if (-not $espera.Alcanzado) {
            $motivoFallo = "la orden no llego a $estado; estado actual: $estadoFinal"
            break
        }
        Write-Step "Entrega" "-> $estado" 'PROVEEDOR SIMULADO'
    }
}

$duracionTotal = $script:Reloj.ElapsedMilliseconds - $inicioMs

# ---------------------------------------------------------------------------
# Tabla de los 10 pasos, con los tiempos que registro la propia orden
# ---------------------------------------------------------------------------

$ordenFinal = Get-Orden -OrdenId $ordenId
if ($null -ne $ordenFinal) { $estadoFinal = $ordenFinal.estado }

Write-Host ""
Write-Host "=== Tabla de pasos (tiempos desde la creacion de la orden) ==="
Write-Host ("  {0,-3} {1,-30} {2,-20} {3,12} {4,12}" -f "#", "ESTADO ALCANZADO", "ORIGEN", "TIEMPO (ms)", "DELTA (ms)")
Write-Host ("  {0,-3} {1,-30} {2,-20} {3,12} {4,12}" -f "-", ("-" * 30), ("-" * 20), ("-" * 12), ("-" * 12))

$creadaEn = $null
if ($null -ne $ordenFinal) { $creadaEn = ConvertTo-Instante $ordenFinal.creadaEn }
$anteriorMs = 0
$numero = 0
foreach ($estado in ($Estados | Select-Object -Skip 1)) {
    $numero++
    $origen = $OrigenPorEstado[$estado]
    $transicion = $null
    if ($null -ne $ordenFinal) {
        $transicion = @($ordenFinal.transiciones | Where-Object { $_.estadoNuevo -eq $estado }) | Select-Object -First 1
    }
    if ($null -eq $transicion) {
        Write-Host ("  {0,-3} {1,-30} {2,-20} {3,12} {4,12}" -f $numero, $estado, $origen, "NO ALCANZADO", "-")
        continue
    }
    $ms = [int64][Math]::Round(((ConvertTo-Instante $transicion.cambiadoEn) - $creadaEn).TotalMilliseconds)
    Write-Host ("  {0,-3} {1,-30} {2,-20} {3,12} {4,12}" -f $numero, $estado, $origen, $ms, ($ms - $anteriorMs))
    $anteriorMs = $ms
}

# ---------------------------------------------------------------------------
# Consulta final del despacho
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== GET $DespachoUrl/api/despacho/$ordenId ==="
$despachoFinal = Invoke-HttpJson -Method 'GET' -Url "$DespachoUrl/api/despacho/$ordenId"
if ($null -eq $despachoFinal) {
    Write-Host "  Sin despacho para esta orden."
}
else {
    Write-Host ("  id:             {0}" -f $despachoFinal.id)
    Write-Host ("  estado:         {0}" -f $despachoFinal.estado)
    Write-Host ("  metodoEntrega:  {0}" -f $despachoFinal.metodoEntrega)
    Write-Host ("  origen:         {0} ({1})" -f $despachoFinal.ubicacionOrigenId, $despachoFinal.tipoUbicacionOrigen)
    Write-Host ("  direccion:      {0}" -f $despachoFinal.direccionEntrega)
    Write-Host ("  guiaId:         {0}" -f $despachoFinal.guiaId)
    Write-Host ("  transportista:  {0}" -f $despachoFinal.transportista)
    Write-Host ("  trackingUrl:    {0}" -f $despachoFinal.trackingUrl)
    Write-Host ("  entregadoEn:    {0}" -f $despachoFinal.entregadoEn)
}

# ---------------------------------------------------------------------------
# Resultado final
# ---------------------------------------------------------------------------

Write-Host ""
$llegoAEntregada = ($estadoFinal -eq 'ENTREGADA') -and ($null -eq $motivoFallo)

if ($llegoAEntregada -and $forzados.Count -eq 0) {
    Write-Host "RESULTADO=APROBADO"
    Write-Host ("  La orden {0} llego a ENTREGADA en {1}ms sin pasos forzados." -f $ordenId, $duracionTotal)
    exit 0
}
elseif ($llegoAEntregada) {
    Write-Host "RESULTADO=NO_APROBADO"
    Write-Host ("  La orden {0} llego a ENTREGADA en {1}ms, pero hubo pasos forzados: {2}" -f $ordenId, $duracionTotal, ($forzados -join ', '))
    exit 1
}
else {
    Write-Host "RESULTADO=FALLIDO"
    Write-Host ("  La orden {0} quedo en {1} despues de {2}ms: {3}" -f $ordenId, $estadoFinal, $duracionTotal, $motivoFallo)
    if ($forzados.Count -gt 0) {
        Write-Host ("  Pasos forzados: {0}" -f ($forzados -join ', '))
    }
    exit 1
}
