# Contrato del contexto Crédito — v1

**Proyecto:** SGO-Logic — Fase 3
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto:** Crédito ("Compra ahora, paga en 30 días")
**Servicio:** `credito-api` (puerto 8099 en el host, base `sgo_credito` en `db-finanzas`, esquema `credito`)
**Estado:** bloque 1 de 4 (perfil, política, oferta y "Mi crédito")
**Fecha:** 2026-10-02
**Depende de:** `sgo-eventos-envelope-v1.md`

---

## 1. Alcance de este bloque

Crédito mantiene un **perfil crediticio precalculado** por cliente a partir del
historial comercial que entrega el ERP. La consulta de oferta solo lee ese perfil:
no recalcula ni llama a otro servicio, así que el checkout puede consultarla en
cada carga sin costo.

Fuera de este bloque: cargo y abono a la línea (saldo utilizado), el medio de pago
`CREDITO` en la saga de Órdenes, el checkout y el bloqueo de líneas. Hasta
entonces `saldoUtilizado` es 0 y el estado `BLOQUEADA` no se usa.

El cliente se identifica siempre por su **email en minúsculas**, el mismo
identificador que usan Órdenes y Pagos.

## 2. Eventos

Ambos viajan en el envelope congelado (`IntegrationEvent<TData>`) por el exchange
`sgo.eventos`, publicados vía Outbox. Montos como cadena con dos decimales,
fechas UTC ISO 8601.

### 2.1 `HistorialClienteErpActualizado` (lo consume Crédito)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `HistorialClienteErpActualizado` |
| `routingKey` | `integracion-erp.cliente.historial-actualizado` |
| `aggregateType` | `ClienteErp` |
| `aggregateId` | email del cliente en minúsculas |
| `source` | `integracion-erp` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `clienteId` | string | Email del cliente en minúsculas. |
| `codigoClienteErp` | string | Código del cliente en el ERP. |
| `clienteDesde` | fecha | Alta del cliente. |
| `comprasUltimos12Meses` | int | Compras de los últimos 12 meses. |
| `montoCompradoUltimos12Meses` | decimal | Monto comprado en los últimos 12 meses. |
| `moneda` | string | `GTQ`. |
| `pagosPuntuales` | int | Pagos hechos a tiempo. |
| `pagosTardios` | int | Pagos hechos con atraso. |
| `maxDiasAtraso` | int | Mayor atraso registrado, en días (informativo, no puntúa en v1). |
| `devoluciones` | int | Compras que terminaron en devolución. |
| `incidentesGraves` | int | Casos graves abiertos (descartan el crédito). |
| `otrosIncidentes` | int | Reclamos menores. |
| `fechaCorte` | fecha | Corte del ERP. Un historial con `fechaCorte` igual o anterior al ya aplicado se ignora. |
| `loteOrigen` | string? | Identificador opcional del lote o ciclo de origen. |

Crédito lo consume en la cola propia `credito.erp` (quorum, con dead letter) con
`EventConsumerBase`: deduplicación por `eventId` en `credito.mensajes_procesados`,
y el historial, perfil, línea, outbox y marca de procesado se guardan en una sola
transacción.

### 2.2 `PerfilCrediticioActualizado` (lo publica Crédito)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `PerfilCrediticioActualizado` |
| `routingKey` | `credito.perfil.actualizado` |
| `aggregateType` | `PerfilCrediticio` |
| `aggregateId` | email del cliente en minúsculas |
| `source` | `credito` |
| `sequence` | Contador propio del perfil; sube solo cuando se emite el evento. |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `clienteId` | string | Email del cliente. |
| `puntaje` | int | 0 a 100. Uso interno; nunca se muestra al cliente. |
| `nivel` | string | `EXCELENTE`, `BUENO`, `REGULAR` o `NO_CALIFICA`. |
| `elegible` | bool | Puede comprar a crédito. |
| `codigoMotivo` | string? | Motivo del descarte si no es elegible (sección 4.1); null si es elegible. |
| `limiteAprobado` | decimal | Límite de la línea; 0 si no es elegible. |
| `moneda` | string | `GTQ`. |
| `versionPolitica` | string | `2026.10-v1`. |
| `fechaCorteHistorial` | fecha | `fechaCorte` del historial evaluado. |
| `calculadoEn` | fecha | Momento del cálculo. |

