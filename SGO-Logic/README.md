# SGO-Logic: MVP de la Fase 2

Esta carpeta contiene el código del MVP de SGO-Logic, el sistema de registro de órdenes y operación omnicanal de MegaStore. El MVP implementa la saga de la orden de punta a punta (reserva de inventario, cobro, certificación fiscal, despacho y entrega), la devolución con reingreso de stock, nota de crédito y reembolso, la integración con el ERP y el POS, las notificaciones al cliente, la consulta de estado y la auditoría append-only. Todo corre en contenedores con Docker Compose (ADR-013), y los sistemas externos se reemplazan por mocks con fallas configurables (ADR-019).

## Relación con la Fase 1

La arquitectura se definió en la Fase 1 y está en `../documentacion/`: el Documento de Definición de Arquitectura (`../documentacion/docs/DDA_SGOLogic.md`, secciones 1 a 13) y los ADR-001 a ADR-012. Esta carpeta es su materialización: la Fase 2 no rediseña esa arquitectura. Las decisiones tomadas al construir el MVP se registran como ADR-013 en adelante (`../documentacion/ADR/`) y se resumen en la sección 14 del DDA, junto con los límites conocidos del MVP (sección 14.9). Los contratos de eventos entre contextos están en `../documentacion/docs/contratos/`.

Clone el repositorio completo, no solo esta carpeta: `docker-compose.yml` construye `mock-erp` y `mock-pos` desde `../Mocks/`.

## Contextos y servicios

Los 12 contextos delimitados de la sección 6.1 del DDA corresponden a estos servicios. Los puertos son los que `docker-compose.yml` publica en el host; dentro de la red de Docker, los servicios .NET escuchan en el 8080.

| Contexto | Servicio | Puerto en el host | Responsabilidad |
|---|---|---|---|
| Borde / API Gateway | `api-gateway` | 8080 | Punto único de entrada. Enruta por prefijo con YARP y, con `GATEWAY_EXIGIR_JWT=true`, valida el JWT de Auth (ADR-020). |
| Auth | `auth-api` | 8088 | Registro, verificación e inicio de sesión; emisión del JWT; carrito unificado en Redis. |
| Órdenes | `ordenes-api` | 8083 | Ciclo de vida de la orden y orquestación de la saga (ADR-016); devoluciones. |
| Inventario | `inventario-api` | 8094 | Existencias por ubicación, reservas con TTL, liberación por compensación y reingreso por devolución (ADR-022). |
| Pagos | `pagos-api` | 8085 | Cobro contra PayKlik con disyuntor y reintento, reembolsos. |
| Crédito | `credito-api` | 8099 | Perfil crediticio precalculado desde el historial del ERP y oferta "Compra ahora, paga en 30 días". |
| Facturación | `facturacion-api` | 8087 | Documento fiscal y certificación asíncrona ante la SAT, nota de crédito. |
| Auditoría | `auditoria-worker` | 8093 | Bitácora append-only de todos los eventos del bus, en su propia instancia de base de datos. |
| Consulta de Estado | `consulta-estado-api` | 8091 | Modelo de lectura de las órdenes en Redis con respaldo en PostgreSQL; incidencias. |
| Despacho | `despacho-api` | 8097 | Origen del despacho, guía con LogiExpress y estados de entrega. |
| Integración ERP | `integracion-erp-worker` | 8096 | Lotes de ventas hacia el ERP-Core (AS/400) cada `ERP_INTERVALO_MINUTOS` y conciliación. |
| Integración POS | `integracion-pos-worker` | 8095 | Propagación confiable de eventos de venta a las 32 instancias de POS. |
| Notificaciones | `notificaciones-api` | 8090 | Avisos al cliente por el proveedor de mensajería, aislados del flujo principal. |

Otros contenedores del stack:

