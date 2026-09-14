# Concurrencia de inventario EAC-05 / HU-026 — ninguna unidad a dos clientes

Esta prueba responde una sola pregunta: con muchas compras simultaneas sobre el
mismo stock, ¿puede una unidad quedar comprometida a dos clientes? El criterio de
aceptacion es que no.

## Que se prueba y por que no hizo falta codigo nuevo

La garantia ya existe desde HU-001 por el bloqueo optimista de ADR-005: la
columna `version` de `inventario.existencia` es token de concurrencia, asi que
todo `UPDATE` la lleva en el `WHERE`. Dos transacciones que leyeron la misma
version no pueden escribir las dos: la segunda no afecta ninguna fila, EF Core lo
reporta como `DbUpdateConcurrencyException` y la `UnidadDeTrabajo` lo traduce a
`ExcepcionConflictoConcurrencia`, que el endpoint devuelve como 409.

Lo que HU-026 aporta no es logica sino **evidencia reproducible**.

## Como correrla

```bash
dotnet test tests/SgoLogic.Inventario.IntegrationTests --filter ReservaConcurrenteTests
```

Requiere Docker: la suite levanta un PostgreSQL real con Testcontainers y corre
los servicios de aplicacion de verdad, sin dobles. Cada intento de reserva usa su
propio alcance de inyeccion (conexion y `DbContext` propios), que es lo que hace
que la concurrencia sea real y no simulada.

## Los cinco casos

| Test | Que certifica |
|---|---|
| `Una_sola_unidad_disputada_por_muchas_ordenes_produce_una_sola_reserva` | El criterio literal: 20 solicitudes simultaneas sobre 1 unidad dejan exactamente 1 reserva; las otras 19 reciben stock insuficiente. |
| `Con_mas_solicitudes_que_stock_se_reserva_exactamente_el_stock_y_nunca_mas` | No-sobreventa con volumen: 50 solicitudes sobre 10 unidades dejan exactamente 10 reservas y `disponible` en 0, nunca negativo. |
| `Cada_reserva_exitosa_emite_exactamente_un_evento` | Los reintentos no duplican eventos: 1 `InventarioReservado` por reserva, porque el evento se encola en la misma transaccion y un intento que choca se descarta entero. |
| `El_token_de_concurrencia_rechaza_la_escritura_que_llega_con_version_vieja` | Caso determinista, sin carrera: dos contextos leen la misma version, el segundo en escribir es rechazado. Es la actualizacion perdida que causaria sobreventa, evitada. |
| `La_misma_orden_pedida_en_paralelo_no_duplica_la_reserva` | Idempotencia bajo concurrencia: el indice unico `ix_reserva_orden_id` impide que una orden reintentada termine con dos reservas. |

El caso determinista y los probabilisticos se complementan: el primero demuestra
que el mecanismo funciona sin depender de que ocurra una carrera; los segundos,
que el resultado agregado es correcto cuando la carrera ocurre de verdad.

## Evidencia medida

Ejecutado el 2026-09-13 sobre PostgreSQL 16. Se instrumento temporalmente el
contador de choques para confirmar que la contencion es real y el test no pasa de
forma vacua por serializacion accidental de las tareas:

| Corrida | Choques de bloqueo optimista | Solicitudes | Unidades | Reservas resultantes |
|---|---|---|---|---|
| 1 | 248 | 50 | 10 | 10 |
| 2 | 328 | 50 | 10 | 10 |
| 3 | 246 | 50 | 10 | 10 |

**Cero sobreventas en 3 corridas**, con entre 246 y 328 choques reales por
corrida. Es decir: el camino de conflicto se ejercita cientos de veces y el
resultado final sigue siendo exacto.

Un intento que recibe un conflicto de version **reintenta** — es lo que haria
Ordenes, porque un 409 de concurrencia no significa que falte stock sino que otro
llego primero a escribir. El test afirma ademas que ningun intento agota sus
reintentos: si eso pasara seria contencion sin resolver, no una decision de
negocio.

## Para el DoD

> EAC-05: 50 solicitudes concurrentes sobre 10 unidades resultan en exactamente
> 10 reservas y `disponible = 0`, nunca negativo. Verificado con
> `ReservaConcurrenteTests` el 2026-09-13, 0 sobreventas en 3 corridas, con
> 246-328 choques de bloqueo optimista reales por corrida.
