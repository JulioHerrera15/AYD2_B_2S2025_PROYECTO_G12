# SGO-Logic (Proyecto G12)

**Curso:** Análisis y Diseño de Sistemas 2, Sección B — USAC

SGO-Logic es el sistema de registro de órdenes y operación omnicanal que se propone para MegaStore, una cadena de tiendas por departamentos con presencia en Guatemala, El Salvador y Costa Rica. Su objetivo es que un cliente pueda iniciar, continuar y completar una compra desde tienda física, web, aplicación móvil o centro de atención con experiencia consistente, sobre una única visión de inventario, cobro, facturación fiscal y despacho — reemplazando los silos actuales entre canales y sistemas.

El repositorio se organiza en dos partes:

- [`documentacion/`](documentacion/) — Documento de Definición de Arquitectura (DDA), Architecture Decision Records (ADR), diagramas, gestión de proyecto y contratos de eventos entre contextos.
- [`SGO-Logic/`](SGO-Logic/) — prototipo del sistema en ASP.NET Core (.NET 8), construido a partir de las decisiones de arquitectura documentadas. En desarrollo activo.

---

## Índice

### Documentación de arquitectura
- [docs/DDA_SGOLogic.md](documentacion/docs/DDA_SGOLogic.md) — Documento de Definición de Arquitectura, **versión 2.0**: catálogo de evidencia, stakeholders y preocupaciones, casos de uso de negocio, características priorizadas, drivers funcionales y casos de uso expandidos (CU-01 a CU-15), drivers de calidad, drivers de restricción, restricciones de infraestructura, ADR, vistas arquitectónicas y matrices de trazabilidad de la Fase 1, más la sección 14 (Cambios de la Fase 2), que documenta cómo se materializó esa arquitectura en el MVP.
- [docs/contratos/](documentacion/docs/contratos/) — Contratos de eventos publicados entre contextos (p. ej. [ordenes-eventos-v1.md](documentacion/docs/contratos/ordenes-eventos-v1.md)).

#### ADR — Architecture Decision Records
- [ADR-001 Estilo de microservicios por contexto delimitado](documentacion/ADR/ADR-001-estilo-microservicios-por-contexto-delimitado.md)
- [ADR-002 Saga orquestada centralizada para el ciclo de vida de la orden](documentacion/ADR/ADR-002-saga-orquestada-centralizada.md)
- [ADR-003 Patrón Outbox con polling publisher](documentacion/ADR/ADR-003-patron-outbox-polling-publisher.md)
- [ADR-004 CQRS ligero con Redis como modelo de lectura](documentacion/ADR/ADR-004-cqrs-redis-modelo-lectura.md)
- [ADR-005 Bloqueo optimista con versión + Redis TTL para reserva de inventario](documentacion/ADR/ADR-005-bloqueo-optimista-reserva-inventario.md)
- [ADR-006 Almacén de auditoría append-only en base separada](documentacion/ADR/ADR-006-auditoria-append-only-base-separada.md)
- [ADR-007 Capa anticorrupción para el ERP AS/400](documentacion/ADR/ADR-007-capa-anticorrupcion-erp-as400.md)
- [ADR-008 Integración batch CSV/XML cada 30 minutos con el ERP](documentacion/ADR/ADR-008-integracion-batch-erp-30-minutos.md)
- [ADR-009 Circuit breaker y reintentos para PayKlik y LogiExpress](documentacion/ADR/ADR-009-circuit-breaker-reintentos-payklik-logiexpress.md)
- [ADR-010 Tokenización y no almacenamiento de datos de tarjeta](documentacion/ADR/ADR-010-tokenizacion-no-almacenamiento-datos-tarjeta.md)
- [ADR-011 Certificación fiscal asíncrona ante la SAT](documentacion/ADR/ADR-011-certificacion-fiscal-asincrona-sat.md)
- [ADR-012 Propagación confiable de eventos al POS](documentacion/ADR/ADR-012-propagacion-eventos-pos.md)
- [ADR-013 Docker Compose como entorno de ejecución del MVP](documentacion/ADR/ADR-013-docker-compose-entorno-ejecucion-mvp.md)
- [ADR-014 El desacople exigido por el enunciado se cubre con Outbox y disyuntor, sin mecanismo nuevo](documentacion/ADR/ADR-014-desacople-outbox-y-disyuntor.md)
- [ADR-015 Bandera de alerta fiscal con reintento manual, sin estado nuevo, cuando se agotan los reintentos de certificación](documentacion/ADR/ADR-015-bandera-alerta-fiscal-sin-estado-nuevo.md)
- [ADR-016 La saga de Órdenes avanza por eventos de resultado de Inventario, Pagos, Facturación y Despacho, sin transiciones manuales](documentacion/ADR/ADR-016-saga-dirigida-por-eventos-de-resultado.md)
- [ADR-017 Despacho consulta a Inventario por HTTP síncrono y trata la guía de LogiExpress como degradación, sin circuit breaker](documentacion/ADR/ADR-017-despacho-consulta-inventario-http-y-guia-pendiente-sin-disyuntor.md)
- [ADR-018 Contrato común de mensajería: envelope único, topología pub/sub con colas quorum y bibliotecas compartidas](documentacion/ADR/ADR-018-contrato-comun-de-mensajeria.md)
- [ADR-019 Los sistemas externos se simulan con mocks en contenedor y fallas configurables](documentacion/ADR/ADR-019-mocks-de-sistemas-externos-con-fallas-configurables.md)

