# Consultas de inventario — HU-016 y HU-017

Inventario expone seis endpoints de lectura. Todos responden desde PostgreSQL en
el momento de la peticion, sin cache intermedia, y todos cuelgan de
`/api/inventario` en el servicio `inventario-api` (puerto `8094` en el compose
local).

## Que endpoint resuelve que evidencia

| Endpoint | HU | Evidencia | Que necesidad cubre |
|---|---|---|---|
| `GET /existencias?ubicacionId=&tipoUbicacion=&pagina=&tamano=` | HU-016 | EV-05, EV-07 | La vista unica de existencias entre las 32 tiendas y los 2 centros de distribucion, sin silos por aplicacion. |
| `GET /ubicaciones` | HU-016 | EV-05 | Que ubicaciones existen y cuanto guarda cada una (SKU distintos, fisica, reservada, disponible). |
| `GET /ubicaciones/{ubicacionId}/existencias` | HU-016, HU-017 | EV-08, EV-32 | Que hay disponible en esta tienda, para decidir desde donde despachar sin llamar por telefono. |
| `GET /ubicaciones/{ubicacionId}/reservas?estado=` | HU-016 | EV-05 | Que hay comprometido en esa ubicacion y no puede prometerse a otro cliente. |
| `GET /existencias/{sku}/movimientos?ubicacionId=&desde=&hasta=` | HU-016 | EV-05 | Historial de entradas y salidas fisicas (el ledger de auditoria). |
| `GET /existencias/{sku}` | HU-001, HU-017 | EV-08, EV-32 | Disponibilidad de un SKU en todas sus ubicaciones. |

## Por que la disponibilidad es de verdad en tiempo real

`disponible` se calcula como `cantidadFisica - cantidadReservada`. No es un
contador que alguien sincroniza despues: `Reservar()` y `LiberarReserva()`
mueven `cantidadReservada` **en la misma transaccion** que crea o expira la
reserva. Cuando la transaccion confirma, cualquier lectura posterior ya ve el
valor nuevo; mientras no confirma, ninguna lo ve. No hay ventana intermedia en
la que la consulta pueda prometer stock que en realidad ya esta comprometido.

Por eso HU-017 no necesito logica de negocio nueva: la propiedad la garantiza el
diseño de HU-001 y HU-002. Lo que HU-017 aporta es la **prueba** de que se
cumple.

## Que se prueba y donde

`tests/SgoLogic.Inventario.IntegrationTests` levanta un PostgreSQL real con
Testcontainers y corre los servicios de aplicacion de verdad, sin dobles.
**Requiere Docker disponible**, a diferencia de los tests unitarios de
`SgoLogic.Inventario.Tests`, que no necesitan infraestructura.

```bash
dotnet test tests/SgoLogic.Inventario.IntegrationTests
```

Cada operacion corre en su propio alcance de inyeccion, con un `DbContext`
nuevo. Es deliberado: con un contexto compartido, el change tracker de EF Core
respondería las lecturas de memoria y el test pasaria sin haber consultado la
base.

| Test | Que certifica |
|---|---|
| `Reservar_baja_el_disponible_en_la_lectura_inmediata_siguiente` | El criterio de aceptacion de HU-017: reservar 4 de 10 deja `disponible` en 6 en la lectura inmediata, con `cantidadFisica` intacta y `version` incrementada. |
| `La_consulta_por_ubicacion_refleja_el_disponible_post_reserva` | Lo mismo desde el angulo por ubicacion (EV-08). |
| `El_resumen_de_ubicaciones_se_traduce_a_SQL_y_descuenta_lo_reservado` | Regresion: el `GROUP BY` de `/ubicaciones` se traduce a SQL. |
| `Al_expirar_la_reserva_el_disponible_vuelve_a_subir` | El otro extremo: al vencer el TTL (HU-002), las unidades vuelven al disponible. |
| `Sin_disponible_la_consulta_nunca_reporta_negativo` | Agotar el stock deja `disponible` en 0 y rechaza la reserva sobrante, nunca lo empuja bajo cero. |

## Una nota sobre los dobles en memoria

El test de regresion de `/ubicaciones` existe por algo concreto. La primera
version de `ResumirUbicacionesAsync` proyectaba el `GroupBy` directo al
constructor de `UbicacionResumenDto`; EF Core no sabe traducir eso y el endpoint
respondia 500. Los tests unitarios pasaban en verde porque el doble en memoria
ejecuta LINQ-to-Objects, que traduce cualquier cosa.

La leccion vale para cualquier consulta nueva: **un doble en memoria no puede
certificar que una consulta LINQ sea traducible a SQL.** Solo Postgres lo dice.