| Servicio | Puerto en el host | Función |
|---|---|---|
| `web` | 8092 | Aplicación web ASP.NET Core MVC que consume el gateway. |
| `payklik-mock` | 8084 | Mock de PayKlik (pasarela de pago y tokenización). |
| `sat-mock` | 8086 | Mock de la SAT (certificación fiscal). |
| `logiexpress-mock` | 8098 | Mock de LogiExpress (guías y rastreo). |
| `mock-mensajeria` | 8089 | Mock del proveedor de SMS, correo y push. |
| `mock-erp` | 4000 | Mock del ERP-Core sobre AS/400. |
| `mock-pos` | 4001 | Mock de las 32 instancias de POS (`T01` a `T32`). |
| `sample-api`, `sample-worker` | 8081, 8082 | Plantillas para crear servicios nuevos. |
| `rabbitmq` | 5672, 15672 | Broker de eventos; la consola de administración está en el 15672. |
| `redis` | 6379 | Carrito y caché de Consulta de Estado. |
| `db-operacional`, `db-finanzas`, `db-auditoria` | sin publicar | Tres instancias de PostgreSQL con una base por contexto. |
| `prometheus` | 9090 | Recolección de métricas. |
| `grafana` | 3000 | Tableros de observabilidad. |

## Requisitos previos

Versiones fijadas en el repositorio:

| Componente | Versión | Dónde se fija |
|---|---|---|
| .NET | 8.0 (`net8.0`) | `TargetFramework` de los proyectos; imágenes `mcr.microsoft.com/dotnet/sdk:8.0-alpine` y `aspnet:8.0-alpine` de los Dockerfile |
| dotnet-ef | 8.0.11 | `.config/dotnet-tools.json` |
| Node.js (solo dentro de `mock-erp` y `mock-pos`) | 20 | `node:20-alpine` en `../Mocks/*/Dockerfile` |
| PostgreSQL | 16 | `postgres:16-alpine` |
| RabbitMQ | 3.13 | `rabbitmq:3.13-management-alpine` |
| Redis | 7 | `redis:7-alpine` |
| Prometheus | 2.55.1 | `prom/prometheus:v2.55.1` |
| Grafana | 11.3.1 | `grafana/grafana:11.3.1` |
| Testcontainers | 4.15.0 | `Testcontainers.PostgreSql` y `Testcontainers.RabbitMq` en las suites de integración |
| xUnit | 2.5.3 | proyectos de `tests/` |

En la máquina que levanta el sistema se necesita:

- Docker con el plugin Docker Compose v2 (comando `docker compose`). Versión de Docker usada por el equipo: PENDIENTE DE EVIDENCIA, porque el repositorio no la fija.
- Bash y curl para `scripts/demo/sembrar-demo.sh` (en Windows, Git Bash o WSL).
- PowerShell para `scripts/dev/dn.ps1` y `scripts/pruebas/camino-feliz.ps1`; este último es compatible con Windows PowerShell 5.1.
- El SDK de .NET 8 instalado en el host solo si se van a correr las suites de Testcontainers (ver "Build y pruebas"). Para levantar el sistema no hace falta.
- k6 no se instala: los scripts de carga lo corren con la imagen `grafana/k6`.

## Instalación y despliegue

Todos los comandos se ejecutan desde esta carpeta (`SGO-Logic/`).

1. Cree el archivo `.env` a partir del ejemplo. `.env` no se versiona.

```bash
cp .env.example .env
```

En PowerShell:

```powershell
Copy-Item .env.example .env
```

2. Revise las variables de `.env`. Las más relevantes para la demostración:

| Variable | Valor en `.env.example` | Efecto |
|---|---|---|
| `GATEWAY_EXIGIR_JWT` | `false` | Llega al gateway como `Gateway__ExigirJwt`. Con `false`, el gateway enruta sin validar el token; si la variable no está definida, el compose también usa `false`. Con `true`, las rutas de órdenes, pagos, facturación, inventario, despacho, auditoría y ERP exigen el JWT de `POST /api/auth/login` y responden 401 sin él. |
| `JWT_SECRET` | `change_me_at_least_32_chars_long_secret` | Secreto con el que Auth firma el JWT y con el que el gateway lo valida (`Auth__JwtSecret` en ambos servicios). |
| `MOCK_MENSAJERIA_FORZAR_FALLA` | `false` | Con `true`, `mock-mensajeria` responde 500 a todo envío, para demostrar que la falla de notificaciones no detiene la saga. |
| `POSTGRES_USER`, `POSTGRES_PASSWORD` | `sgo_admin`, `change_me` | Usuario de las tres instancias de PostgreSQL. `scripts/dev/dn.ps1` asume `change_me`. |
| `PG_AUDIT_USER`, `PG_AUDIT_PASS` | `auditoria_app`, `change_me` | Usuario restringido (solo INSERT y SELECT) de la base de auditoría. |
| `RABBITMQ_USER`, `RABBITMQ_PASSWORD` | `sgo_admin`, `change_me` | Credenciales del broker y de su consola en el puerto 15672. |
| `REDIS_PASSWORD` | `change_me` | Contraseña de Redis. |
| `OUTBOX_POLLING_MS`, `OUTBOX_BATCH_SIZE`, `OUTBOX_MAX_REINTENTOS` | `500`, `200`, `5` | Intervalo, lote y reintentos del publicador Outbox de cada servicio. |
| `AUTH_JWT_EXPIRACION_MINUTOS` | `60` | Vigencia del JWT. |
| `ERP_INTERVALO_MINUTOS` | `30` | Periodo de los lotes hacia el ERP. |
| `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD` | `admin`, `change_me` | Credenciales de Grafana. |