#### Diagramas
- [diagramas_CDU/](documentacion/diagramas_CDU/) — Diagramas de los casos de uso expandidos CU-01 a CU-15 (formatos editable `.drawio` y de imagen).
- [img/](documentacion/img/) — Diagrama de Contexto, Diagrama de Bloques, Diagrama de Casos de Uso de Negocio de Alto Nivel, Diagrama de Despliegue Lógico, Diagrama de Despliegue Físico, Flujo Principal del Sistema, mockups de UI.

#### Gestión de Proyecto
- [Gestion_Proyecto/Plan_Tablero_Trello.md](documentacion/Gestion_Proyecto/Plan_Tablero_Trello.md)
- [Gestion_Proyecto/Backlog_Historias_Usuario.md](documentacion/Gestion_Proyecto/Backlog_Historias_Usuario.md)

### Implementación
- [SGO-Logic/](SGO-Logic/) — Prototipo ASP.NET Core (.NET 8) del sistema, alineado con el estilo de microservicios por contexto delimitado definido en ADR-001.

---

## Instalación y despliegue

Guía para levantar el sistema completo en una máquina limpia con Docker.
**El `docker-compose.yml` vive dentro de `SGO-Logic/`, no en la raíz del
repositorio** — es el primer paso y el más fácil de saltarse por accidente.

### Requisitos previos

- **Docker y Docker Compose** (el plugin `docker compose`, no el binario
  standalone `docker-compose` de la v1 — todos los comandos de este
  documento y de los scripts del repositorio usan la sintaxis nueva). Es el
  único requisito real: cada servicio .NET se compila dentro de su propio
  contenedor al correr `docker compose up --build`, así que **no hace falta
  tener el SDK de .NET 8 instalado en la máquina host**.
  - El único script del repositorio que invoca el SDK de .NET
    (`SGO-Logic/scripts/dev/dn.ps1`, pensado para correr comandos `dotnet`
    sueltos como migraciones de EF Core) tampoco lo requiere localmente:
    levanta el SDK dentro de un contenedor (`mcr.microsoft.com/dotnet/sdk:8.0`)
    por su cuenta. Es una conveniencia de desarrollo, no un requisito para
    correr el sistema. Dos cosas a tener en cuenta si lo usas: (1) requiere
    que el stack ya esté arriba — se conecta a la red
    `sgo-logic_default` del compose principal, así que falla si nada la ha
    creado todavía; (2) la cadena de conexión que usa está fija en el
    script (`Username=sgo_admin;Password=change_me` contra
    `sgo_ordenes`), no la lee de tu `.env` — si cambiaste esas credenciales,
    edita el script o pásalas por variable de entorno antes de correrlo.
