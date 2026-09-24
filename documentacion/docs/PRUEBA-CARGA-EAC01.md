# Prueba de carga EAC-01 / HU-023 — cero ordenes perdidas si PayKlik falla

Esta prueba responde una sola pregunta: si PayKlik (la pasarela de pago) se
cae durante el pico de Black Friday, ¿se pierde alguna orden? El criterio de
aceptacion de HU-023 es cero.

## Como correrla

Requiere el stack local arriba (`docker compose up -d` desde `SGO-Logic/`).
No instala nada en la maquina: k6 y `psql` corren dentro de contenedores.

```bash
k6 run -e ORDENES_POR_MINUTO=80 -e RUN_ID=<id> scripts/pruebas-carga/eac01-hu023-carga.js
```

**Paso obligatorio: esperar el drenado completo antes de verificar.** El
procesamiento del cobro es asincrono (outbox + cola + reintentos) y un minuto
fijo no alcanza: en una corrida del 2026-09-22, lanzar la verificacion 19
segundos despues del fin de la carga dio 98 ordenes "perdidas" que en realidad
seguian en transito. Antes de verificar, repetir esta consulta hasta que
devuelva 0 (ninguna orden de la corrida en `CREADA`, `RESERVA_SOLICITADA`,
`INVENTARIO_RESERVADO` o `COBRO_SOLICITADO`):

```bash
docker compose exec -T db-operacional psql -U sgo_admin -d sgo_ordenes -tA -c "SELECT count(*) FROM ordenes.orden WHERE cliente_id LIKE 'CARGA-EAC01-<id>-%' AND estado IN ('CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO');"
```

Con 0 ordenes en proceso, correr la verificacion con el mismo `RUN_ID`, para
no mezclar ordenes de corridas anteriores:

```bash
RUN_ID=<id> bash scripts/pruebas-carga/eac01-hu023-verificar.sh
```

Ambos comandos se ejecutan desde `SGO-Logic/`. La red `sgo-logic_default` es
la que crea este `docker-compose.yml` (confirmada con `docker network ls`);
si tu proyecto de compose tiene otro nombre, ajusta el `--network`.

## Que mide

El script de carga (`eac01-hu023-carga.js`) crea ordenes de forma continua
durante toda la corrida y, por cada una, la lleva hasta el punto donde
arranca el cobro real contra PayKlik: crear -> confirmar. No se fuerza
ninguna transicion. La saga avanza sola hasta el cobro: RESERVA_SOLICITADA ->
INVENTARIO_RESERVADO -> COBRO_SOLICITADO -> PAGO_APLICADO (o PAGO_PENDIENTE
mientras PayKlik esta caido). La prueba no espera el despacho ni la entrega:
EAC-01 mide el cobro.

El script siembra existencias contra inventario-api (puerto 8094) al
inicio de la corrida, con suficiente stock para todo el volumen de la
prueba. Cada orden consume una unidad del SKU-CARGA-EAC01 en CD-01.

Cada orden se paga con `TARJETA`, que exige `tokenMedioPago` (ADR-010,
DDA 14.3.1). El token lo emite PayKlik (`POST /v1/tokens`, puerto 8084), que
en modo CAIDO tampoco tokeniza. Por eso el `setup()` del script emite un token
por orden con PayKlik en modo normal, antes de la caida: modela clientes que
ya tokenizaron cuando el proveedor cae, y permite que las ordenes creadas
durante la caida lleguen al cobro, que es lo que EAC-01 mide. Los tokens son de
un solo uso y su vigencia (900 s) cubre toda la corrida. Solo el token llega a
Ordenes y a Pagos.

La corrida tiene tres fases, con el mock de PayKlik cambiando de modo contra
su endpoint de administracion mientras la carga sigue constante:

1. **Calentamiento** (90 s) — PayKlik en modo normal.
2. **Caida** (120 s) — PayKlik responde 503 a todo cobro.
3. **Recuperacion** (90 s) — PayKlik vuelve a modo normal.