Se emite **solo si cambió** el puntaje, el nivel, la elegibilidad, el motivo o el
límite. Un recálculo con el mismo resultado no emite nada. Hoy no tiene
consumidores además de Auditoría (por `#`).

## 3. Endpoints

### 3.1 `GET /api/credito/elegibilidad?monto={decimal opcional}`

Enrutado por el gateway con `RequireAuthenticatedUser`. Requiere JWT; el cliente
sale **siempre** del claim `email`, nunca de la query ni del cuerpo (un
`clienteId` en la query se ignora). `monto` menor o igual a 0 responde 400. Sin
claim de email, 401.

Respuesta 200 (`OfertaCreditoDto`, sin puntaje):

```json
{
  "estado": "DISPONIBLE",
  "ofrecer": true,
  "codigoMotivo": null,
  "titulo": "Tienes crédito disponible",
  "mensaje": "Tienes Q3,500.00 disponibles de un límite de Q3,500.00. ...",
  "recomendacion": "Elige crédito al pagar y cancela el total desde Mi crédito dentro de los 30 días.",
  "limiteAprobado": "3500.00",
  "disponible": "3500.00",
  "montoSolicitado": "1200.00",
  "moneda": "GTQ",
  "plazoDias": 30,
  "fechaPagoEstimada": "2026-11-01T00:00:00.000Z",
  "calculadoEn": "2026-10-02T17:10:00.000Z"
}
```

| `estado` | Cuándo |
|---|---|
| `DISPONIBLE` | Elegible, línea `ACTIVA` y el disponible cubre el monto (o no se envió monto). Único estado con `ofrecer = true` y `fechaPagoEstimada` (hoy + 30 días). |
| `CUPO_INSUFICIENTE` | Elegible y línea activa, pero el disponible es menor que el monto. |
| `NO_ELEGIBLE` | Hay perfil y no es elegible; `codigoMotivo` dice por qué. |
| `SIN_HISTORIAL` | No hay perfil del cliente. |
| `LINEA_BLOQUEADA` | Reservado para un bloque posterior. |

`titulo`, `mensaje` y `recomendacion` salen de `CatalogoMensajesCredito`: lenguaje
llano, sin códigos internos, sin hablar de puntaje.

### 3.2 `GET /api/credito/mi-credito`

Mismo enrutamiento y misma regla de identidad. Respuesta 200 (`MiCreditoDto`, sin
puntaje): `estado`, `nivel`, `etiquetaNivel`, `titulo`, `mensaje`,
`recomendacion`, `limiteAprobado`, `saldoUtilizado`, `disponible`, `moneda`,
`plazoDias`, `actualizadoEn` y `factores[]` con `nombre`, `valoracion` y
`descripcion`.

Los cuatro factores para el cliente agrupan los de la política: "Pagos a tiempo"
(puntualidad), "Monto de tus compras" (monto), "Tiempo y frecuencia como cliente"
(antigüedad + frecuencia) y "Devoluciones y reclamos" (devoluciones + otros
incidentes). `valoracion` según puntos obtenidos sobre el máximo del factor: 80% o
más `FAVORABLE`, 50% o más `ACEPTABLE`, menos `A_MEJORAR`.

### 3.3 `POST /interno/credito/historiales`

Solo existe con `Credito:EndpointsInternos=true` (falso por defecto, verdadero en
`docker-compose.yml`). **No se enruta por el gateway**: se llama directo a
`http://localhost:8099`. Sirve para demo y pruebas mientras no exista el worker
del ERP.