- **RAM.** No se midió el consumo exacto del stack; como referencia
  razonable — son 28 contenedores, entre ellos unos 20 procesos .NET además
  de tres instancias de PostgreSQL, RabbitMQ, Redis, Prometheus y Grafana —
  reserva al menos 8 GB de RAM para el motor de Docker, con más margen si
  vas a correr además las pruebas de carga (`scripts/pruebas-carga/`,
  `scripts/escalabilidad/`) en paralelo.

### Pasos de despliegue

```bash
git clone https://github.com/JulioHerrera15/AYD2_B_2S2025_PROYECTO_G12.git
cd AYD2_B_2S2025_PROYECTO_G12/SGO-Logic   # el compose vive aqui, NO en la raiz del repositorio
cp .env.example .env
docker compose up -d --build
docker compose ps
```

La primera corrida tarda **varios minutos**, no segundos: descarga las
imágenes base (PostgreSQL, RabbitMQ, Redis, Prometheus, Grafana) y
construye alrededor de 20 imágenes .NET desde cero. Corridas posteriores
son mucho más rápidas porque Docker reutiliza las capas ya construidas y
las imágenes base ya están en caché local. `docker compose ps` debe
mostrar los 28 servicios en estado `healthy`; si alguno queda `unhealthy`
o reiniciando, revisa sus logs con `docker compose logs -f <servicio>`.

### Verificación

- **Salud agregada:** `docker compose ps` sin ningún servicio en estado
  distinto de `healthy`.
- **API Gateway:** `http://localhost:8080/health/live` debe responder 200.
  Es el punto de entrada único hacia Auth/Carrito, Consulta de Estado,
  Notificaciones, Órdenes, Pagos, Facturación, Inventario y Auditoría.
- **UI de demo:** `http://localhost:8092` (`web`) — habla con el sistema
  exclusivamente a través del API Gateway.
- **Consola de RabbitMQ:** `http://localhost:15672`, con el usuario y
  contraseña de tu `.env` (`RABBITMQ_USER` / `RABBITMQ_PASSWORD`; con
  `.env.example` sin modificar, `sgo_admin` / `change_me`).
- **Grafana:** `http://localhost:3000`, con el usuario y contraseña de tu
  `.env` (`GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD`; con
  `.env.example` sin modificar, `admin` / `change_me`). Trae dos tableros
  aprovisionados por archivo — detalle en
  [`documentacion/docs/OBSERVABILIDAD.md`](documentacion/docs/OBSERVABILIDAD.md).
- **Prometheus:** `http://localhost:9090/targets` — los targets del job
  `sgo-logic` deben aparecer `UP`.

### Registro y verificación de cuenta

`auth-api` no envía correo de verdad: el código de verificación de un
registro nuevo se escribe en los **logs de `auth-api`**, no llega a ningún
buzón. Quien siga la UI de demo (`http://localhost:8092`) para registrarse
se queda trabado en el paso de verificación si no sabe esto.

```bash
docker compose logs auth-api | grep "Codigo de verificacion"
```

El mensaje de log está marcado explícitamente como de demo
(`Application/AuthService.cs`: `"DEMO — Codigo de verificacion para
{Email}: {Codigo}"`). Los tres endpoints del flujo son
`POST /api/auth/registro`, `POST /api/auth/verificar` (con el código de
arriba) y `POST /api/auth/login`.

### Datos de ejemplo para la demo

```bash
./scripts/demo/sembrar-demo.sh
```

