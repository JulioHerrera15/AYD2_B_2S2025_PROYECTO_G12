# Contrato de eventos del contexto Facturación — v1

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Facturación
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-10
**Depende de:** `sgo-eventos-envelope-v1.md`, `TOPOLOGIA-MENSAJERIA.md`

---

## 0. Por qué este documento existe

Facturación cubre la primera mitad de HU-005: consume
`ordenes.fiscal.certificacion-solicitada` (evento 6 de `ordenes-eventos-v1`,
CU-04), genera el documento fiscal XML con el CUI del cliente y una serie
autorizada, lo persiste con serie y correlativo asignados **una sola vez**, y lo
envía a certificar ante la SAT de forma **asíncrona**, con reintentos por backoff
persistido (ADR-011, RES-07). Cuando la SAT devuelve la certificación, Facturación
publica el resultado por Outbox; si agota los reintentos, publica una alerta
interna. El documento nunca se pierde.

El consumo de la certificación en Órdenes (evento 12 `OrdenCertificadaFiscalmente`
y el cambio de estado de la orden a `CONFIRMADA`) **no** es parte de este bloque.

Este documento define únicamente el contenido de `data` de los eventos que
**publica** Facturación sobre `sgo.eventos`. El sobre, las reglas de idempotencia y
ordenamiento, las enumeraciones compartidas y la topología del bus están en
`sgo-eventos-envelope-v1.md` y `TOPOLOGIA-MENSAJERIA.md` y no se repiten aquí.

Facturación es el único emisor de eventos con routing key `facturacion.*`.

Trazabilidad al DDA: ADR-001, ADR-003, ADR-011, RES-07, EV-19, EV-29, EV-30,
CU-04.

---

## 1. Restricciones duras

1. **Nada de datos de tarjeta.** Ningún payload, tabla ni XML de este contexto
   lleva PAN, CVV/CVC ni fecha de vencimiento (RES-08, envelope sección 2 regla 4).
   Facturación no toca medios de pago.
2. **Serie y correlativo se asignan una sola vez**, al crear el documento, con la
   secuencia de PostgreSQL `facturacion.documento_fiscal_correlativo`. Los
   reintentos ante la SAT reusan siempre el mismo número; nunca generan uno nuevo.
3. **Idempotencia hacia la SAT por documento.** La clave de idempotencia que viaja
   a la SAT es el identificador del **documento fiscal**, no el del intento: un
   reintento no genera una certificación duplicada.

---

## 2. Enumeraciones propias del contexto

Las compartidas (`Moneda`, `Pais`, `TipoDocumentoFiscal`) están en el documento de
envelope y en `ordenes-eventos-v1`.

### 2.1 `EstadoDocumentoFiscal`

Estado del agregado `DocumentoFiscal`. Lista cerrada. **No viaja en eventos**; se
documenta para entender el flujo y se puede consultar por la API.

| Valor | Significado | Terminal |
|---|---|---|
| `PENDIENTE` | Documento generado y persistido con su XML, serie y correlativo; pendiente de certificar ante la SAT. El backoff vive en `proximo_intento_en`. | no |
| `CERTIFICADO` | La SAT devolvió folio y número de autorización. | sí |
| `ALERTA` | Se agotó `Sat:MaxIntentos`. Estado terminal de alerta para revisión manual (ADR-011, CU-04). El documento se preserva con su XML, serie y correlativo. | sí |

---

## 3. Catálogo de eventos

Dos eventos sobre `sgo.eventos`. Ninguno más se publica bajo `facturacion.*` en la
versión 1.

| # | `eventType` | Routing key | Exchange | CU | Consumidores |
|---|---|---|---|---|---|
| 1 | `DocumentoFiscalCertificado` | `facturacion.documento-fiscal.certificado` | `sgo.eventos` | CU-04 | Órdenes (bloque siguiente), Consulta de Estado |
| 2 | `CertificacionSatFallida` | `facturacion.documento-fiscal.certificacion-fallida` | `sgo.eventos` | CU-04 | Operaciones / alerta interna, Consulta de Estado |

Auditoría consume los dos por su binding `#` y no se lista fila por fila.

### 3.1 Mensaje interno del canal MOM de la SAT

`SolicitudEnvioSat` **no es un evento de dominio**. Viaja por el exchange direct
`sgo.sat` (canal MOM interno de Facturación, `TOPOLOGIA-MENSAJERIA` sección 1,
ADR-011), no por `sgo.eventos`. Lo produce el worker de reintentos de Facturación y
lo consume el enviador a la SAT del mismo contexto. Se documenta aquí sólo para
dejar registro del canal.