Los scripts de demostración (`sembrar-demo.sh`) y de carga llaman al gateway sin token, así que requieren `GATEWAY_EXIGIR_JWT=false`. `camino-feliz.ps1` llama directamente a los puertos de cada servicio y no depende de la bandera.

3. Construya y levante el stack. La primera construcción compila las 19 imágenes .NET y las 2 de Node.

```bash
docker compose up -d --build
```

4. Verifique que los contenedores estén `healthy`.

```bash
docker compose ps
```

```bash
curl http://localhost:8080/health/live
```

Cada servicio .NET expone `/health/live` y `/health/ready`. Las migraciones de base de datos se aplican solas al arrancar cada servicio.

5. Para encender la autenticación en el borde después de levantar el stack, cambie `GATEWAY_EXIGIR_JWT=true` en `.env` y recree el gateway.

```bash
docker compose up -d api-gateway
```

Para detener el stack sin borrar datos, o borrándolos:

```bash
docker compose down
```

```bash
docker compose down -v
```

## Datos de demo

`scripts/demo/sembrar-demo.sh` registra el usuario `demo@sgo.local` (contraseña `Demo1234!`), lo verifica, inicia sesión y carga existencias de `SKU-DEMO-001` a `SKU-DEMO-003` en `CD-CENTRAL` y `TIENDA-001`, todo a través del gateway en `http://localhost:8080`.

1. Primera ejecución: registra el usuario y termina pidiendo el código de verificación.

```bash
./scripts/demo/sembrar-demo.sh
```

2. Obtenga el código. Auth no envía correo en el MVP: escribe el código de 6 dígitos en su log con el texto `DEMO — Codigo de verificacion para <correo>: <codigo>`. El código vence a los 15 minutos.

```bash
docker compose logs auth-api | grep -i codigo
```

En PowerShell:

```powershell
docker compose logs auth-api | Select-String -Pattern codigo
```

3. Segunda ejecución, con el código:

```bash
DEMO_VERIFICATION_CODE=123456 ./scripts/demo/sembrar-demo.sh
```

Reemplace `123456` por el código del log. Las existencias se cargan con `PUT /api/inventario/existencias`, que fija la cantidad física, así que el script se puede repetir sin acumular stock. Se pueden cambiar `BASE_URL`, `DEMO_EMAIL`, `DEMO_PASSWORD`, `DEMO_NOMBRE` y `DEMO_VERIFICATION_CODE` por variables de entorno.

Auth solo genera el código al registrar un correo nuevo: un correo ya registrado no produce otro. Si el código venció o ya no está en el log, use otro correo con `DEMO_EMAIL`.

## Build y pruebas

Hay dos formas de ejecutar `dotnet`, y cuál usar depende de la suite.

### Con `scripts/dev/dn.ps1`

`dn.ps1` corre `dotnet` dentro de un contenedor `mcr.microsoft.com/dotnet/sdk:8.0`, conectado a la red `sgo-logic_default` del stack, así que no hace falta el SDK en el host, pero sí que Docker esté encendido y el stack levantado.

```powershell
.\scripts\dev\dn.ps1 build SgoLogic.sln
```

```powershell
.\scripts\dev\dn.ps1 test tests/SgoLogic.Ordenes.Tests
```