Siembra lo mínimo persistido que existe hoy para una demo: un usuario
(`demo@sgo.local` por defecto, o `DEMO_EMAIL`) y existencias repetibles de
tres SKUs entre centro de distribución y tienda. No hay un catálogo de
productos separado — un "producto" es solo un SKU con descripción libre que
se manda inline al crear una orden — así que el script no siembra nombres ni
precios de catálogo, solo identidad e inventario.

La primera corrida registra el usuario y se detiene pidiendo el código de
verificación (el mismo mecanismo de la sección anterior):

```bash
docker compose logs auth-api | grep -i codigo
DEMO_VERIFICATION_CODE=123456 ./scripts/demo/sembrar-demo.sh
```

`BASE_URL`, `DEMO_EMAIL`, `DEMO_PASSWORD`, `DEMO_NOMBRE` y
`DEMO_VERIFICATION_CODE` son configurables por variable de entorno — detalle
en `scripts/demo/README.md`.

Si en cambio necesitas un recorrido completo de punta a punta (orden creada,
pagada, facturada, despachada y entregada) para una demo,
`scripts/pruebas/camino-feliz.ps1` hace eso — siembra su propio SKU y corre
la saga completa hasta `ENTREGADA`.

### Servicios y puertos

Puertos de host re-derivados directamente de `docker-compose.yml` (no de
una corrida anterior, que puede haber quedado desactualizada si se agregó
o movió algún servicio). Son 28 contenedores en total.

**Servicios del sistema** (contextos de negocio, ASP.NET Core / .NET 8):

| Servicio | Puerto | Qué es |
|---|---|---|
| `api-gateway` | 8080 | Punto de entrada único (YARP), enruta hacia el resto de contextos |
| `sample-api` | 8081 | Plantilla de referencia (ver nota abajo), no es parte del dominio de negocio |
| `sample-worker` | 8082 | Plantilla de referencia (ver nota abajo), no es parte del dominio de negocio |
| `ordenes-api` | 8083 | Órdenes: creación, confirmación, transiciones de la saga, devoluciones |
| `pagos-api` | 8085 | Cobro contra la pasarela de pago |
| `facturacion-api` | 8087 | Certificación fiscal ante la SAT |
| `auth-api` | 8088 | Autenticación (JWT) y carrito de compras (Redis) |
| `notificaciones-api` | 8090 | Envío de notificaciones al cliente |
| `consulta-estado-api` | 8091 | Modelo de lectura CQRS (Redis + fallback a SQL) del estado de una orden |
| `web` | 8092 | UI de demostración; habla con el sistema vía `api-gateway` |
| `auditoria-worker` | 8093 | Bitácora inmutable de todos los eventos del sistema |
| `inventario-api` | 8094 | Reservas de inventario con TTL y bloqueo optimista (ADR-005) |
| `integracion-pos-worker` | 8095 | Sincronización con el punto de venta de tienda física, orientado a eventos |
| `integracion-erp-worker` | 8096 | Consolidación batch hacia el ERP legacy (ver nota abajo y [`documentacion/docs/ESCALABILIDAD.md`](documentacion/docs/ESCALABILIDAD.md)) |
| `despacho-api` | 8097 | Despacho de órdenes, integrado con el proveedor de logística |

**`sample-api` / `sample-worker` se quedan arriba en el arranque por
defecto**, pero no son parte del flujo de negocio: son la plantilla de
referencia de la base técnica para crear un servicio nuevo (outbox +
consumidor) y sirven además como verificación en vivo del patrón outbox
(`SgoLogic.Sample.Api` escribe, `SgoLogic.Sample.Worker` consume) sin
depender de ningún contexto de negocio para probarlo.

**Servicio nuevo desde la última revisión de este README:**
`integracion-erp-worker` (8096) no existía antes. Es un contexto híbrido:
consume eventos de la saga de Órdenes (`OrdenConfirmada`, `OrdenCancelada`,
`OrdenCertificadaFiscalmente`, `DevolucionRegistrada`) igual que el resto
del Backbone, pero además corre un `PeriodicTimer` propio que consolida un
lote cada 30 minutos (`Erp__IntervaloConsolidacionMinutos`, configurable)
y lo transfiere por HTTP a `mock-erp`, más otro temporizador de
conciliación cada 24 horas. Su llegada movió `logiexpress-mock` de 8096 a
8098 — si algo tuyo tenía ese puerto anotado, actualízalo.