| Campo | Valor |
|---|---|
| `eventType` | `SolicitudEnvioSat` |
| Exchange | `sgo.sat` (direct, durable) |
| Routing key | `sat.certificacion` |
| Cola | `facturacion.sat` (quorum, dead letter a `sgo.eventos.dlx`) |
| `data` | `{ "documentoFiscalId": "<guid>", "ordenId": "ORD-...", "intento": 1 }` |

Se encola por `IOutboxWriter` (la regla dura del envelope prohíbe publicar directo
al broker), con el exchange y la routing key de `sgo.sat`. `intento` permite al
enviador descartar un reencolado obsoleto para no contar dos veces contra
`Sat:MaxIntentos`.

---

## 4. Bindings

| Cola | Dueño | Bindings | Exchange |
|---|---|---|---|
| `facturacion.ordenes` | Angel (Facturación) | `ordenes.fiscal.certificacion-solicitada` | `sgo.eventos` |
| `facturacion.sat` | Angel (Facturación) | `sat.certificacion` | `sgo.sat` |
| `ordenes.facturacion` (bloque siguiente) | Angel (Órdenes) | `facturacion.documento-fiscal.certificado` | `sgo.eventos` |
| `consulta-estado.facturacion` | Jonathan | `facturacion.documento-fiscal.*` | `sgo.eventos` |
| `auditoria.eventos` | Julio | `#` | `sgo.eventos` |

**Pendiente explícito.** `ordenes-eventos-v1` sección 4 liga la cola
`facturacion.ordenes` también a `ordenes.devolucion.registrada` (nota de crédito,
HU-014). Ese evento aún no existe; el binding se agregará cuando exista, y traerá
al menos un evento `facturacion.nota-credito.*`.

---

## 5. Envelope aplicado a este contexto

| Campo | Valor en este contexto |
|---|---|
| `source` | `"facturacion"` |
| `aggregateType` | `"DocumentoFiscal"` |
| `aggregateId` | El identificador interno del documento fiscal (GUID en texto). |
| `sequence` | Asignado por Facturación, monotónico por `aggregateId` (por documento). Empieza en 1. Cada mensaje que Facturación encola por Outbox para ese documento —incluidas las `SolicitudEnvioSat` internas— consume un valor. |
| `transactionId` / `causationId` | Heredados del evento `CertificacionFiscalSolicitada` consumido. Facturación nunca los pasa a mano; los reintentos por el worker de polling reusan el `transactionId` guardado en el documento para no romper la correlación de la saga. |

---

## 6. Reglas de emisión

1. **Un documento fiscal produce a lo sumo un evento de desenlace**: o bien
   `DocumentoFiscalCertificado` cuando la SAT certifica, o bien
   `CertificacionSatFallida` cuando se agotan `Sat:MaxIntentos`. Nunca los dos.
2. **El evento se encola en la misma transacción** que registra el cambio de
   estado del documento (el enviador a la SAT hace un único `SaveChangesAsync` que
   cubre el documento, la fila de outbox y la marca de mensaje procesado).
3. **Un fallo de la SAT no es un error de proceso.** El enviador no nackea el
   mensaje de `sgo.sat` por un rechazo, timeout o 5xx: registra el desenlace de
   negocio (programa el reintento con backoff en `proximo_intento_en`) y ackea.
   Sólo nackea ante errores técnicos de su propio lado (base caída).
4. **El backoff vive en la columna `proximo_intento_en`, no en memoria.** Un
   worker de polling toma los documentos vencidos y reencola una `SolicitudEnvioSat`.
   Backoff exponencial con jitter, base `Sat:BackoffBaseMs` (2000 ms), tope
   `Sat:MaxIntentos` (4).
5. **`sequence`** lo lleva el agregado `DocumentoFiscal`. No se inventa.

---

## 7. Catálogo detallado

En las secciones siguientes sólo se muestra el contenido de `data`.

### 7.1 `DocumentoFiscalCertificado`

Routing key `facturacion.documento-fiscal.certificado`. Se emite cuando la SAT
devuelve la certificación del documento. En el bloque siguiente lo consumirá
Órdenes para emitir `OrdenCertificadaFiscalmente` (evento 12 de
`ordenes-eventos-v1`) y pasar la orden de `CONFIRMADA_PENDIENTE_FISCAL` a
`CONFIRMADA`.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `documentoFiscalId` | string | sí | Identificador interno del documento. |
| `ordenId` | string | sí | `ORD-2026-000123`. |
| `tipoDocumento` | string | sí | Enum `TipoDocumentoFiscal`. |
| `serie` | string | sí | Serie autorizada asignada al crear el documento. |
| `numeroDocumento` | string | sí | Correlativo formateado a ocho dígitos. |
| `numeroAutorizacion` | string | sí | Autorización devuelta por la SAT. |
| `folio` | string | sí | Folio devuelto por la SAT. |
| `moneda` | string | sí | Enum `Moneda`. |
| `total` | string | sí | Decimal como cadena. Debe cuadrar con el `total` de la orden. |
| `certificadaEn` | string | sí | ISO 8601 UTC, momento de la respuesta de la SAT. |