El volumen de carga es 80 ordenes por minuto (tasa de llegada expresada por
minuto en k6; hasta la corrida del 2026-09-19 el script la redondeaba a
ordenes por segundo y en la practica generaba 60 por minuto). El DDA no fija un numero
absoluto para el pico de "ocho veces" — solo dice que es 8x un dia normal sin
decir cuanto es un dia normal. Se derivo ese numero de la unica magnitud de
escala que el DDA si da (las 32 tiendas del canal fisico, EV-16): a un ritmo
conservador de 1 orden cada 3 minutos por tienda en la hora pico de un dia
normal, el canal fisico solo ya da ~11 ordenes/minuto; aplicando el factor 8x
declarado da ~85/minuto, redondeado hacia abajo a 80. Es una estimacion de
ingenieria, documentada como tal, no un dato oficial del cliente — se ajusta
sin tocar el script con la variable `ORDENES_POR_MINUTO`.

El script de carga no espera el resultado del cobro: la saga procesa el
cobro de forma asincrona via outbox (CobroSolicitado encola en Pagos,
que llama a PayKlik). Esa es justamente la garantia que EAC-01 exige
(el fallo del proveedor no bloquea el flujo), y es lo que permite que la
carga generadora corra a ritmo constante sin importar si PayKlik esta caido.

La verificacion (`eac01-hu023-verificar.sh`) corre despues, contra las bases
reales (`sgo_ordenes`, `sgo_consulta_estado` y `sgo_pagos`), y cuenta:

- ordenes creadas,
- cuantas quedaron en un estado consultable (con fila en el modelo de
  lectura de Consulta de Estado),
- cuantas en `PAGO_APLICADO` y cuantas en `PAGO_PENDIENTE`,
- cuantas quedaron atascadas en `COBRO_SOLICITADO` sin ningun intento de
  pago registrado,
- cuantos intentos de pago hay por resultado (iniciado/exitoso/fallido/
  rechazado),
- cuantos intentos fallidos tienen su evento de resultado de Pagos
  (`PagoAplicado`, `PagoRechazado` o `PagoPendiente`, con la misma orden y el
  mismo numero de intento) en la bitacora de Auditoria (`sgo_auditoria`), y
  el porcentaje que representan.

El script de carga reporta, ademas del resumen estandar de k6, la metrica
`sgo_latencia_orden_ms` con la latencia de las peticiones sincronas de la
orden (crear y confirmar), sin la tokenizacion del `setup()`. Sus p95 y p99
son los valores de latencia que se registran.

## Umbral de aprobacion

Una orden se cuenta como **perdida** si, al terminar la espera de drenado,
no tiene ningun intento de pago registrado en `pagos.intento_pago`, o si
quedo en un estado no consultable. El umbral es **cero órdenes perdidas**.
Si el conteo da cero, la prueba aprueba; cualquier numero mayor que cero es
un fallo de HU-023, sin importar cuantas ordenes se hayan procesado bien.

EAC-01 fija ademas un segundo umbral: **100% de intentos fallidos
auditados**. El verificador lo informa en una linea propia
(`UMBRAL_AUDITORIA=CUMPLE` o `NO_CUMPLE`), separada de `RESULTADO`, que sigue
siendo el criterio de HU-023 (cero ordenes perdidas).

## Que queda fuera de esta prueba, y por que

- **El 99.9% de disponibilidad de Pagos que tambien pide EAC-01.** Eso es una
  metrica de uptime sobre una ventana operativa real; una corrida local de
  unos minutos no la puede medir de forma significativa. Esta prueba valida
  la garantia de no-perdida, no la disponibilidad del servicio.
- **El autoescalado horizontal (HPA).** Las tareas de HU-023 lo mencionan,
  pero el entorno de este MVP es docker compose de una sola instancia por
  servicio (ADR-013), no Kubernetes: no hay HPA que probar todavia. Esta
  prueba corre contra esa unica instancia.
- **Despacho y LogiExpress.** La orden no se lleva a `DESPACHO_SOLICITADO` ni
  mas alla, y el umbral de aprobacion no depende de ello. Ese tramo se ejerce
  con `scripts/pruebas/camino-feliz.ps1`.