Suites que corren con `dn.ps1`: `SgoLogic.Auth.Tests`, `SgoLogic.BuildingBlocks.Tests`, `SgoLogic.ConsultaEstado.Tests`, `SgoLogic.Despacho.Tests`, `SgoLogic.Facturacion.Tests`, `SgoLogic.IntegracionErp.Tests`, `SgoLogic.IntegracionPos.Tests`, `SgoLogic.Inventario.Tests`, `SgoLogic.Notificaciones.Tests`, `SgoLogic.Ordenes.Tests` y `SgoLogic.Pagos.Tests`. Para correrlas todas en una sola línea:

```powershell
Get-ChildItem tests -Directory | Where-Object { $_.Name -notin 'SgoLogic.Auditoria.Tests','SgoLogic.BuildingBlocks.IntegrationTests','SgoLogic.Inventario.IntegrationTests' } | ForEach-Object { .\scripts\dev\dn.ps1 test "tests/$($_.Name)" }
```

`dn.ps1` también sirve para las migraciones de EF Core:

```powershell
.\scripts\dev\dn.ps1 ef migrations add NombreMigracion --project src/Services/SgoLogic.Ordenes.Api --output-dir Infrastructure/Persistencia/Migraciones
```

### Suites de Testcontainers, en forma nativa

Tres suites levantan sus propios contenedores de PostgreSQL o RabbitMQ con Testcontainers y necesitan el socket de Docker, que el contenedor de `dn.ps1` no monta. Dentro de `dn.ps1` fallan al no encontrar el daemon, sin que sea un error del código. Se corren con el SDK de .NET 8 del host y Docker encendido:

```bash
dotnet test tests/SgoLogic.BuildingBlocks.IntegrationTests
```

```bash
dotnet test tests/SgoLogic.Inventario.IntegrationTests
```

```bash
dotnet test tests/SgoLogic.Auditoria.Tests
```

`SgoLogic.Inventario.IntegrationTests` contiene la prueba de reservas concurrentes de EAC-05, y `SgoLogic.Auditoria.Tests` la de carga y recuperación de EAC-03.

## Mocks de sistemas externos

Los mocks guardan su configuración en memoria: al reiniciar el contenedor vuelven a su modo por defecto. Los comandos de esta sección y de las siguientes usan curl de Bash; en Windows PowerShell 5.1 use `curl.exe`, porque `curl` es un alias de `Invoke-WebRequest`.

| Mock | Puerto | Modos | Cambio de modo | Consulta y administración |
|---|---|---|---|---|
| `payklik-mock` | 8084 | `NORMAL` (aprueba), `LENTO` (espera `latenciaMs`), `CAIDO` (503, no procesa ni consume el token), `RECHAZO` (200 con estado `RECHAZADO`, código 51), `INTERMITENTE` (en cada petición falla como `CAIDO` con probabilidad `probabilidadFallo`). Por defecto: `NORMAL`, `latenciaMs` 2000, `probabilidadFallo` 0.5. | `PUT /admin/modo` con `{"modo", "latenciaMs", "probabilidadFallo"}`; los dos últimos son opcionales. | `GET /admin/modo`, `GET /admin/estado`, `GET /admin/cargos`, `GET /admin/reembolsos`, `GET /admin/tokens` |
| `sat-mock` | 8086 | `NORMAL` (certifica), `LENTO`, `CAIDO` (503), `RECHAZO` (no certifica). Además, con `probabilidadFallo` mayor que 0 cualquier modo responde 503 al azar. Por defecto: `NORMAL`, `latenciaMs` 2000, `probabilidadFallo` 0. | `PUT /admin/modo` con `{"modo", "latenciaMs", "probabilidadFallo"}` | `GET /admin/modo`, `GET /admin/estado`, `GET /admin/certificaciones` |
| `logiexpress-mock` | 8098 | `NORMAL` (emite la guía, salvo la solicitud número `unoDeCada`, que agota el tiempo), `LENTO`, `CAIDO` (ninguna solicitud responde a tiempo). Por defecto: `NORMAL`, `latenciaMs` 5000, `unoDeCada` 50. | `PUT /admin/modo` con `{"modo", "latenciaMs", "unoDeCada"}` | `GET /admin/modo`, `GET /admin/guias`, `GET /admin/intentos`; `POST /admin/guias/{guiaId}/estado` cambia el estado de rastreo de una guía |
| `mock-mensajeria` | 8089 | Envío normal o falla forzada (500 en SMS, correo y push). Por defecto, el valor de `MOCK_MENSAJERIA_FORZAR_FALLA`. | `POST /api/mensajes/config/falla?activa=true` o `?activa=false`, sin reiniciar el contenedor. | Los envíos quedan en el log del contenedor. |
| `mock-erp` | 4000 | En línea, o caído con `tipo` `timeout` (504 a los 8 segundos), `error500` o `no_disponible` (503), por tiempo indefinido o durante `duracionMs`. | `POST /erp/admin/caida` con `{"activar", "tipo", "duracionMs"}`; `POST /erp/admin/reset` restaura el estado inicial. | `GET /erp/admin/estado`, `GET /erp/lotes` |
| `mock-pos` | 4001 | Cada tienda `T01` a `T32` en línea o desconectada con `tipo` `timeout`, `error500` o `no_disponible`, por tiempo indefinido o durante `duracionMs`. | `POST /pos/admin/{tiendaId}/caida` con `{"activar", "tipo", "duracionMs"}`; `POST /pos/admin/caida-masiva` con `{"tiendas", "activar", "tipo", "duracionMs"}`; `POST /pos/admin/reset`. | `GET /pos`, `GET /pos/{tiendaId}/estado`, `GET /pos/{tiendaId}/eventos` |