```json
{
  "documentoFiscalId": "5e4f3a5f-987a-42ca-888e-063c640ef111",
  "ordenId": "ORD-2026-000123",
  "tipoDocumento": "FACTURA",
  "serie": "A",
  "numeroDocumento": "00000001",
  "numeroAutorizacion": "89958267-C72E-4760-B3A8-490454540678",
  "folio": "FOL-D19FEE774074",
  "moneda": "GTQ",
  "total": "1400.00",
  "certificadaEn": "2026-09-10T14:32:46.557Z"
}
```

---

### 7.2 `CertificacionSatFallida`

Routing key `facturacion.documento-fiscal.certificacion-fallida`. Se emite cuando
el documento agota `Sat:MaxIntentos` contra la SAT y pasa al estado terminal
`ALERTA` para revisión manual (ADR-011). Es una alerta interna: el documento no se
pierde, queda persistido en `ALERTA` con su XML, serie y correlativo.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `documentoFiscalId` | string | sí | |
| `ordenId` | string | sí | |
| `tipoDocumento` | string | sí | Enum `TipoDocumentoFiscal`. |
| `serie` | string | sí | |
| `numeroDocumento` | string | sí | |
| `intentos` | integer | sí | Igual a `Sat:MaxIntentos`. |
| `ultimoCodigo` | string | sí | Código de la última falla (`SAT_NO_DISPONIBLE`, `TIMEOUT`, `RED`, o el código de rechazo de la SAT). |
| `ultimoMotivo` | string | sí | Texto corto legible de la última falla. |
| `fallidaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "documentoFiscalId": "7a1c9e33-0b12-4f88-9d54-77b0a2c9e611",
  "ordenId": "ORD-2026-000998",
  "tipoDocumento": "FACTURA",
  "serie": "A",
  "numeroDocumento": "00000002",
  "intentos": 4,
  "ultimoCodigo": "SAT_NO_DISPONIBLE",
  "ultimoMotivo": "La SAT respondio 503.",
  "fallidaEn": "2026-09-10T14:33:47.512Z"
}
```

---

## 8. Matriz de consumo

| Consumidor | Dueño | HU | Eventos | Para qué |
|---|---|---|---|---|
| Auditoría | Julio | HU-006 | Los dos, por binding `#` | Bitácora append-only indexada por `transactionId` (EV-30, EAC-03). |
| Órdenes | Angel | HU-005 (bloque siguiente) | 1 | Emitir `OrdenCertificadaFiscalmente` y pasar la orden a `CONFIRMADA`. |
| Consulta de Estado | Jonathan | HU-010 | `facturacion.documento-fiscal.*` | Enriquecer la vista 360 con el estado fiscal y el folio. No materializa estado de Orden. |
| Operaciones | — | CU-04 | 2 | Alerta para revisión manual de documentos que agotaron reintentos. |

---

## 9. Congelamiento

Una vez congelado, cualquier cambio se avisa al grupo antes de hacerlo.

| Revisor | Contexto | Estado | Fecha |
|---|---|---|---|
| Angel Martínez (202300410) | Facturación, Órdenes, Pagos | autor | 2026-09-10 |
| Julio Herrera | Auditoría, base técnica | pendiente | |
| Jonathan Jerónimo | Consulta de Estado | pendiente | |
| Francisco Castillo | responsable ADR-011 | pendiente | |

---

## 10. Supuestos abiertos

1. **Nota de crédito (HU-014).** `facturacion.ordenes` se ligará a
   `ordenes.devolucion.registrada` cuando ese evento exista; agregará al menos un
   evento `facturacion.nota-credito.*`.
2. **Número de documento a ocho dígitos.** El ejemplo de `ordenes-eventos-v1`
   muestra seis (`"005510"`); acá se usan ocho para holgura frente a la secuencia.
   El contrato de Órdenes lo trata como cadena opaca, así que no rompe nada.
3. **Serie única global.** En este bloque la serie sale de configuración
   (`Sat:Serie`, por defecto `"A"`) y es la misma para todos los documentos. Un
   esquema de series por tienda o por tipo de documento queda fuera de alcance.
4. **XML fiscal.** El XML es una representación propia del proyecto
   (`urn:sgo-logic:facturacion:xml:v1`), no el formato FEL real de la SAT de
   Guatemala. El mock de la SAT lo recibe como texto y no lo valida contra XSD.
```
