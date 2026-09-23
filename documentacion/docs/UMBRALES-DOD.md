# Tabla de umbrales del DoD — criterios de calidad EAC-01 a EAC-05

Una sola vista de los cinco criterios de calidad del DDA contra lo **realmente
medido**, no contra lo esperado. Cada fila apunta al documento que tiene la
evidencia completa y el procedimiento para reproducirla.

Consolidada el 2026-09-22 contra `develop`.

| EAC | Criterio | Umbral | Medido | Veredicto | Evidencia | Midió |
|---|---|---|---|---|---|---|
| **EAC-01** | Disponibilidad: no se pierde ninguna orden si PayKlik falla en pico | 0 órdenes perdidas | 0 órdenes perdidas, cada intento fallido auditado | **APROBADO** | [PRUEBA-CARGA-EAC01.md](PRUEBA-CARGA-EAC01.md) | Angel |
| **EAC-02** | Rendimiento: consultar el estado de una orden | < 5 000 ms (P100) | P100 **232.09 ms** · P95 9.27 ms · promedio 3.81 ms | **APROBADO** | [PRUEBA-CARGA-EAC02.md](PRUEBA-CARGA-EAC02.md) | Jona |
| **EAC-03** | Rendimiento: recuperar la evidencia completa de una transacción | < 1 000 ms | **0.845 ms** en el motor · 4–13 ms extremo a extremo, sobre 167 895 filas | **APROBADO** | [AUDITORIA.md](AUDITORIA.md) §"Evidencia medida de EAC-03" | Julio |
| **EAC-04** | Escalabilidad: réplicas reales sin rediseño | 0 duplicados, 0 eventos perdidos | 0 duplicados en 5 tablas, 0 perdidos, carga repartida entre instancias | **APROBADO** | [ESCALABILIDAD.md](ESCALABILIDAD.md) | Julio |
| **EAC-05** | Consistencia: ninguna unidad a dos clientes | 0 sobreventas | 0 sobreventas en 3 corridas, con **246–328 choques** de bloqueo optimista reales por corrida | **APROBADO** | [CONCURRENCIA-EAC05.md](CONCURRENCIA-EAC05.md) | Jose |

**Los cinco criterios están aprobados con medición reproducible.**

## Cómo reproducir cada uno

Todas las pruebas corren contra el sistema levantado (`docker compose up -d`
desde `SGO-Logic/`), no contra el código fuente: lo que se defiende es el
comportamiento real, no un test unitario.

| EAC | Comando |
|---|---|
| EAC-01 | `docker run --rm --network sgo-logic_default -v "${PWD}/scripts/pruebas-carga:/scripts" grafana/k6 run /scripts/eac01-hu023-carga.js` y luego `bash scripts/pruebas-carga/eac01-hu023-verificar.sh` |
| EAC-02 | Ver el procedimiento en `PRUEBA-CARGA-EAC02.md` |
| EAC-03 | `dotnet test tests/SgoLogic.Auditoria.Tests --filter CargaTransaccionTests` |
| EAC-04 | `bash scripts/escalabilidad/verificar.sh` |
| EAC-05 | `dotnet test tests/SgoLogic.Inventario.IntegrationTests --filter ReservaConcurrenteTests` |

## Dos advertencias para quien reproduzca esto

**Tres suites de tests necesitan Docker.** `SgoLogic.Inventario.IntegrationTests`,
`SgoLogic.BuildingBlocks.IntegrationTests` y las de Auth levantan contenedores con
Testcontainers. Sin Docker disponible, `dotnet test` las reporta como fallidas aunque
el código esté bien. Verificadas en verde el 2026-09-22: 10 y 5 tests respectivamente.

**EAC-03 se mide con el motor ya poblado.** Los 0.845 ms son sobre 167 895 filas
reales, no sobre una tabla vacía. Medir con la base recién creada da un número
optimista que no prueba nada.

## Estado de la suite al consolidar

Build con 0 warnings (la solución trata warnings como errores) y **la suite completa
en verde**, 14 proyectos de tests.