## Camino feliz de punta a punta

`scripts/pruebas/camino-feliz.ps1` recorre la saga completa sin transiciones manuales: siembra existencias de `SKU-CAMINO-FELIZ` en `CD-01`, tokeniza una tarjeta en PayKlik, crea una orden a domicilio, la confirma y espera cada estado hasta `ENTREGADA`. Los pasos `RESERVA_SOLICITADA` a `DESPACHO_SOLICITADO` los avanza la saga sola; en `EN_PREPARACION`, `EN_TRANSITO` y `ENTREGADA` el script hace de transportista, porque el mock de LogiExpress no avisa a Despacho por sí mismo. Llama a los puertos de cada servicio sin pasar por el gateway.

Con los mocks en modo `NORMAL`:

```powershell
.\scripts\pruebas\camino-feliz.ps1
```

```powershell
.\scripts\pruebas\camino-feliz.ps1 -TimeoutSeg 120
```

El script imprime cada transición con su origen y termina con `RESULTADO=APROBADO` si la orden llegó a `ENTREGADA`. La evidencia versionada de la última corrida documentada está en `scripts/pruebas/evidencia/camino-feliz-2026-09-22.txt`.

Para seguir una orden por su cuenta, reemplace `ORD-...` por el identificador que imprime el script:

```bash
curl http://localhost:8083/api/ordenes/ORD-2026-000001
```

```bash
curl http://localhost:8091/api/estado/ordenes/ORD-2026-000001
```

```bash
curl http://localhost:8094/api/inventario/reservas/ORD-2026-000001
```

```bash
curl http://localhost:8097/api/despacho/ORD-2026-000001
```

```bash
curl http://localhost:8087/api/facturacion/documentos/ORD-2026-000001
```

```bash
curl http://localhost:8093/auditoria/agregados/ORD-2026-000001
```

## Escenarios de falla

Cada escenario se fuerza cambiando el modo del mock antes de correr `camino-feliz.ps1` o de crear una orden. En estos escenarios el script no llega a `ENTREGADA`, así que no termina en `RESULTADO=APROBADO`; el resultado se observa consultando la orden con los comandos de la sección anterior. Al terminar, devuelva cada mock a su modo normal.

Pago rechazado o proveedor de pagos caído. La orden pasa a `PAGO_PENDIENTE`, Órdenes publica `LiberacionReservaSolicitada` y, desde ADR-022, Inventario libera la reserva en el momento: la reserva de la orden queda `LIBERADA` y el disponible se recupera sin esperar el TTL.

```bash
curl -X PUT http://localhost:8084/admin/modo -H "Content-Type: application/json" -d '{"modo":"RECHAZO"}'
```

```bash
curl -X PUT http://localhost:8084/admin/modo -H "Content-Type: application/json" -d '{"modo":"CAIDO"}'
```

```bash
curl -X PUT http://localhost:8084/admin/modo -H "Content-Type: application/json" -d '{"modo":"INTERMITENTE","probabilidadFallo":0.3}'
```

```bash
curl -X PUT http://localhost:8084/admin/modo -H "Content-Type: application/json" -d '{"modo":"NORMAL"}'
```