- **EAC-02 (consulta de estado bajo 5 s) y EAC-04 (escalabilidad sostenida).**
  Son HU-024 y HU-025, con sus propias pruebas. Esta prueba toca la vista de
  Consulta de Estado solo para saber si la orden es consultable, no mide su
  latencia.
- **El numero exacto de "ocho veces".** El DDA no lo fija; el numero usado
  aqui (80 ordenes/minuto) es una estimacion documentada arriba, no una cifra
  contractual. Si el negocio publica una cifra oficial, se reemplaza
  cambiando una sola variable de entorno.

## Evidencia vigente (2026-09-22)

Corrida con `RUN_ID=20260922b` (prefijo `CARGA-EAC01-20260922b-`), 80 ordenes
por minuto, PayKlik en modo CAIDO entre t=90 s y t=210 s. La verificacion se
corrio despues del drenado completo: la carga termino a las 18:35:37, la consulta de
drenado devolvio 0 ordenes en proceso a las 18:38:41 y el verificador corrio 15
segundos despues.

Archivos, en `SGO-Logic/scripts/pruebas/evidencia/`:

- `eac01-carga-2026-09-22.txt`: salida completa de k6.
- `eac01-verificacion-2026-09-22.txt`: salida de `eac01-hu023-verificar.sh`.
- `metricas-ordenes-2026-09-22.txt`: `/metrics` de Ordenes, tomado con
  `outbox_pendientes` en 0.
- `camino-feliz-2026-09-22.txt`: salida de `scripts/pruebas/camino-feliz.ps1`
  en el mismo stack (orden ORD-2026-000671, ENTREGADA en 19 236 ms, sin pasos
  forzados).

| Medida | Umbral | Valor medido | Cumple |
|---|---|---|---|
| Ordenes perdidas por indisponibilidad de PayKlik | 0 | 0 de 379 creadas (379 consultables, 0 sin intento de pago) | Si |
| Intentos fallidos auditados | 100% | 100.00% (130 de 130; 117 ordenes, 13 de ellas con dos intentos fallidos) | Si |
| Disponibilidad de Pagos durante el evento | 99.9% | No medida en esta prueba (ver la seccion anterior) | No aplica |

Otros valores de la corrida: 379 ordenes enviadas, 100% de checks de k6
exitosos, 0% de peticiones HTTP fallidas, latencia de crear y confirmar
(`sgo_latencia_orden_ms`) con p95 de 12.78 ms y p99 de 16.88 ms. Al cierre del
drenado: 214 ordenes en `DESPACHO_SOLICITADO`, 48 en
`CONFIRMADA_PENDIENTE_FISCAL` y 117 en `PAGO_PENDIENTE`; 262 intentos
`EXITOSO` y 130 `FALLIDO`. En Auditoria, 130 `IntentoPagoFallido`, 117
`PagoPendiente` (uno por orden pendiente) y 262 `PagoAplicado`. Ordenes
termino con `outbox_fallidos_total` en 0.

### Por que se regenero la evidencia

La evidencia del 2026-09-19 (285 ordenes, 0 perdidas) dejo de ser vigente por
tres razones:

1. **El criterio de auditoria no se media.** El verificador de entonces solo
   contaba ordenes perdidas. Medida con el criterio actual, esa corrida tenia
   **90.30%** de intentos fallidos auditados (121 de 134): con el reintento
   automatico de Pagos (`MaxReintentos` = 1), un cobro puede fallar dos veces y
   solo el ultimo intento llegaba a Auditoria, dentro de `PagoPendiente`. Una
   corrida del 2026-09-22 con el codigo anterior dio 90.78% (128 de 141). Desde
   el 2026-09-22 cada intento fallido emite `IntentoPagoFallido`
   (`pagos-eventos-v1` v1.3), sin cambiar el evento de resultado que ve la
   saga.
2. **El volumen era menor al declarado.** El script redondeaba la tasa a
   ordenes por segundo y generaba 60 por minuto, no 80.
3. **El camino probado cambio.** La tokenizacion delegada a PayKlik (2026-09-20)
   obliga a enviar `tokenMedioPago`, y el endpoint de transiciones manuales de
   Ordenes se elimino el 2026-09-21.

Los archivos `-2026-09-19` se eliminaron; quedan en el historial de git.