Cuerpo: igual al `data` de `HistorialClienteErpActualizado`. Aplica el historial
con la misma lógica que el consumidor y responde 200 con `clienteId`, `aplicado`
(false si la fecha de corte no es posterior a la vigente), `huboCambios`,
`puntaje`, `nivel`, `elegible`, `codigoMotivo`, `limiteAprobado`, `moneda`,
`versionPolitica`, `fechaCorteHistorial` y `calculadoEn`. 400 si el cuerpo falta,
no trae cliente o trae valores negativos.

## 4. Política `2026.10-v1`

Todos los números son configurables en `Credito:Politica` (`appsettings.json`);
estos son los valores por defecto. Cambiarlos es una política nueva y debe ir con
otra `VersionPolitica`.

### 4.1 Descartes directos

Se evalúan en este orden; el primero que aplica define `codigoMotivo`:

1. `SIN_HISTORIAL`: no hay historial del cliente.
2. `INCIDENTE_GRAVE`: `incidentesGraves > 0`.
3. `ANTIGUEDAD_INSUFICIENTE`: menos de 90 días desde `clienteDesde`.
4. `DEVOLUCIONES_EXCESIVAS`: `devoluciones / comprasUltimos12Meses > 0.30`.
5. `PUNTAJE_INSUFICIENTE`: puntaje menor que 60.

Un cliente descartado queda `NO_CALIFICA`, con límite 0 y línea `INACTIVA`,
aunque su puntaje sea alto.

### 4.2 Puntaje (0 a 100)

Se calcula siempre, aunque haya descarte.

| Factor | Máximo | Cálculo |
|---|---|---|
| Puntualidad | 40 | `40 * puntuales / (puntuales + tardíos)`; sin pagos registrados, 20. |
| Monto | 30 | `30 * min(monto12m / 12000, 1)`. |
| Antigüedad | 8 | `8 * min(díasComoCliente / 730, 1)`. |
| Frecuencia | 7 | `7 * min(compras12m / 12, 1)`. |
| Devoluciones | 10 | `10 * (1 - min(tasa / 0.30, 1))`, con tasa = devoluciones / compras (0 sin compras). |
| Incidentes | 5 | 5 con 0 `otrosIncidentes`, 2.5 con 1, 0 con 2 o más. |

Puntaje = suma redondeada a entero con `MidpointRounding.AwayFromZero`.

### 4.3 Nivel y límite

| Puntaje | Nivel | Límite (GTQ) |
|---|---|---|
| 90 o más | `EXCELENTE` | 7,500 |
| 75 a 89 | `BUENO` | 3,500 |
| 60 a 74 | `REGULAR` | 1,500 |
| menos de 60 | `NO_CALIFICA` | 0 |

Plazo: 30 días. La línea conserva `saldoUtilizado` al recalcular;
`disponible = limiteAprobado - saldoUtilizado`, nunca menor que 0.

## 5. Contrato para Integración ERP y mock ERP (pendiente, lo implementa su dueño)

Crédito ya consume `HistorialClienteErpActualizado` y, mientras tanto, se alimenta
con la siembra de demo (`Credito:SembrarDemo`) y con `POST /interno/credito/historiales`.
Lo siguiente queda escrito para que lo implemente el dueño de `Mocks/mock-erp` y de
`SgoLogic.IntegracionErp.Worker`.

### 5.1 Mock ERP

**`GET /erp/clientes/historial?modificadosDesde={ISO-8601 opcional}`**

Responde 200 con los clientes cuyo historial cambió desde `modificadosDesde` (o
todos si no viene):

```json
{
  "fechaCorte": "2026-10-02T06:00:00.000Z",
  "total": 6,
  "clientes": [
    {
      "codigoCliente": "ERP-CLI-0001",
      "email": "cliente1@sgo.local",
      "clienteDesde": "2023-09-28T00:00:00.000Z",
      "comprasUltimos12Meses": 18,
      "montoCompradoUltimos12Meses": 14500.00,
      "moneda": "GTQ",
      "pagosPuntuales": 18,
      "pagosTardios": 0,
      "maxDiasAtraso": 0,
      "devoluciones": 1,
      "incidentesGraves": 0,
      "otrosIncidentes": 0,
      "actualizadoEn": "2026-10-02T06:00:00.000Z"
    }
  ]
}
```