SAT caída. Facturación reintenta con backoff; al agotar los reintentos la orden queda en `CONFIRMADA_PENDIENTE_FISCAL` con la bandera de alerta fiscal (ADR-015), que se consulta y se reintenta a mano:

```bash
curl -X PUT http://localhost:8086/admin/modo -H "Content-Type: application/json" -d '{"modo":"CAIDO"}'
```

```bash
curl http://localhost:8083/api/ordenes/alertas-fiscales
```

```bash
curl -X PUT http://localhost:8086/admin/modo -H "Content-Type: application/json" -d '{"modo":"NORMAL"}'
```

```bash
curl -X POST http://localhost:8083/api/ordenes/ORD-2026-000001/reintentar-certificacion
```

LogiExpress sin respuesta. El despacho pasa a `GUIA_PENDIENTE` y el pedido sale igual (ADR-017):

```bash
curl -X PUT http://localhost:8098/admin/modo -H "Content-Type: application/json" -d '{"modo":"CAIDO"}'
```

```bash
curl -X PUT http://localhost:8098/admin/modo -H "Content-Type: application/json" -d '{"modo":"NORMAL"}'
```

Proveedor de mensajería caído. La orden avanza igual; el aviso se pierde y queda una advertencia en el log de `notificaciones-api` (límite del MVP, sección 14.9 del DDA):

```bash
curl -X POST "http://localhost:8089/api/mensajes/config/falla?activa=true"
```

```bash
docker compose logs notificaciones-api
```

```bash
curl -X POST "http://localhost:8089/api/mensajes/config/falla?activa=false"
```

ERP caído. `integracion-erp-worker` reintenta cada envío hasta `ERP_MAX_REINTENTOS` veces y deja el lote en `PENDIENTE_REINTENTO` para los ciclos siguientes; al llegar a `ERP_MAX_INTENTOS_LOTE` intentos lo marca `FALLIDO`, y se reintenta a mano con `POST /api/erp/lotes/{loteId}/reintentar` en el puerto 8096:

```bash
curl -X POST http://localhost:4000/erp/admin/caida -H "Content-Type: application/json" -d '{"activar":true,"tipo":"no_disponible","duracionMs":120000}'
```

```bash
curl http://localhost:4000/erp/admin/estado
```

```bash
curl -X POST http://localhost:4000/erp/admin/reset
```

Tiendas POS desconectadas. `integracion-pos-worker` reintenta localmente la entrega a esas tiendas y, si no la logra, devuelve el evento a RabbitMQ para que se reentregue (ADR-012):

```bash
curl -X POST http://localhost:4001/pos/admin/caida-masiva -H "Content-Type: application/json" -d '{"tiendas":["T05","T06"],"activar":true,"tipo":"timeout"}'
```

```bash
curl http://localhost:4001/pos
```

```bash
curl -X POST http://localhost:4001/pos/admin/reset
```

Las pruebas de carga con fallas intermitentes de PayKlik (EAC-01) y de consulta bajo carga (EAC-02) están en `scripts/pruebas-carga/`; cada script documenta en su encabezado cómo correrlo con la imagen `grafana/k6`.

## Observabilidad

| Herramienta | URL | Acceso |
|---|---|---|
| Prometheus | http://localhost:9090 | Sin credenciales. Recoge `/metrics` de los servicios cada 5 segundos según `observabilidad/prometheus.yml`. |
| Grafana | http://localhost:3000 | `GRAFANA_ADMIN_USER` y `GRAFANA_ADMIN_PASSWORD` de `.env`. |
| Consola de RabbitMQ | http://localhost:15672 | `RABBITMQ_USER` y `RABBITMQ_PASSWORD` de `.env`. |

Grafana se aprovisiona desde `observabilidad/grafana/provisioning/` con Prometheus como fuente de datos y dos tableros: "SGO-Logic - Salud del sistema" y "SGO-Logic - Mensajeria y resiliencia". Las métricas propias son `outbox_pendientes`, `outbox_publicados_total`, `outbox_fallidos_total`, `outbox_latencia_publicacion_segundos`, `mensajes_consumidos_total`, `mensajes_duplicados_total` y `alertas_fiscales_levantadas_total`, además de las métricas HTTP estándar. Prometheus no raspa los mocks de sistemas externos, salvo `mock-mensajeria`.
