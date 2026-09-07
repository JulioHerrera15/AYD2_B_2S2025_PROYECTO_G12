# Envelope de eventos de SGO-Logic — v1

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Alcance:** todos los contextos de escritura del sistema
**Propietario del documento:** base técnica (HU-008)
**Redactado por:** 202300410 — Angel David Martínez García
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-07

---

## 0. Por qué este documento existe

EAC-03 exige reconstruir la evidencia **completa** de una transacción en menos de un segundo, indexada por identificador de transacción, con cero transacciones sin respaldo. Auditoría no puede cumplirlo si cada contexto inventa su propio sobre y su propio nombre de correlación: la bitácora quedaría con agujeros y el escenario no se cumpliría.

Por eso el sobre no pertenece a ningún contexto. Es del proyecto. Órdenes, Inventario, Pagos, Facturación, Despacho, Integración POS e Integración ERP publican con **este mismo sobre**; cada uno solo define el contenido de su `data` en su propio documento de contrato.

Este documento se implementa una sola vez, en el paquete compartido `SgoLogic.Contracts` de la solución base. Ningún contexto redefine el sobre por su cuenta.

---

## 1. Envelope común

| Campo | Tipo | Obligatorio | Descripción |
|---|---|---|---|
| `eventId` | string (UUID v4) | sí | Identificador único e irrepetible del evento. Ancla de idempotencia del consumidor. |
| `eventType` | string (PascalCase) | sí | Nombre del evento, tal como lo define el contrato de su contexto emisor. |
| `eventVersion` | integer | sí | Versión del contrato de ese evento. |
| `occurredAt` | string (ISO 8601 UTC) | sí | Momento del hecho de negocio, no el de publicación. Milisegundos y sufijo `Z`. |
| `sequence` | integer | sí | Secuencia monotónica creciente por `aggregateId`. Empieza en 1. Ver sección 3. |
| `transactionId` | string (UUID v4) | sí | Correlación de extremo a extremo. Idéntico en todos los eventos de una misma transacción de negocio, sin importar qué contexto los emita. Es el índice de Auditoría (EV-30, EAC-03). |
| `causationId` | string (UUID) \| null | sí | `eventId` del evento que provocó este. `null` solo cuando el origen fue una acción directa de un usuario. Ver sección 4. |
| `aggregateType` | string | sí | Tipo del agregado: `Orden`, `Reserva`, `Pago`, `DocumentoFiscal`, `Despacho`, `Devolucion`, `LoteErp`. |
| `aggregateId` | string | sí | Identificador del agregado dentro de su contexto. |
| `source` | string | sí | Contexto emisor en minúscula: `ordenes`, `inventario`, `pagos`, `facturacion`, `despacho`, `integracion-pos`, `integracion-erp`. |
| `data` | object | sí | Cuerpo específico del evento, definido por el contrato de su contexto. |

### Ejemplo

```json
{
  "eventId": "3f5b2c9e-8a41-4d7e-9c02-1b6e5a7d4f30",
  "eventType": "OrdenEstadoCambiado",
  "eventVersion": 1,
  "occurredAt": "2026-09-07T14:32:10.123Z",
  "sequence": 7,
  "transactionId": "a1c4e8b2-77d3-4f19-b5aa-0e93c62d1845",
  "causationId": "9d21f0ab-3c55-4e88-8f14-77b0a2c9e611",
  "aggregateType": "Orden",
  "aggregateId": "ORD-2026-000123",
  "source": "ordenes",
  "data": {}
}
```

---

## 2. Reglas obligatorias

1. **Toda publicación pasa por Outbox** (ADR-003). Prohibido publicar directo al broker desde el código de aplicación. El evento se persiste en la tabla outbox del contexto dentro de la misma transacción que el cambio de datos.
2. **Consumo idempotente.** El broker entrega *al menos una vez*. Todo consumidor deduplica por `eventId`. Procesar un `eventId` dos veces no puede producir dos efectos.
3. **Ordenamiento por `sequence`, nunca por hora de llegada.** Ver sección 3.
4. **Prohibido en todo payload de todo contexto:** número completo de tarjeta (PAN), código de seguridad (CVV/CVC) y fecha de vencimiento completa. Restricción dura de RES-08, EV-25 y ADR-010. Solo se transmite token, últimos cuatro dígitos y marca.
5. **Montos como cadena decimal**, nunca número de punto flotante. Ejemplo: `"1250.00"`.
6. **Fechas en ISO 8601 UTC** con milisegundos y sufijo `Z`.
7. **Identificadores como cadena**, nunca entero.
8. **Casing.** `eventType` en PascalCase, por ser nombre de tipo. **Todo valor de enumeración va en MAYUSCULA_CON_GUION_BAJO**, sin excepción y en todos los contextos. Un contrato congelado con dos convenciones produce bugs de mapeo.

---

## 3. Secuencia por agregado

`occurredAt` no ordena. Una saga puede ejecutar varias transiciones dentro del mismo milisegundo, y un modelo de lectura que ordene por tiempo puede materializar el estado viejo encima del nuevo.

Reglas:

- `sequence` es un entero monotónico creciente **por `aggregateId`**, no global. Empieza en 1 con el primer evento del agregado.
- El contexto dueño del agregado lo asigna dentro de la misma transacción que escribe el outbox. No lo asigna el publicador.
- **Los consumidores aplican last-writer-wins por `sequence`.** Si llega un evento con `sequence` menor o igual al último aplicado para ese `aggregateId`, se descarta sin error. Esto cubre a la vez el reordenamiento y el reintento duplicado.
- Un hueco en la secuencia no es error: significa que ese consumidor no está ligado a todos los eventos del agregado.

