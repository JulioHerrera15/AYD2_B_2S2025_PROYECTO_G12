# Mock POS (32 instancias) — SGO-Logic

**Restricción de diseño que respeta el mock:** el POS es **downstream-only**, pues
solo consume eventos de venta, nunca es fuente de datos hacia SGO-Logic y no se le exige
ninguna capacidad nueva (por ejemplo, no expone una API de consulta ni hace polling). Por
eso el único endpoint "de negocio" es la recepción de eventos y el resto son endpoints de
administración que solo existen en el mock, para poder observar y forzar condiciones de
prueba.

## Arrancar el mock

```bash
npm install
npm start        # o: node server.js
# Escucha en http://localhost:4001
```

Al arrancar (o tras `POST /pos/admin/reset`) existen 32 tiendas `T01` a `T32`, todas en
línea y sin eventos procesados.

## 1) Recepción de evento de venta — `POST /pos/:tiendaId/eventos`

```json
{
  "eventoId": "EVT-0001",
  "ordenId": "ORD-9001",
  "tipo": "VentaConfirmada",
  "canal": "web",
  "monto": 450.00,
  "fecha": "2026-09-10T14:00:00Z"
}
```

`eventoId` es obligatorio y es la clave de idempotencia: el mismo evento
reenviado a la **misma** tienda no se reprocesa.

Respuestas posibles:
- `200 ACEPTADO` — primera vez que la tienda ve ese `eventoId`; se registra como
  "reflejado" en la terminal.
- `200 DUPLICADO` — la tienda ya había procesado ese `eventoId`; se devuelve la
  confirmación original sin volver a aplicarlo. Esto es lo que evita que un reintento del
  worker de Integración POS duplique la venta en la terminal.
- `503 NO_DISPONIBLE` / `500 ERROR_TERMINAL` / `504 TIMEOUT` — la tienda está fuera de
  línea (ver sección siguiente).

## 2) Tiendas fuera de línea

`POST /pos/admin/:tiendaId/caida`

```json
{ "activar": true, "tipo": "no_disponible", "duracionMs": 5000 }
```

- `tipo: "no_disponible"` → `503` inmediato (terminal apagada / sin conexión — el caso
  típico que describe el DDA).
- `tipo: "error500"` → `500` inmediato (falla interna de la terminal).
- `tipo: "timeout"` → no responde durante 8 s y luego `504` (para forzar a que el cliente
  agote su propio timeout, como pasaría con una tienda con la red caída a medias).
- `duracionMs` opcional: si se omite, la tienda queda fuera de línea hasta que se llame de
  nuevo con `"activar": false`.

También existe `POST /pos/admin/caida-masiva` para desconectar varias tiendas a la vez
(por ejemplo, simular un incidente de red regional):

```json
{ "tiendas": ["T20", "T21", "T22"], "activar": true, "tipo": "error500" }
```

Con esto se puede probar que el worker de Integración POS:
- No pierde la venta cuando una tienda está fuera de línea (el mensaje debería ir a
  cola de fallidos y reintentarse con back-off del lado de SGO-Logic — este mock solo
  simula el rechazo del lado del POS, la lógica de reintento vive en Integración POS).
- Al reconectarse la tienda, el mismo evento se acepta normalmente.
- Otras tiendas siguen operando con normalidad mientras una está caída (el fallo es por
  tienda, no global).

## 3) Duplicados

La idempotencia es **por tienda + `eventoId`**, igual que en el diseño real: si Integración
POS reintenta el mismo evento porque no recibió confirmación a tiempo (por ejemplo, la
tienda respondió lento pero sí lo procesó), el segundo envío no duplica la venta en la
terminal — se detecta y se responde `DUPLICADO` con el detalle del evento original.

## Consultas de apoyo

- `GET /pos/:tiendaId/eventos` — todos los eventos que una tienda ya procesó.
- `GET /pos/:tiendaId/estado` — si está en línea, tipo de caída activa (si aplica) y
  últimos 10 eventos de su bitácora.
- `GET /pos` — resumen de las 32 tiendas: cuáles están fuera de línea y cuántos eventos
  ha procesado cada una. Útil para verificar rápido el efecto de una caída masiva.
- `POST /pos/admin/reset` — reinicia las 32 tiendas a su estado inicial (todas en línea,
  sin eventos).

## Script de prueba de extremo a extremo

```bash
node server.js &
bash test-cliente.sh
```

Ver `test-cliente.sh`: evento aceptado, mismo evento duplicado, tienda fuera de línea con
rechazo, reconexión automática y reintento exitoso, y caída masiva regional.