**Mocks de proveedores externos** (simulan sistemas que en producción son reales):

| Servicio | Puerto | Qué es |
|---|---|---|
| `payklik-mock` | 8084 | Pasarela de pago simulada |
| `sat-mock` | 8086 | Certificación fiscal simulada (SAT) |
| `logiexpress-mock` | 8098 | Proveedor de despacho simulado |
| `mock-mensajeria` | 8089 | Envío de SMS/email simulado, con modo de falla forzada configurable |
| `mock-erp` | 4000 | ERP legacy simulado (AS/400); ahora sí consumido, por `integracion-erp-worker` |
| `mock-pos` | 4001 | Punto de venta de tienda física simulado |

**Infraestructura:**

| Servicio | Puerto | Qué es |
|---|---|---|
| `rabbitmq` | 5672 (AMQP), 15672 (consola web) | Bus de eventos |
| `redis` | 6379 | Caché de lectura (Consulta de Estado) y sesión/carrito con TTL (Auth) |
| `db-operacional` | sin publicar, solo red interna | PostgreSQL: bases de Órdenes, Inventario, Auth, Consulta de Estado, Notificaciones, Integración POS, Integración ERP, Despacho y los servicios de ejemplo |
| `db-finanzas` | sin publicar, solo red interna | PostgreSQL: bases de Pagos y Facturación |
| `db-auditoria` | sin publicar, solo red interna | PostgreSQL: base de Auditoría, separada por diseño |

**Observabilidad:**

| Servicio | Puerto | Qué es |
|---|---|---|
| `prometheus` | 9090 | Scraping de métricas de cada servicio |
| `grafana` | 3000 | Tableros sobre las métricas de Prometheus |

### Variables de entorno configurables

Todas viven en `.env` (creado a partir de `.env.example` en el paso
anterior). Esto es lo que trae hoy el archivo, agrupado por área:

**PostgreSQL / credenciales compartidas**
- `POSTGRES_USER` / `POSTGRES_PASSWORD` — usuario y contraseña compartidos
  por los tres contenedores de PostgreSQL, cada uno con su propia base y
  volumen.
- `POSTGRES_OPERACIONAL_DB`, `POSTGRES_OPERACIONAL_MULTIPLE_DATABASES` — la
  base principal y la lista de bases adicionales creadas en
  `db-operacional` (una por servicio).
- `POSTGRES_FINANZAS_DB`, `POSTGRES_FINANZAS_MULTIPLE_DATABASES` — lo mismo
  para `db-finanzas`.
- `POSTGRES_AUDITORIA_DB` — base de `db-auditoria`.
- `PG_AUDIT_USER` / `PG_AUDIT_PASS` — usuario restringido (solo
  INSERT+SELECT) con el que corre `auditoria-worker` en tiempo de
  ejecución; las migraciones corren aparte, con `POSTGRES_USER`. Detalle en
  [`documentacion/docs/AUDITORIA.md`](documentacion/docs/AUDITORIA.md).

**RabbitMQ**
- `RABBITMQ_USER` / `RABBITMQ_PASSWORD` — credenciales del broker y de su
  consola de administración (puerto 15672).

**Redis**
- `REDIS_PASSWORD` — usado tanto por el carrito/sesión de `auth-api` como
  por el caché de lectura de `consulta-estado-api`.

**Outbox** (aplica a todo contexto que publica eventos)
- `OUTBOX_POLLING_MS` — cada cuántos milisegundos el publicador revisa la
  tabla de outbox en busca de filas pendientes.