Respeta el modo caída igual que `POST /erp/lotes` (`POST /erp/admin/caida`):
`timeout` no responde a tiempo (504 tardío), `error500` responde 500 y
`no_disponible` responde 503.

Datos iniciales: los mismos seis clientes de la siembra de demo de Crédito
(`clienteDesde` = hoy menos los días indicados):

| email | días como cliente | compras 12m | monto 12m | puntuales | tardíos | máx. atraso | devoluciones | graves | otros |
|---|---|---|---|---|---|---|---|---|---|
| cliente1@sgo.local | 1100 | 18 | 14500 | 18 | 0 | 0 | 1 | 0 | 0 |
| cliente2@sgo.local | 500 | 9 | 8400 | 8 | 1 | 6 | 1 | 0 | 0 |
| cliente3@sgo.local | 300 | 5 | 6000 | 4 | 1 | 12 | 1 | 0 | 0 |
| cliente4@sgo.local | 800 | 10 | 9000 | 3 | 7 | 45 | 2 | 0 | 1 |
| cliente5@sgo.local | 40 | 2 | 900 | 2 | 0 | 0 | 0 | 0 | 0 |
| cliente6@sgo.local | 900 | 14 | 11000 | 14 | 0 | 0 | 1 | 1 | 0 |

`demo@sgo.local` no está a propósito: es el caso `SIN_HISTORIAL`.

**`PUT /erp/admin/clientes/{email}/historial`**: reemplaza el historial de un
cliente para pruebas (mismo cuerpo que un elemento de `clientes`, sin `email`) y
actualiza su `actualizadoEn`.

**`POST /erp/admin/reset`**: además de lo que ya reinicia, restaura los seis
clientes iniciales.

### 5.2 Integración ERP

- Worker nuevo `ErpHistorialClientesWorker`: corre al arrancar y luego cada
  `Erp:IntervaloHistorialMinutos` (por defecto 30).
- Método nuevo en `IClienteErp` para `GET /erp/clientes/historial`, con la misma
  política de reintentos que `EnviarLoteAsync` (`Erp:MaxReintentos`, backoff
  exponencial de 250 ms hasta 4 s, reintenta 500, 503, 504, timeout y error de red).
- Lock propio de PostgreSQL `sgo-logic.integracion-erp.historial-clientes`. No
  reutilizar el de lotes (`sgo-logic.integracion-erp.batch`): son ciclos
  independientes.
- Tabla propia con la última versión publicada por cliente (por ejemplo, email,
  `actualizadoEn` y un hash del contenido) para publicar solo los clientes que
  cambiaron.
- Por cada cliente que cambió, un `HistorialClienteErpActualizado` vía outbox, con
  `aggregateId` = email en minúsculas y `data.fechaCorte` = `fechaCorte` de la
  respuesta; `data.clienteId` = email en minúsculas y
  `data.codigoClienteErp` = `codigoCliente`.
- Si el ERP está caído (reintentos agotados) no publica nada y lo intenta en el
  siguiente ciclo. Crédito sigue respondiendo con el último perfil calculado.

## 6. Configuración

| Clave | Por defecto | En `docker-compose.yml` |
|---|---|---|
| `Credito:SembrarDemo` | `false` | `true` |
| `Credito:EndpointsInternos` | `false` | `true` |
| `Credito:Politica:*` | sección 4 | sin cambios |
| `Auth:JwtSecret` | vacío (todo 401) | `${JWT_SECRET}` |
| `Outbox:Source` | `credito` | `credito` |

Métricas propias en `/metrics`: `credito_consultas_elegibilidad_total{estado}` y
`credito_perfiles_recalculados_total`, además de las transversales de outbox y
consumo.
