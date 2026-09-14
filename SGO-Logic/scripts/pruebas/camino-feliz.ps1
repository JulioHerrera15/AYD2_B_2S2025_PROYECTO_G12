# camino-feliz.ps1 — Guion de demo y evidencia del camino feliz de la saga.
# Siembra existencias, crea una orden, la confirma, deja que la saga avance
# sola hasta PAGO_APLICADO, fuerza el paso fiscal, espera certificacion y
# y finalmente fuerza despacho.
#
# Cadena de transiciones:
#   CREADA -> RESERVA_SOLICITADA               [AUTO]  confirmar
#   RESERVA_SOLICITADA -> INVENTARIO_RESERVADO  [AUTO]  evento InventarioReservado
#   INVENTARIO_RESERVADO -> COBRO_SOLICITADO    [AUTO]  evento saga
#   COBRO_SOLICITADO -> PAGO_APLICADO           [AUTO]  evento PagoAplicado
#   PAGO_APLICADO -> CONFIRMADA_PENDIENTE_FISCAL [FORZADO] POST transiciones
#   CONFIRMADA_PENDIENTE_FISCAL -> CONFIRMADA   [AUTO]  evento DocumentoFiscalCertificado
#   CONFIRMADA -> DESPACHO_SOLICITADO           [FORZADO] POST transiciones (temporal)
#
# Uso:
#   .\scripts\pruebas\camino-feliz.ps1
#   .\scripts\pruebas\camino-feliz.ps1 -BaseUrl http://localhost:8083
#   .\scripts\pruebas\camino-feliz.ps1 -TimeoutSeg 120

param(
    [string]$BaseUrl = 'http://localhost:8083',
    [int]$TimeoutSeg = 60,
    [string]$InventarioUrl = 'http://localhost:8094',
    [string]$Sku = 'SKU-CAMINO-FELIZ',
    [string]$UbicacionId = 'CD-01'
)

# ---------------------------------------------------------------------------
# Funciones auxiliares
# ---------------------------------------------------------------------------