- `OUTBOX_BATCH_SIZE` — cuántas filas pendientes toma como máximo cada
  ciclo de polling.
- `OUTBOX_MAX_REINTENTOS` — reintentos de publicación antes de marcar una
  fila como fallida y enviarla a la dead letter. Detalle en
  [`documentacion/docs/TOPOLOGIA-MENSAJERIA.md`](documentacion/docs/TOPOLOGIA-MENSAJERIA.md).

**Auth**
- `JWT_SECRET` — clave de firma de los tokens JWT (mínimo 32 caracteres).
- `AUTH_JWT_EXPIRACION_MINUTOS` — vigencia del token.
- `AUTH_CARRITO_TTL_SEGUNDOS` — TTL del carrito de compras en Redis.

**Mensajería simulada**
- `MOCK_MENSAJERIA_FORZAR_FALLA` — en `true`, hace que el mock de
  mensajería falle a propósito, para demostrar el aislamiento de fallos en
  Notificaciones.

**Integración ERP** (`integracion-erp-worker`) — la dirección de `mock-erp`
va fija por red interna en `docker-compose.yml`, no por variable de entorno.
- `ERP_FORMATO` — formato del lote consolidado (`CSV`, por defecto).
- `ERP_TIMEOUT_MS` — tiempo de espera del cliente HTTP hacia `mock-erp`.
- `ERP_MAX_REINTENTOS` — reintentos del cliente HTTP por lote.
- `ERP_MAX_INTENTOS_LOTE` — intentos antes de marcar un lote `FALLIDO`.
- `ERP_INTERVALO_MINUTOS` — cada cuánto corre el temporizador de
  consolidación batch (30 minutos por defecto, ver
  [`documentacion/docs/ESCALABILIDAD.md`](documentacion/docs/ESCALABILIDAD.md)).
- `ERP_INTERVALO_CONCILIACION_HORAS` — cada cuánto corre el temporizador de
  conciliación contra `mock-erp` (24 horas por defecto).

**Grafana**
- `GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD` — credenciales de acceso
  a Grafana.

### Detener y limpiar

```bash
docker compose down       # detiene y elimina los contenedores; conserva los volumes (datos de las bases)
docker compose down -v    # ademas borra los volumes: BORRA los datos de Postgres, RabbitMQ y Grafana
```

### Documentación de apoyo para operar el sistema

Para escalar, depurar o extender el sistema ya levantado:

- [`documentacion/docs/OBSERVABILIDAD.md`](documentacion/docs/OBSERVABILIDAD.md) — qué mide Prometheus, los tableros de Grafana, cómo reproducir las demostraciones de resiliencia.
- [`documentacion/docs/TOPOLOGIA-MENSAJERIA.md`](documentacion/docs/TOPOLOGIA-MENSAJERIA.md) — exchanges, colas, routing keys, política de reintentos y dead-letter de RabbitMQ.
- [`documentacion/docs/ESCALABILIDAD.md`](documentacion/docs/ESCALABILIDAD.md) — cómo escalar servicios horizontalmente con `docker compose --scale` y la evidencia medida de esa prueba.
- [`documentacion/docs/AUDITORIA.md`](documentacion/docs/AUDITORIA.md) — diseño de la bitácora inmutable y su evidencia medida de latencia.
- [`documentacion/docs/PRUEBA-CARGA-EAC01.md`](documentacion/docs/PRUEBA-CARGA-EAC01.md) y [`documentacion/docs/PRUEBA-CARGA-EAC02.md`](documentacion/docs/PRUEBA-CARGA-EAC02.md) — pruebas de carga de resiliencia (caída de la pasarela de pago) y de rendimiento de Consulta de Estado.
- [`documentacion/docs/contratos/PLANTILLA-SERVICIO.md`](documentacion/docs/contratos/PLANTILLA-SERVICIO.md) — cómo aplicar outbox, publicar eventos y escribir un consumidor, para quien vaya a tocar un servicio existente o crear uno nuevo.