Guardar el último `sequence` aplicado por `aggregateId` es responsabilidad del consumidor. En Consulta de Estado va junto a la vista materializada en Redis; en Auditoría no aplica porque inserta todo sin sobrescribir.

---

## 4. Propagación de correlación

`transactionId` y `causationId` no se llenan a mano. Si cada persona los cablea, la mitad va a mandar `null` y la correlación se rompe justo donde EAC-03 la necesita.

La base técnica provee un contexto ambiente por `AsyncLocal`:

- El **consumidor base**, al recibir un evento, coloca en el contexto ambiente el `transactionId` entrante y guarda el `eventId` entrante como causa.
- El **`IOutboxWriter`**, al escribir un evento nuevo, lee ese contexto ambiente y rellena automáticamente `transactionId` con el heredado y `causationId` con el `eventId` de la causa.
- Si no hay contexto ambiente, el origen es una acción directa de usuario: se genera un `transactionId` nuevo y `causationId` queda en `null`.

Ningún servicio implementa esto por su cuenta. Viene en `SgoLogic.Contracts` y se usa igual en los siete contextos emisores.

---

## 5. Topología del bus, modo pub/sub

| Elemento | Valor |
|---|---|
| Exchange | `sgo.eventos` |
| Tipo | `topic`, `durable: true`, `auto_delete: false` |
| Dead letter exchange | `sgo.eventos.dlx` (`fanout`, durable) |
| Cola de descarte | `sgo.eventos.dlq` (durable) |
| Persistencia de mensaje | `delivery_mode: 2` |
| `content_type` | `application/json` |
| `content_encoding` | `utf-8` |
| Cliente | RabbitMQ.Client directo, sin abstracción de terceros |

Los otros dos modos del bus quedan fuera de este documento: el canal MOM dedicado a la SAT es interno de Facturación (ADR-011) y el canal `direct` de lote hacia el ERP es interno de Integración ERP (ADR-008).

### Convención de routing key

```
<contexto>.<entidad>.<hecho-en-kebab-case>
```

El primer segmento es siempre el contexto emisor, que debe coincidir con `source`.

### Pendientes de la base técnica

Estos puntos son de HU-008 y no de ningún contrato de contexto. Se documentan aquí para que tengan dueño y no queden sueltos:

1. **Tipo de cola.** `x-delivery-limit` solo funciona en colas quorum; en colas clásicas se ignora en silencio y un mensaje venenoso se reintenta para siempre. Hay que decidir entre colas quorum, o colas clásicas con conteo de reintentos del lado del consumidor. Advertencia asociada: las colas quorum no soportan TTL por mensaje, lo que afecta el esquema de reintentos por TTL previsto en HU-004.
2. **Dueño de la DLQ.** `sgo.eventos.dlq` es una sola cola compartida por todos los consumidores. Necesita dueño nombrado, métrica de profundidad en Grafana y criterio explícito de qué se hace cuando algo cae ahí. Sin eso, en la demo habrá mensajes muertos que nadie miró.

---

## 6. Bindings de Auditoría

Auditoría se liga con `#`, no con `<contexto>.#`. Registra absolutamente todo lo que pasa por `sgo.eventos`, de cualquier contexto emisor, sin excepción. Cualquier binding más estrecho deja agujeros en la bitácora e incumple EAC-03 y el veto de Finanzas sobre trazabilidad (EV-38).

| Cola | Dueño | Binding |
|---|---|---|
| `auditoria.eventos` | Julio | `#` |

---

## 7. Enumeraciones compartidas

Comunes a todos los contextos. Las enumeraciones propias de un dominio se declaran en el contrato de su contexto.

| Enumeración | Valores permitidos |
|---|---|
| `Canal` | `WEB`, `MOVIL`, `CALL_CENTER`, `TIENDA` |
| `Moneda` | `GTQ`, `USD`, `CRC` |
| `Pais` | `GT`, `SV`, `CR` |
| `MetodoEntrega` | `DOMICILIO`, `RECOGIDA_TIENDA` |
| `TipoUbicacion` | `CENTRO_DISTRIBUCION`, `TIENDA` |
| `MedioPago` | `TARJETA`, `TRANSFERENCIA`, `BILLETERA_DIGITAL` |

---

## 8. Versionado

**Cambio aditivo (v1.x).** Agregar un campo opcional o un evento nuevo. No requiere renegociar. Se anuncia al grupo y se actualiza el documento. Los consumidores existentes siguen funcionando sin tocarse.

**Cambio rompiente (v2).** Quitar un campo, renombrarlo, cambiar su tipo, o agregar un valor a una enumeración cerrada. Requiere `eventVersion: 2`, routing key con sufijo `.v2`, publicación simultánea de ambas versiones hasta que todos los consumidores migren, y acuerdo explícito del grupo registrado en el tablero.

**Deprecación.** Un evento no se elimina mientras exista un consumidor ligado a él. Se marca como obsoleto con fecha de retiro.

---

## 9. Decisiones cerradas

| Punto | Decisión | Fecha |
|---|---|---|
| Cliente de mensajería | RabbitMQ.Client directo | 2026-09-07 |
| Motor SQL | PostgreSQL | 2026-09-07 |

El motor queda amarrado por el propio DDA: la sección 9.1 sostiene el techo de USD 1,200 mensuales (RES-11, EV-42) afirmando que Kubernetes, RabbitMQ, Redis y los motores SQL son software libre sin costo de licenciamiento. Un motor con licencia comercial en producción contradiría ese análisis de costos.