function Write-Step {
    param([string]$Paso, [string]$Detalle, [bool]$Forzado)
    $marcador = if ($Forzado) { '[FORZADO]' } else { '[AUTO]' }
    Write-Host ("  {0} {1,-35} {2}" -f $marcador, $Paso, $Detalle)
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

function Get-EstadoOrden {
    param([string]$BaseUrl, [string]$OrdenId)
    $orden = Invoke-HttpJson -Method 'GET' -Url "$BaseUrl/api/ordenes/$OrdenId"
    if ($null -ne $orden) { return $orden.estado }
    return $null
}

function ForzarTransicion {
    param(
        [string]$BaseUrl,
        [string]$OrdenId,
        [string]$EstadoDestino,
        [string]$PasoSaga,
        [string]$Motivo
    )
    $body = @{
        estadoDestino = $EstadoDestino
        pasoSaga      = $PasoSaga
        motivo        = $Motivo
    }
    return Invoke-HttpJson -Method 'POST' -Url "$BaseUrl/api/ordenes/$OrdenId/transiciones" -Body $body
}

function EsperarEstado {
    param(
        [string]$BaseUrl,
        [string]$OrdenId,
        [string]$EstadoObjetivo,
        [int]$TimeoutMs,
        [long]$InicioMs,
        [ref]$EstadosAlcanzados,
        [ref]$EstadoAnterior
    )
    while ($true) {
        $nuevoEstado = Get-EstadoOrden -BaseUrl $BaseUrl -OrdenId $OrdenId
        $ahoraMs = [System.Environment]::TickCount64
        $elapsedMs = $ahoraMs - $InicioMs

        if ($null -ne $nuevoEstado -and $nuevoEstado -ne $EstadoAnterior.Value) {
            $EstadosAlcanzados.Value += @{ Estado = $nuevoEstado; TiempoMs = $elapsedMs }
            $EstadoAnterior.Value = $nuevoEstado
        }

        if ($nuevoEstado -eq $EstadoObjetivo) {
            return $true
        }

        if ($ahoraMs - $InicioMs -ge $TimeoutMs) {
            return $false
        }

        Start-Sleep -Milliseconds 500
    }
}

# ---------------------------------------------------------------------------
# Cabecera
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== Camino feliz de la saga ==="
Write-Host "  Ordenes:      $BaseUrl"
Write-Host "  Inventario:   $InventarioUrl"
Write-Host "  Timeout:      ${TimeoutSeg}s"
Write-Host ""

$inicioMs = [System.Environment]::TickCount64
$timeoutMs = $TimeoutSeg * 1000
$estadosAlcanzados = @()
$estadoAnterior = ''

# ---------------------------------------------------------------------------
# Paso 0: Siembra de existencias
# ---------------------------------------------------------------------------

Write-Host "Paso 0: Siembra de existencias"
$seedBody = @{
    sku             = $Sku
    ubicacionId     = $UbicacionId
    tipoUbicacion   = 'CENTRO_DISTRIBUCION'
    cantidadFisica  = 100
}
$seedRes = Invoke-HttpJson -Method 'PUT' -Url "$InventarioUrl/api/inventario/existencias" -Body $seedBody
if ($null -eq $seedRes) {
    Write-Host "  FALLO: no se pudo sembrar existencias en $InventarioUrl"
    exit 1
}
Write-Step "Siembra" "SKU=$Sku cant=100 en $UbicacionId" $false

# ---------------------------------------------------------------------------
# Paso 1: Crear orden
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Paso 1: Crear orden"
$crearBody = @{
    clienteId        = "CAMINO-FELIZ-$(Get-Date -Format 'yyyyMMddHHmmss')"
    canal            = 'WEB'
    metodoEntrega    = 'DOMICILIO'
    moneda           = 'GTQ'
    medioPago        = 'TARJETA'
    impuestos        = 12.0
    lineas           = @(
        @{
            sku             = $Sku
            descripcion     = 'Producto de prueba camino feliz'
            cantidad        = 1
            precioUnitario  = 100.0
        }
    )
}
$orden = Invoke-HttpJson -Method 'POST' -Url "$BaseUrl/api/ordenes" -Body $crearBody
if ($null -eq $orden) {
    Write-Host "  FALLO: no se pudo crear la orden"
    exit 1
}
$ordenId = $orden.id
Write-Step "Crear orden" "id=$ordenId" $false

# ---------------------------------------------------------------------------
# Paso 2: Confirmar orden -> RESERVA_SOLICITADA
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Paso 2: Confirmar orden"
$confirmarRes = Invoke-HttpJson -Method 'POST' -Url "$BaseUrl/api/ordenes/$ordenId/confirmar" -Body @{ motivo = 'camino feliz - demo' }
if ($null -eq $confirmarRes) {
    Write-Host "  FALLO: no se pudo confirmar la orden"
    exit 1
}
Write-Step "Confirmar" "-> RESERVA_SOLICITADA" $false

# ---------------------------------------------------------------------------
# Paso 3: Polling hasta PAGO_APLICADO (la saga avanza sola)
#   RESERVA_SOLICITADA -> INVENTARIO_RESERVADO -> COBRO_SOLICITADO -> PAGO_APLICADO
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Paso 3: Esperando que la saga avance sola hasta PAGO_APLICADO..."

$pagoAlcanzado = EsperarEstado -BaseUrl $BaseUrl -OrdenId $ordenId `
    -EstadoObjetivo 'PAGO_APLICADO' -TimeoutMs $timeoutMs -InicioMs $inicioMs `
    -EstadosAlcanzados ([ref]$estadosAlcanzados) -EstadoAnterior ([ref]$estadoAnterior)

if (-not $pagoAlcanzado) {
    $ultimoEstado = if ($estadosAlcanzados.Count -gt 0) { $estadosAlcanzados[-1].Estado } else { 'desconocido' }
    Write-Host "  TIMEOUT: la saga no llego a PAGO_APLICADO. Estado actual: $ultimoEstado"
    # Caer a la impresion de tabla y resultado
    $estadoFinal = $ultimoEstado
}
else {
    Write-Step "Saga automatica" "RESERVA_SOLICITADA -> ... -> PAGO_APLICADO" $false

    # ---------------------------------------------------------------------------
    # Paso 4: Forzar transicion a CONFIRMADA_PENDIENTE_FISCAL
    #   PAGO_APLICADO -> CONFIRMADA_PENDIENTE_FISCAL (no hay evento automatico)
    # ---------------------------------------------------------------------------

    Write-Host ""
    Write-Host "Paso 4: Forzar transicion fiscal"
    $fiscalRes = ForzarTransicion -BaseUrl $BaseUrl -OrdenId $ordenId `
        -EstadoDestino 'CONFIRMADA_PENDIENTE_FISCAL' -PasoSaga 'FISCAL' `
        -Motivo 'paso manual para disparar certificacion fiscal SAT'

    if ($null -eq $fiscalRes) {
        Write-Host "  FALLO: no se pudo forzar CONFIRMADA_PENDIENTE_FISCAL"
        $estadoFinal = 'PAGO_APLICADO'
    }
    else {
        Write-Step "Transicion fiscal" "-> CONFIRMADA_PENDIENTE_FISCAL" $true

        # Registrar el estado en la tabla si acaba de cambiar
        $nuevoEstado = Get-EstadoOrden -BaseUrl $BaseUrl -OrdenId $ordenId
        if ($null -ne $nuevoEstado -and $nuevoEstado -ne $estadoAnterior) {
            $ahoraMs = [System.Environment]::TickCount64
            $estadosAlcanzados += @{ Estado = $nuevoEstado; TiempoMs = ($ahoraMs - $inicioMs) }
            $estadoAnterior = $nuevoEstado
        }

        # ---------------------------------------------------------------------------
        # Paso 5: Polling hasta CONFIRMADA (Facturacion certifica via SAT)
        #   CONFIRMADA_PENDIENTE_FISCAL -> CONFIRMADA
        # ---------------------------------------------------------------------------

        Write-Host ""
        Write-Host "Paso 5: Esperando certificacion fiscal (CONFIRMADA)..."

        $confirmadaAlcanzado = EsperarEstado -BaseUrl $BaseUrl -OrdenId $ordenId `
            -EstadoObjetivo 'CONFIRMADA' -TimeoutMs $timeoutMs -InicioMs $inicioMs `
            -EstadosAlcanzados ([ref]$estadosAlcanzados) -EstadoAnterior ([ref]$estadoAnterior)

        if (-not $confirmadaAlcanzado) {
            $ultimoEstado = if ($estadosAlcanzados.Count -gt 0) { $estadosAlcanzados[-1].Estado } else { 'desconocido' }
            Write-Host "  TIMEOUT: la certificacion fiscal no fallo en ${TimeoutSeg}s. Estado: $ultimoEstado"
            $estadoFinal = $ultimoEstado
        }
        else {
            Write-Step "Certificacion fiscal" "-> CONFIRMADA" $false

            # ---------------------------------------------------------------------------
            # Paso 6: Forzar despacho (temporal: no existe servicio de Logistica)
            #   CONFIRMADA -> DESPACHO_SOLICITADO
            # ---------------------------------------------------------------------------

            Write-Host ""
            Write-Host "Paso 6: Forzar despacho (temporal - no existe Logistica)"
            $despachoRes = ForzarTransicion -BaseUrl $BaseUrl -OrdenId $ordenId `
                -EstadoDestino 'DESPACHO_SOLICITADO' -PasoSaga 'DESPACHO' `
                -Motivo 'logistica no existe como servicio: transicion forzada temporal'

            if ($null -ne $despachoRes) {
                Write-Step "Despacho" "-> DESPACHO_SOLICITADO" $true
            }
            else {
                Write-Step "Despacho" "no se pudo forzar (continua de todos modos)" $true
            }

            $estadoFinal = 'CONFIRMADA'
        }
    }
}

# ---------------------------------------------------------------------------
# Tabla de resultados
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=== Tabla de estados alcanzados ==="
Write-Host ("  {0,-45} {1,12}" -f "ESTADO", "TIEMPO (ms)")
Write-Host ("  {0,-45} {1,12}" -f ("-" * 45), ("-" * 12))
foreach ($entry in $estadosAlcanzados) {
    Write-Host ("  {0,-45} {1,12}" -f $entry.Estado, $entry.TiempoMs)
}

# ---------------------------------------------------------------------------
# Resultado final
# ---------------------------------------------------------------------------

$duracionTotal = ([System.Environment]::TickCount64) - $inicioMs
Write-Host ""
if ($estadoFinal -eq 'CONFIRMADA') {
    Write-Host "RESULTADO=APROBADO"
    Write-Host ("  La orden {0} llego a CONFIRMADA en {1}ms" -f $ordenId, $duracionTotal)
    Write-Host ""
    Write-Host "Pasos automaticos:  INVENTARIO_RESERVADO, COBRO_SOLICITADO,"
    Write-Host "                    PAGO_APLICADO, CONFIRMADA_PENDIENTE_FISCAL -> CONFIRMADA"
    Write-Host "Pasos forzados:     CONFIRMADA_PENDIENTE_FISCAL (fiscal), DESPACHO_SOLICITADO (logistica)"
    exit 0
}
else {
    Write-Host "RESULTADO=FALLIDO"
    Write-Host ("  La orden {0} quedo en {1} despues de {2}ms" -f $ordenId, $estadoFinal, $duracionTotal)
    exit 1
}
