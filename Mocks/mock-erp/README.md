# Mock ERP-Core (AS/400) — SGO-Logic

**Importante:** este mock expone HTTP solo por comodidad de pruebas. El ERP real **no**
tiene API síncrona (EV-15); en producción, la capa anticorrupción (ADR-007) le entrega el
archivo por el mecanismo de transferencia que el AS/400 realmente soporte. Aquí ese archivo
se envía como cuerpo de una petición HTTP para poder automatizar las pruebas.

## Arrancar el mock

```bash
npm install
npm start        # o: node server.js
# Escucha en http://localhost:4000
```

## 1) Recepción de lote — `POST /erp/lotes`

Acepta tres formatos de `Content-Type`:

### a) JSON (atajo recomendado para pruebas automatizadas)

```json
{
  "loteId": "LOTE-2026-09-10-001",
  "secuencia": 1,
  "fechaGeneracion": "2026-09-10T14:00:00Z",
  "totalesControl": { "cantidadRegistros": 3, "montoTotal": 960.00 },
  "registros": [
    { "ordenId": "ORD-1001", "canal": "web", "tienda": "T01", "monto": 320.00, "fecha": "2026-09-10T13:45:00Z" },
    { "ordenId": "ORD-1002", "canal": "tienda", "tienda": "T02", "monto": 320.00, "fecha": "2026-09-10T13:50:00Z" },
    { "ordenId": "ORD-1003", "canal": "app", "tienda": "T01", "monto": 320.00, "fecha": "2026-09-10T13:55:00Z" }
  ]
}
```

### b) CSV (`Content-Type: text/csv`)

Línea 1 = metadatos del lote; línea 2 = encabezado de columnas de registro; líneas 3+ = registros.

```csv
LOTE-2026-09-10-001,1,2026-09-10T14:00:00Z,3,960.00
ORDEN,CANAL,TIENDA,MONTO,FECHA
ORD-1001,web,T01,320.00,2026-09-10T13:45:00Z
ORD-1002,tienda,T02,320.00,2026-09-10T13:50:00Z
ORD-1003,app,T01,320.00,2026-09-10T13:55:00Z
```

### c) XML (`Content-Type: application/xml`)

```xml
<Lote id="LOTE-2026-09-10-001" secuencia="1" fecha="2026-09-10T14:00:00Z">
  <TotalesControl cantidadRegistros="3" montoTotal="960.00"/>
  <Registros>
    <Orden id="ORD-1001" canal="web" tienda="T01" monto="320.00" fecha="2026-09-10T13:45:00Z"/>
    <Orden id="ORD-1002" canal="tienda" tienda="T02" monto="320.00" fecha="2026-09-10T13:50:00Z"/>
    <Orden id="ORD-1003" canal="app" tienda="T01" monto="320.00" fecha="2026-09-10T13:55:00Z"/>
  </Registros>
</Lote>
```

## 2) Cuadre de totales

El mock **recalcula** `cantidadRegistros` y `montoTotal` a partir de los registros recibidos
y los compara contra lo declarado en el encabezado del lote:

- Si coinciden, el lote se **acepta** y avanza `ultimaSecuenciaAceptada`.
- Si no coinciden → `HTTP 422`, `estado: "RECHAZADO_CUADRE"`, con el detalle de la(s)
  discrepancia(s). **No** se marca el lote como procesado ni avanza la secuencia: el worker
  de Integración ERP debe corregir y reenviar con la misma secuencia.

También se valida:
- **Duplicado** (mismo `loteId` ya procesado) → `HTTP 409`, `estado: "DUPLICADO"`, devuelve la
  confirmación previa (idempotencia — evita doble carga si el worker reintenta un envío que sí
  llegó).
- **Secuencia retrocedida** (`secuencia` ≤ última aceptada) → `HTTP 409`,
  `estado: "SECUENCIA_INVALIDA"`.

## 3) Respuesta

Confirmación de éxito:

```json
{
  "loteId": "LOTE-2026-09-10-001",
  "secuencia": 1,
  "estado": "ACEPTADO",
  "cantidadRegistrosProcesados": 3,
  "montoTotalProcesado": 960,
  "discrepancias": [],
  "procesadoEn": "2026-09-10T22:25:22.674Z"
}
```

Esta misma estructura es la que el proceso de reconciliación diaria, debe
comparar contra lo que SGO-Logic registró como enviado, para producir el reporte de
diferencias de Finanzas.

Consultas de apoyo:
- `GET /erp/lotes/:loteId` → confirmación puntual de un lote.
- `GET /erp/lotes` → listado completo + última secuencia aceptada (para reconciliación masiva).

## 4) Caída provocada

`POST /erp/admin/caida`

```json
{ "activar": true, "tipo": "error500", "duracionMs": 5000 }
```

- `tipo: "no_disponible"` → responde `503` inmediato (AS/400 en mantenimiento).
- `tipo: "error500"` → responde `500` inmediato (falla interna del AS/400).
- `tipo: "timeout"` → **no responde** durante 8 s y luego devuelve `504` (para forzar a que
  el cliente agote su propio timeout, tal como pasaría con el AS/400 real colgado).
- `duracionMs` es opcional: si se omite, la caída queda activa hasta llamar de nuevo con
  `"activar": false` o hasta `POST /erp/admin/reset`.

Con esto se puede probar que el worker de Integración ERP (HU-013):
- No pierde el lote si el ERP no responde.
- Reintenta con back-off (como el resto de integraciones externas del sistema, ADR-009 en
  espíritu).
- Al recuperarse el ERP, el mismo lote (misma secuencia) se acepta normalmente.

Otros endpoints de administración (no existen en el AS/400 real, solo para pruebas):
- `GET /erp/admin/estado` → estado interno + últimos 10 eventos de la bitácora.
- `POST /erp/admin/reset` → limpia todo el estado en memoria.

## Script de prueba de extremo a extremo

Ver `test-cliente.sh`: levanta el flujo completo — lote correcto, duplicado, CSV con
descuadre, lote XML, activación de caída, intento fallido durante la caída, y reintento
exitoso tras la recuperación.

```bash
node server.js &
bash test-cliente.sh
```
