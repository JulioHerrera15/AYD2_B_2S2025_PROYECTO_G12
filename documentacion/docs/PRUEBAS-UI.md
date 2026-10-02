# Pruebas manuales por la interfaz web + carga con Locust

Esta guía es para sentarse frente al navegador como lo haría un cliente real de
MegaStore, romper cosas en la infraestructura de Docker Compose y anotar qué
pasa en pantalla. No usa `curl` ni pega directo a la API salvo que se indique
explícitamente (por ejemplo, el endpoint de administración de un mock, que no
tiene pantalla propia).

Todos los comandos son para macOS y se ejecutan desde la raíz de `SGO-Logic/`
con el stack completo arriba:

```bash
cd SGO-Logic
docker compose up -d
docker compose ps        # confirmar que todo está "healthy" antes de empezar
```

> Nota de ubicación: este documento vive en `documentacion/docs/`, que es una
> carpeta **hermana** de `SGO-Logic/` (no está dentro del repo de código). Si
> estás parado en `SGO-Logic/`, la ruta relativa es `../documentacion/docs/PRUEBAS-UI.md`.

## 0. Antes de empezar

### URLs

| Qué | URL | Puerto |
| --- | --- | --- |
| Tienda web | http://localhost:8092 | 8092 |
| API Gateway | http://localhost:8080 | 8080 |
| PayKlik (mock) | http://localhost:8084 | 8084 |
| SAT (mock) | http://localhost:8086 | 8086 |
| LogiExpress (mock) | http://localhost:8098 | 8098 |
| Mock de mensajería | http://localhost:8089 | 8089 |
| Órdenes API | http://localhost:8083 | 8083 |
| Inventario API | http://localhost:8094 | 8094 |
| Auth API | http://localhost:8088 | 8088 |
| Pagos API | http://localhost:8085 | 8085 |
| Facturación API | http://localhost:8087 | 8087 |
| Despacho API | http://localhost:8097 | 8097 |
| Consulta de Estado API | http://localhost:8091 | 8091 |
| RabbitMQ (AMQP) | — | 5672 |
| RabbitMQ (management UI) | http://localhost:15672 | 15672 |
| Redis | — | 6379 |
| Prometheus | http://localhost:9090 | 9090 |
| Grafana | http://localhost:3000 | 3000 |

Usuario/contraseña de RabbitMQ y Grafana están en tu `.env` local
(`RABBITMQ_USER`/`RABBITMQ_PASSWORD`, `GRAFANA_ADMIN_USER`/`GRAFANA_ADMIN_PASSWORD`);
no se imprimen acá porque no son los mismos en todos los entornos. `.env.example`
tiene los nombres de las variables y valores de ejemplo.

### Cuentas de prueba

Sembradas por `scripts/seed/sql/01-auth.sql` (contraseña `1234` para todas):

| Rol | Email |
| --- | --- |
| ADMIN | `admin@sgo.local`, `admin2@sgo.local` |
| OPERADOR | `operador@sgo.local` |
| CLIENTE | `cliente1@sgo.local`, `cliente2@sgo.local`, `cliente3@sgo.local` |

**Ojo:** los 3 clientes sembrados comparten el mismo carrito solo si dos
pestañas usan la misma cuenta a la vez (el carrito vive en Redis bajo la
clave `cart:{sub del JWT}`, por cliente). Para las pruebas manuales de este
documento (una persona, una pantalla) esto no es un problema; para Locust sí
lo es, y se explica en el Bloque 2.

### Catálogo sembrado

8 productos fijos en el código de la Web (`PROD-001`..`PROD-008`, no vienen de
Inventario — solo el stock se consulta en vivo). Dos ya están sembrados con
stock bajo a propósito, sin que haga falta tocar nada:

| SKU | Producto | Ubicación baja | Unidades |
| --- | --- | --- | --- |
| `PROD-001` | Lumina Smart Refrigerator Pro | Tienda Portales (recogida en tienda) | **3** |
| `PROD-005` | Laptop Gamer Ultra 16" Ryzen 9 | Tienda Pradera (recogida en tienda) | **0** |

El resto de SKUs y ubicaciones tienen 50-200 unidades. Esto se usa en la
prueba de sobreventa (sección 5.6) y **no debe tocarse con Locust** (sección 5).

---

## 1. Cómo romper cada cosa (y cómo distinguir las dos formas)

Hay dos formas de romper un proveedor externo, y prueban cosas distintas:

- **Detener el contenedor** (`docker compose stop <servicio>`): el servicio
  desaparece de la red `sgo-logic_default`. Quien lo llame ve un fallo de
  conexión (connection refused / timeout de TCP), no una respuesta.
- **Modo de falla del mock** (`PUT /admin/modo` en el mock, con `curl`, porque
  no tiene pantalla propia): el mock sigue arriba y responde, pero con error,
  lentitud o rechazo. Esto prueba el manejo de una respuesta explícita del
  proveedor, no la ausencia de red.

Los tres mocks de proveedor (PayKlik, SAT, LogiExpress) **no soportan los
mismos modos** — verificado en el código, no asumido:

| Mock | Puerto admin | Modos válidos | Parámetro extra |
| --- | --- | --- | --- |
| PayKlik | 8084 | `NORMAL`, `LENTO`, `CAIDO`, `RECHAZO`, `INTERMITENTE` | `probabilidadFallo` (con `INTERMITENTE`) |
| SAT | 8086 | `NORMAL`, `LENTO`, `CAIDO`, `RECHAZO` (sin `INTERMITENTE`) | `probabilidadFallo` (se evalúa *antes* del modo, independiente de él) |
| LogiExpress | 8098 | `NORMAL`, `LENTO`, `CAIDO` (sin `RECHAZO` ni `INTERMITENTE`) | `unoDeCada` (1 de cada N envíos cuelga 5 minutos en vez de fallar rápido — a propósito, para que Despacho lo trate como guía pendiente y no como error) |

El **mock de mensajería** es distinto a los otros tres: no tiene un enum de
modos. Se activa/desactiva con:

```bash
curl -X POST "http://localhost:8089/api/mensajes/config/falla?activa=true"
# ... prueba ...
curl -X POST "http://localhost:8089/api/mensajes/config/falla?activa=false"
```

Formato genérico para PayKlik/SAT/LogiExpress:

```bash
curl -X PUT http://localhost:<puerto-admin>/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo": "CAIDO"}'

# Volver a la normalidad SIEMPRE antes de la siguiente prueba:
curl -X PUT http://localhost:<puerto-admin>/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo": "NORMAL"}'
```

Las pruebas están ordenadas de menos a más disruptiva: se pueden correr en
secuencia sin reconstruir nada entre una y otra (`docker compose stop`/`start`
no borra volúmenes ni datos).

---

## 2. Bloque 1 — Resiliencia por la interfaz

### 2.1 PayKlik en modo LENTO

- **Qué se rompe:**
  ```bash
  curl -X PUT http://localhost:8084/admin/modo -H "Content-Type: application/json" -d '{"modo":"LENTO","latenciaMs":5000}'
  ```
- **Qué hacer en la página:** Iniciá sesión con `cliente1@sgo.local` / `1234`,
  agregá cualquier producto al carrito, andá a `/Checkout`, llenás una tarjeta
  válida (usá el número de prueba `4111111111111111`, CVV `123`, vencimiento
  cualquier mes/año futuro, p. ej. `12/29`) y tocá **"Validar tarjeta"**.
- **Qué debería verse:** el botón cambia a "Validando…" y tarda ~5 segundos
  (por debajo del límite de 10s del propio frontend). Después de esos 5s,
  aparece "Tarjeta validada — [MARCA] terminada en [4 dígitos]" y se habilita
  "Confirmar y pagar". Ninguna pantalla en blanco, ningún error.
- **Qué indicaría una falla real:** que el botón quede colgado sin nunca
  resolver (JS roto), o que el sitio muestre un error aunque la tarjeta sí se
  haya validado (revisar en paralelo `curl http://localhost:8084/admin/tokens`
  para confirmar si el token se emitió).
- **Cómo recuperarse:** volver a `NORMAL` con el comando de arriba. No hace
  falta reiniciar nada más.

### 2.2 PayKlik en modo CAIDO

- **Qué se rompe:**
  ```bash
  curl -X PUT http://localhost:8084/admin/modo -H "Content-Type: application/json" -d '{"modo":"CAIDO"}'
  ```
- **Qué hacer en la página:** con un carrito armado, en `/Checkout` llenás
  los datos de tarjeta y tocás **"Validar tarjeta"**.
- **Qué debería verse:** tras un momento (el mock responde 503 casi de
  inmediato, no hay que esperar los 10s de timeout), aparece en rojo:
  *"No pudimos comunicarnos con el procesador de pagos. Tu carrito sigue
  guardado: intenta validar la tarjeta de nuevo en unos momentos."* El botón
  pasa a decir "Reintentar validación". El carrito NO se pierde (podés volver
  a `/Carrito` y seguir ahí).
- **Qué indicaría una falla real:** que el carrito se vacíe, que aparezca un
  error crudo de .NET (stack trace, página amarilla de desarrollo), o que el
  botón "Confirmar y pagar" quede habilitado sin un token válido.
- **Nota importante:** este mismo mensaje aparece **idéntico** si en vez de
  poner PayKlik en `CAIDO` **detenés el contenedor** (sección 2.3). El
  frontend no distingue "el proveedor respondió con error" de "el proveedor no
  existe en la red" — ambos casos caen en el mismo `catch` genérico. Para
  confirmar cuál pasó de verdad hay que mirar fuera de la página
  (`docker compose ps` o `curl http://localhost:8084/admin/estado`).
- **Cómo recuperarse:** `curl -X PUT ... -d '{"modo":"NORMAL"}'`.

### 2.3 PayKlik: contenedor detenido

- **Qué se rompe:**
  ```bash
  docker compose stop payklik-mock
  ```
- **Qué hacer en la página:** igual que 2.2: carrito armado, `/Checkout`,
  llenar tarjeta, tocar "Validar tarjeta".
- **Qué debería verse:** el mismo mensaje que en 2.2 (ver la nota de ahí).
  Puede tardar un poco más en aparecer (falla de conexión TCP en vez de un
  503 inmediato), pero sigue estando muy por debajo de los 10s de timeout del
  frontend.
- **Qué indicaría una falla real:** que la página tarde exactamente 10s y
  recién ahí muestre el error (eso indicaría que el error de conexión no se
  está detectando rápido y se está esperando el timeout completo del
  navegador), o que rompa algo más allá del checkout (el catálogo y el
  carrito no dependen de PayKlik, deberían seguir funcionando normal).
- **Cómo recuperarse:**
  ```bash
  docker compose start payklik-mock
  docker compose ps payklik-mock   # esperar "healthy"
  ```
  Repetir "Validar tarjeta": debería funcionar de inmediato, sin nada a medias.

### 2.4 Mock de mensajería: falla forzada

- **Qué se rompe:**
  ```bash
  curl -X POST "http://localhost:8089/api/mensajes/config/falla?activa=true"
  ```
- **Qué hacer en la página:** registrá una cuenta nueva en `/Auth/Registro`
  (nombre, apellido, un correo nuevo, contraseña de 8+ caracteres). El
  registro dispara el envío del código de verificación por este mock.
- **Qué debería verse:** el registro en sí **no debería fallar** — el correo
  se registra igual (el envío de la notificación es asíncrono/best-effort,
  no bloquea la creación de la cuenta) y te lleva a la pantalla de
  "Verificar código". El problema real es que el código nunca va a llegar
  mientras la falla esté activa; la pantalla de verificación debería ofrecer
  "Reenviar código" sin romperse.
- **Qué indicaría una falla real:** que el registro completo falle con un
  error 500 visible al usuario (significaría que Auth no tolera que
  Notificaciones/mensajería esté caída), o que la cuenta quede en un estado
  raro que no permita loguearse ni reenviar el código.
- **Cómo recuperarse:**
  ```bash
  curl -X POST "http://localhost:8089/api/mensajes/config/falla?activa=false"
  ```
  Tocar "Reenviar código" en la pantalla de verificación: el código debería
  llegar (comprobable en `curl http://localhost:8089/api/mensajes | tail`,
  o revisando el correo real si el mock tiene SMTP configurado).

### 2.5 Mock de mensajería: contenedor detenido

- **Qué se rompe:** `docker compose stop mock-mensajeria`
- **Qué hacer en la página:** lo mismo que 2.4 (registro de una cuenta nueva).
- **Qué debería verse:** igual que 2.4 — el registro no debería depender de
  que el envío haya salido bien.
- **Qué indicaría una falla real:** igual que 2.4.
- **Cómo recuperarse:** `docker compose start mock-mensajeria`, reenviar
  código, confirmar que llega.

### 2.6 SAT en modo CAIDO / RECHAZO

- **Qué se rompe:**
  ```bash
  curl -X PUT http://localhost:8086/admin/modo -H "Content-Type: application/json" -d '{"modo":"CAIDO"}'
  ```
- **Qué hacer en la página:** completá una compra normal de punta a punta
  (catálogo → carrito → checkout con tarjeta válida → confirmar). El cobro
  con PayKlik en `NORMAL` debe salir bien; lo que falla es la certificación
  fiscal posterior, que pasa por SAT.
- **Qué debería verse:** la pantalla de éxito del checkout (`/Checkout/Exito`)
  debe mostrar el pedido como pagado/confirmado igual — la certificación
  fiscal ante la SAT es un paso posterior y asíncrono, no debería bloquear ni
  demorar la confirmación de la compra que ve el cliente. Para ver el efecto
  real hay que ir a `/Estado` con el número de orden: el pedido debería
  seguir avanzando (la SAT tiene sus propios reintentos con backoff).
- **Qué indicaría una falla real:** que el checkout mismo se cuelgue o falle
  esperando a la SAT (el cliente jamás debería notar una caída de la SAT en
  el momento de pagar), o que el pedido quede sin ningún indicio de que algo
  está pendiente si SAT tarda mucho en recuperarse.
- **Cómo recuperarse:** `curl ... -d '{"modo":"NORMAL"}'`. Los reintentos
  automáticos de Facturación deberían certificar el documento poco después.

### 2.7 SAT: contenedor detenido

- **Qué se rompe:** `docker compose stop sat-mock`
- **Qué hacer y qué debería verse:** igual que 2.6.
- **Qué indicaría una falla real:** igual que 2.6.
- **Cómo recuperarse:** `docker compose start sat-mock`.

### 2.8 LogiExpress en modo CAIDO

- **Qué se rompe:**
  ```bash
  curl -X PUT http://localhost:8098/admin/modo -H "Content-Type: application/json" -d '{"modo":"CAIDO"}'
  ```
- **Qué hacer en la página:** completá una compra de punta a punta y
  esperá a que el pedido llegue a estado confirmado. Después andá a
  `/Estado` con ese número de orden.
- **Qué debería verse:** el pedido debe mostrar un estado consistente con
  "despacho pendiente" (no debería llegar a "en tránsito" ni "entregado")
  pero tampoco debería mostrarse como cancelado o con error. El checkout en
  sí no debe verse afectado en absoluto (LogiExpress solo entra en juego
  después de que la orden ya está confirmada y pagada).
- **Qué indicaría una falla real:** que la orden se cancele sola, o que el
  checkout se demore o falle por culpa de LogiExpress (no debería tener
  ninguna dependencia síncrona con el pago).
- **Cómo recuperarse:** `curl ... -d '{"modo":"NORMAL"}'`. El despacho debería
  retomar y avanzar el estado del pedido.
- **Nota:** el modo `LENTO` de LogiExpress no es un simple retraso corto: 1
  de cada `unoDeCada` envíos (50 por defecto) **cuelga 5 minutos reales**
  antes de responder, a propósito. Para forzarlo en todos los envíos durante
  la demo: `{"modo":"LENTO","unoDeCada":1}`. No lo dejen así por error para
  el resto de las pruebas.

### 2.9 LogiExpress: contenedor detenido

- **Qué se rompe:** `docker compose stop logiexpress-mock`
- **Qué hacer y qué debería verse:** igual que 2.8.
- **Cómo recuperarse:** `docker compose start logiexpress-mock`.

### 2.10 Inventario detenido

- **Qué se rompe:** `docker compose stop inventario-api`
- **Qué hacer en la página:** entrá al catálogo (`/Catalogo`) y mirá el
  stock de cualquier producto (la vista consulta `/Catalogo/Stock?sku=...`
  contra Inventario en vivo).
- **Qué debería verse:** el catálogo (nombres, precios, descripciones) sigue
  visible porque es una lista fija en la Web, no depende de Inventario. Lo
  que debe fallar de forma controlada es específicamente el dato de
  existencias: un mensaje o un estado "no disponible" en esa sección, no que
  toda la página se caiga.
- **Qué indicaría una falla real:** que **toda** la página de catálogo se
  rompa (indicaría que el fallo de un dato secundario está tumbando la
  página completa), o que el checkout deje pasar una compra sin haber podido
  confirmar stock en ningún momento de la saga.
- **Cómo recuperarse:** `docker compose start inventario-api`, esperar
  "healthy", recargar `/Catalogo`.

### 2.11 Órdenes detenido

- **Qué se rompe:** `docker compose stop ordenes-api`
- **Qué hacer en la página:** con un carrito armado y tarjeta ya validada,
  tocá **"Confirmar y pagar"**.
- **Qué debería verse:** un mensaje de error legible en la misma pantalla
  de checkout (no una pantalla en blanco ni un stack trace), indicando que
  no se pudo registrar el pedido. El carrito debe seguir intacto para poder
  reintentar apenas Órdenes vuelva.
- **Qué indicaría una falla real:** una pantalla en blanco, un error 500 sin
  formato, o que el carrito se vacíe igual aunque la orden nunca se haya
  creado (el cliente perdería su selección sin haber comprado nada).
- **Cómo recuperarse:** `docker compose start ordenes-api`, esperar
  "healthy", reintentar "Confirmar y pagar": debería completarse normal.

### 2.12 Redis detenido

- **Qué se rompe:** `docker compose stop redis`
- **Qué hacer en la página (dos cosas distintas a probar):**
  1. Agregar un producto al carrito desde `/Catalogo` (el carrito vive en
     Redis).
  2. Consultar `/Estado` con el número de una orden que ya existía **antes**
     de detener Redis (Consulta de Estado es cache-first en Redis con
     fallback a SQL, según el diseño documentado del servicio).
- **Qué debería verse:**
  - Carrito: en el código no hay un manejo explícito para cuando Redis no
    responde en la llamada de carrito — lo esperable, si el diseño es
    consistente, es un error controlado (mensaje de "no se pudo actualizar
    el carrito"); **no está confirmado en el código que exista ese manejo
    amigable**, así que esta es una de las pruebas donde hay que anotar
    literalmente qué aparece en pantalla, porque bien puede ser el punto
    débil que esta prueba está buscando.
  - Consulta de estado: **sí debería seguir funcionando** (más lenta, sin
    cache), gracias al fallback a SQL. Si además de lento deja de responder
    del todo, es una falla real del fallback.
- **Qué indicaría una falla real:** que la consulta de estado deje de
  funcionar por completo (el fallback a SQL no estaría funcionando), o que
  cualquiera de los dos casos muestre un error crudo sin formato.
- **Cómo recuperarse:** `docker compose start redis`, esperar "healthy",
  repetir ambas pruebas: deberían volver a la normalidad sin dejar nada a
  medias (el carrito puede haberse perdido si Redis no persiste a disco en
  este compose — anotarlo si pasa, es esperable y no es un bug).

### 2.13 Base de datos de auditoría detenida

- **Qué se rompe:** `docker compose stop db-auditoria`
- **Advertencia honesta:** esta prueba **no se puede validar completamente
  desde la interfaz**. `db-auditoria` respalda `auditoria-worker`, que
  registra actividad para trazabilidad; no hay ninguna pantalla en la Web
  que muestre datos de auditoría. Lo único observable desde afuera es que el
  resto del sistema (catálogo, checkout, pedidos) debería seguir funcionando
  exactamente igual, porque ningún flujo de compra depende de la auditoría
  en el camino síncrono.
- **Qué hacer:** completar una compra normal de punta a punta.
- **Qué debería verse:** la compra se completa sin ningún síntoma visible.
- **Qué indicaría una falla real:** que cualquier parte del flujo de compra
  se vea afectada (indicaría un acoplamiento indebido entre auditoría y el
  resto del sistema, que no debería existir).
- **Cómo recuperarse:** `docker compose start db-auditoria`.

### 2.14 Base de datos de finanzas detenida

- **Qué se rompe:** `docker compose stop db-finanzas` (respalda Pagos y
  Facturación)
- **Qué hacer en la página:** con carrito y tarjeta lista, tocá "Confirmar
  y pagar".
- **Qué debería verse:** la orden se crea y confirma (Órdenes no depende de
  esta base), pero el cobro nunca se puede procesar porque Pagos no puede
  escribir su intento. La pantalla de éxito debería reflejar el pedido como
  pendiente de pago (no como pagado), sin mostrar un error crudo.
- **Qué indicaría una falla real:** que el checkout entero falle en vez de
  quedar "pendiente" (Órdenes no debería enterarse de que la base de Pagos
  está caída más que por la ausencia de respuesta), o que la orden se
  muestre como pagada sin haberlo estado.
- **Cómo recuperarse:** `docker compose start db-finanzas`, esperar
  "healthy". El cobro debería completarse cuando Pagos pueda volver a
  escribir (según cómo esté implementado el reintento del lado de Pagos al
  momento de correr esta prueba).

### 2.15 Base de datos operacional detenida

- **Qué se rompe:** `docker compose stop db-operacional` (respalda Órdenes,
  Inventario, Auth, Consulta de Estado, Notificaciones, Integración POS/ERP
  y Despacho — es la más compartida de las tres)
- **Qué hacer en la página:** cualquier cosa: cargar `/Catalogo`, intentar
  loguearse, ver `/Estado`.
- **Qué debería verse:** esta es la prueba más disruptiva de las tres bases.
  Es esperable que el login falle, que agregar al carrito falle (depende de
  Auth) y que el checkout no se pueda completar. Lo que se está evaluando
  acá es la **calidad del mensaje de error**, no que todo siga funcionando:
  cada fallo debería mostrar un mensaje entendible, nunca una pantalla en
  blanco ni un stack trace de ASP.NET.
- **Qué indicaría una falla real:** una página en blanco, un error 500 sin
  formato, o (peor) que alguna operación parezca completarse exitosamente
  sin haberse guardado nada.
- **Cómo recuperarse:** `docker compose start db-operacional`, esperar
  "healthy" en todos los servicios que dependen de ella
  (`docker compose ps`), y repetir: catálogo, login y checkout deberían
  volver a comportarse con normalidad.

### 2.16 RabbitMQ detenido

- **Qué se rompe:** `docker compose stop rabbitmq`
- **Qué hacer en la página:** completá una compra de punta a punta con
  tarjeta válida.
- **Qué debería verse:** la llamada síncrona de creación y confirmación de
  la orden puede completarse igual (`POST /api/ordenes` y
  `.../confirmar` no dependen de que el mensaje se entregue de inmediato: se
  guarda en la tabla de outbox junto con el cambio de negocio, en la misma
  transacción). La pantalla de éxito debería mostrarse, pero el pedido se va
  a quedar **estancado** en un estado temprano de la saga (reserva de
  inventario, cobro) porque ningún consumidor puede recibir los eventos
  mientras RabbitMQ esté caído. Consultando `/Estado` varias veces, el
  estado no debería avanzar mientras dure la caída.
- **Qué indicaría una falla real:** que el checkout mismo falle de forma
  visible al usuario (no debería: el problema es que la saga no avanza, no
  que la creación de la orden falle), o que el pedido se pierda por completo
  al recuperar RabbitMQ en vez de retomar donde quedó.
- **Cómo recuperarse:** `docker compose start rabbitmq`, esperar "healthy".
  Los mensajes en la tabla de outbox deberían empezar a publicarse y la
  saga debería retomar sola (ver el estado avanzar en `/Estado` en los
  siguientes segundos, sin haber perdido la orden).

### 2.17 API Gateway detenido

- **Qué se rompe:** `docker compose stop api-gateway`
- **Qué hacer en la página:** cualquier interacción con la Web que no sea
  cargar la página de inicio pura (catálogo, carrito, login, checkout,
  estado, incidencias — todo pasa por el Gateway).
- **Qué debería verse:** esta es la prueba más disruptiva de todo el bloque:
  literalmente toda funcionalidad de la tienda deja de responder. Lo que se
  evalúa es que, aun así, la Web muestre algún mensaje de error entendible
  en vez de colgarse indefinidamente o mostrar un stack trace.
- **Qué indicaría una falla real:** una espera indefinida sin ningún
  feedback al usuario, o una pantalla en blanco total.
- **Cómo recuperarse:** `docker compose start api-gateway`, esperar
  "healthy" (depende de que todos los servicios que él orquesta también
  estén sanos), y confirmar que catálogo, login y checkout vuelven a
  funcionar de punta a punta.

---

## 3. Bloque 2 — Carga tipo Black Friday con Locust

### 3.1 Por qué Locust y qué pasa con los scripts de k6

`scripts/pruebas-carga/` y `scripts/escalabilidad/` tienen los scripts de k6
con los que ya se midió y documentó EAC-01 (cero órdenes perdidas si PayKlik
cae) y EAC-04 (escalabilidad horizontal), y esas cifras están citadas en
`ESCALABILIDAD.md` y en `bitacora.md`. **No se tocan.** Siguen siendo la
evidencia reproducible de esas dos entregas.

Locust es la herramienta **de acá en adelante**, para cargas nuevas, porque
su interfaz web muestra en vivo usuarios activos, RPS y latencias, útil para
proyectar en una demo junto a Grafana. k6 mide más preciso para un reporte
técnico (como EAC-01/EAC-04); Locust es mejor para "mostrar en pantalla
mientras pasa".

> **Aviso de puerto:** la interfaz web de Locust por defecto es el puerto
> **8089**, que en este proyecto ya está tomado por el mock de mensajería.
> Arrancá Locust con `--web-port 8890` (o el que prefieras) para no chocar.

### 3.2 Instalación

No hay convención previa de Python en este repo. Se agrega
`scripts/carga-locust/requirements.txt`:

```bash
cd SGO-Logic/scripts/carga-locust
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

### 3.3 El locustfile: qué simula y por qué

Archivo: `scripts/carga-locust/locustfile.py`. El comentario de cabecera del
archivo documenta el detalle; acá el resumen de las decisiones de diseño:

**El recorrido de compra completo, apuntando a la Web (`localhost:8092`), no
a la API directo** (eso ya lo hace `eac01-hu023-carga.js`, que le pega
directo a Órdenes y mide otra cosa: latencia de API pura). Cada usuario
virtual de Locust:

1. Se registra con un correo único (`POST /Auth/Registro`), lee el código de
   verificación desde el mock de mensajería (`GET /api/mensajes` en el
   puerto 8089, sin pasar por la Web) y se verifica (`POST /Auth/Verificar`)
   e inicia sesión (`POST /Auth/Login`) — **una sola vez**, en `on_start`, no
   en cada iteración.
2. En bucle, cada iteración es una compra: navega el catálogo
   (`GET /Catalogo`), consulta stock de un SKU al azar
   (`GET /Catalogo/Stock`), lo agrega al carrito
   (`POST /Carrito/Agregar`), tokeniza una tarjeta de prueba **directo contra
   el mock de PayKlik** (`POST http://localhost:8084/v1/tokens` — así es como
   lo hace el JS del navegador real, no pasa por la Web ni por el Gateway) y
   confirma la compra (`POST /Checkout/Procesar`, que crea y confirma la
   orden del lado del servidor).

**Ojo con la tokenización (la advertencia del enunciado):** el checkout
exige un `tokenMedioPago` vigente cuando el medio de pago es `TARJETA`. Si
el locustfile no tokeniza antes de mandar el formulario de checkout, el
`Checkout/Procesar` devuelve el formulario con el error *"Valida tu tarjeta
antes de confirmar el pedido"* y ninguna orden se crea. El script ya hace
los dos pasos en orden; si lo modificás, no te saltees la tokenización.

**Por qué cada usuario virtual se registra en vez de reusar
`cliente1/2/3@sgo.local`:** el carrito vive en Redis bajo la clave
`cart:{sub del JWT}`. Con cientos de usuarios virtuales concurrentes
compartiendo solo 3 cuentas, sus carritos se pisarían entre sí y la prueba
mediría una condición de carrera del propio test, no del sistema. El costo
es que cada usuario virtual hace un registro completo (incluida la
verificación) una sola vez al arrancar — eso agrega una ráfaga de tráfico a
Auth y al mock de mensajería durante el *ramp-up*, que vale la pena mirar en
Grafana también, pero no cuenta para el cálculo de la tasa de órdenes por
segundo de la sección 3.4 (esa tasa es solo del bucle de compra en régimen
estable).

**Por qué no uses los 3 usuarios sembrados para nada más que una prueba
manual rápida:** son útiles para una corrida de humo con 1-3 usuarios de
Locust (`-u 3`), pero no para 50+.

### 3.4 Cálculo de la tasa: N/T

Con **N** usuarios concurrentes y un recorrido de compra que tarda **T**
segundos en promedio (incluyendo las pausas de "pensar" entre pasos y el
`wait_time` entre iteraciones), la tasa resultante es:

```
tasa (órdenes/segundo) = N / T
```

El locustfile trae pausas cortas entre pasos (catálogo → carrito → tokenizar
→ confirmar, 1-3s cada una) más un `wait_time` de 3 a 8 segundos entre
compras completas de un mismo usuario. Sumando eso a la latencia real de
cada llamada HTTP, T ronda **12-20 segundos por compra completa** en un
sistema sano — pero **no lo tomen como dato fijo**: la propia interfaz de
Locust muestra el RPS y el tiempo de respuesta medio en vivo; usen eso, no
esta estimación a priori, para ajustar N durante la corrida.

**Discrepancia que hay que resolver antes de fijar el objetivo — no la
inventé, la encontré leyendo `ESCALABILIDAD.md` y el propio script de
EAC-01, y no coincide con el enunciado tal como lo planteaste:**

- El enunciado dice: *"el piso documentado del proyecto son 80 órdenes por
  minuto (~1.33/s), así que el objetivo son ~11 órdenes por segundo"* (es
  decir, 8x sobre 80/min).
- Pero `eac01-hu023-carga.js` (y `ESCALABILIDAD.md` línea 81, que remite a
  `PRUEBA-CARGA-EAC01.md#que-mide`) derivan las cosas al revés: el **piso de
  día normal** son ~10.7 órdenes/minuto (32 tiendas × 20 órdenes/hora/tienda,
  EV-16), y **80 órdenes/minuto (~1.33/s) ya es el pico con el factor 8x
  aplicado** (85 redondeado hacia abajo a 80, de forma conservadora). Es la
  cifra que ya usa EAC-01 con k6 como carga sostenida.

Tomar 8x sobre 80/min (lo que pide el enunciado) da 640 órdenes/min ≈ 10.7-11
órdenes/s, pero eso sería en realidad un escenario de **64x** sobre el día
normal (8x aplicado dos veces), no de 8x. Dejo las dos lecturas documentadas
para que el equipo elija con conocimiento de causa, en vez de que yo decida
en silencio:

| Lectura | Objetivo de órdenes/s | Qué representa |
| --- | --- | --- |
| A — igualar el pico ya documentado en EAC-01 | ~1.33/s (80/min) | 8x sobre el día normal (consistente con el DDA y con lo que k6 ya midió — permite comparar Locust contra esa misma marca) |
| B — lectura literal del enunciado de esta tarea | ~10.7-11/s | 8x aplicado sobre un número que ya es 8x (64x sobre el día normal real) |

El plan de carga de la sección 3.5 apunta al **objetivo B (~11 órdenes/s)**
porque es el que pediste explícitamente, pero con esta nota al lado para que
el equipo decida si de verdad quiere un escenario de 64x o si el objetivo
real era iguales condiciones que EAC-01 (opción A, mucho más alcanzable con
menos usuarios).

### 3.5 Plan de carga escalonado

No arranquen directo en 500. La idea es encontrar el escalón donde el
sistema empieza a degradarse, no solo confirmar el número final.

```bash
locust -f locustfile.py --host http://localhost:8092 --web-port 8890
```

Abrí `http://localhost:8890` y cargá cada escalón manualmente (Locust
permite cambiar de usuarios/spawn rate sin reiniciar):

| Escalón | Usuarios (N) | Spawn rate | Duración sugerida | Qué anotar |
| --- | --- | --- | --- | --- |
| 1 | 50 | 5/s | 3-5 min | RPS estable, p95 de latencia por endpoint, % de fallos (debería ser ~0%) |
| 2 | 100 | 10/s | 3-5 min | Igual que arriba + `docker stats` (¿algún contenedor en 100% CPU sostenido?) |
| 3 | 200 | 15/s | 3-5 min | Igual + primeros síntomas de degradación (p95 subiendo, fallos > 0%) |
| 4 | 500 | 20/s | 5 min | Dónde exactamente se rompe o degrada, y qué componente lo muestra primero (Grafana, `docker stats`, o los propios errores en Locust) |

En cada escalón, dejalo correr en régimen estable (no en el ramp-up) antes
de anotar nada — los primeros 30-60 segundos de cada escalón no son
representativos.

**Advertencia explícita:** 500 usuarios de Locust contra 28 contenedores
corriendo en una sola laptop pueden saturar la máquina del que corre la
prueba **antes** que al sistema bajo prueba. Cómo distinguir una cosa de la
otra:

- Corré `docker stats` en otra terminal mientras dura la prueba. Si **todos**
  los contenedores muestran CPU alta simultáneamente y la laptop entera se
  siente lenta (el mouse se traba, el ventilador se dispara), es la máquina,
  no el sistema.
- Si en cambio **uno o dos** contenedores puntuales (por ejemplo
  `ordenes-api` o `db-operacional`) muestran CPU al 100% mientras el resto
  está tranquilo, eso sí es una señal real de dónde se degrada el sistema.
- El Activity Monitor de macOS (CPU global de la laptop) es el chequeo más
  rápido: si el promedio de todos los núcleos ya está saturado antes de
  llegar a 500 usuarios de Locust, el cuello de botella es la laptop.
- Si tenés más de una máquina disponible, correr Locust desde una máquina
  distinta a la que corre `docker compose` elimina la duda de raíz.

### 3.6 El inventario se agota — decisión tomada

Si todos los usuarios de carga compran el mismo SKU, en pocos segundos se
queda sin stock y el resto empieza a recibir rechazos que no miden nada útil
sobre el sistema, solo la velocidad de una carrera contra 50-200 unidades.

**Decisión documentada:** el locustfile reparte la compra entre 6 de los 8
SKUs del catálogo (`PROD-002`, `PROD-003`, `PROD-004`, `PROD-006`,
`PROD-007`, `PROD-008`), elegidos al azar por iteración, y siempre con
entrega a `DOMICILIO` (reserva contra los centros de distribución, con ~200
unidades por SKU — el mayor margen disponible). **Se excluyen a propósito
`PROD-001` y `PROD-005`**, porque están sembrados con stock deliberadamente
bajo (3 y 0 unidades) para la prueba de sobreventa manual de la sección 3.8;
si Locust los toca, arruina ese escenario antes de que el equipo llegue a
probarlo a mano.

Con 6 SKUs a ~200 unidades cada uno en los CD (1200 unidades de margen
total), el plan de carga de la sección 3.5 no debería agotar el inventario
en ninguno de los 4 escalones salvo que se lo deje corriendo mucho más
tiempo del sugerido. Si de todas formas se agota, subí la cantidad
sembrada con:

```bash
docker compose exec -T db-operacional psql -U "$POSTGRES_USER" -d sgo_ordenes -c \
  "UPDATE inventario.existencia SET cantidad_fisica = cantidad_fisica + 500 WHERE sku IN ('PROD-002','PROD-003','PROD-004','PROD-006','PROD-007','PROD-008') AND tipo_ubicacion = 'CD';"
```

(ajustá `$POSTGRES_USER` al usuario real configurado en tu `.env`).

### 3.7 Qué observar en pantalla mientras corre

**Con una o dos personas navegando a mano, en paralelo a la carga:**

1. Antes de arrancar Locust, cronometrar (con el cronómetro del teléfono o
   `Cmd+Shift+4` para grabar con timestamp) cuánto tarda `/Catalogo` y un
   checkout completo **en frío**, como referencia base.
2. Con Locust corriendo en el escalón de 100 o 200 usuarios, repetir
   exactamente la misma navegación y volver a cronometrar. Anotar la
   diferencia contra la referencia base, no un número absoluto solo.
3. Prestar atención puntual a: si "Validar tarjeta" tarda notoriamente más
   que en frío, si el catálogo tarda en pintar, si algún botón queda
   deshabilitado más tiempo del esperado, si aparece algún mensaje de error
   que en frío no aparecía.
4. Anotar el escalón exacto (50/100/200/500) en el que la navegación manual
   empieza a sentirse distinta — ese es el dato más útil de esta sección,
   más que la cifra de RPS sola.

**En la interfaz de Locust (`http://localhost:8890`):**

- Pestaña "Charts": RPS y tiempo de respuesta en vivo, por tipo de request.
  Buscar el punto donde el tiempo de respuesta empieza a crecer más rápido
  que el RPS (señal clásica de saturación).
- Pestaña "Failures": cualquier fila que no sea 0 hay que investigarla —
  puede ser una falla real del sistema o un timeout del propio Locust si la
  laptop está saturada (ver sección 3.5).

**En Grafana (`http://localhost:3000`):**

- Los dashboards provistos en `observabilidad/grafana/provisioning` ya
  deberían tener paneles por servicio (revisar cuáles existen antes de la
  corrida, no asumir nombres). Mirar en particular: uso de CPU/memoria por
  contenedor, latencia de RabbitMQ (profundidad de cola — una cola creciendo
  sin bajar es la señal más clara de que un consumidor no da abasto), y
  latencia de Postgres.
- Correlacionar cualquier pico de latencia visto en Locust con el mismo
  instante en Grafana, para identificar qué componente específico lo causó.

### 3.8 Prueba de sobreventa desde la UI

Usa el escenario que ya viene sembrado, sin que haga falta preparar nada:
`PROD-001` (Lumina Smart Refrigerator Pro) tiene solo **3 unidades** en la
tienda Portales (`TIENDA-03-PORTALES`), con recogida en tienda.

- **Cómo dejarlo con stock bajo:** no hace falta ningún paso — ya está así
  desde el seed. Si en algún momento se agotó por otra prueba y hace falta
  resembrarlo, el comando mínimo es:
  ```bash
  docker compose exec -T db-operacional psql -U "$POSTGRES_USER" -d sgo_ordenes -c \
    "UPDATE inventario.existencia SET cantidad_fisica = 3 WHERE sku = 'PROD-001' AND ubicacion_id = 'TIENDA-03-PORTALES';"
  ```
- **Qué hacer:** con 4-5 personas (o 4-5 pestañas/perfiles de navegador
  distintos, cada uno logueado con una cuenta distinta para no compartir
  carrito), cada una: agregar "Lumina Smart Refrigerator Pro" al carrito,
  ir a `/Checkout`, elegir **"Recogida en tienda"** → **"MegaStore Portales
  — Zona 17"**, tokenizar una tarjeta válida y confirmar, todas al mismo
  tiempo (contar "1, 2, 3, ¡ya!" en voz alta o coordinarlo por chat para que
  los clics sean simultáneos).
- **Qué debería verse:** como máximo 3 compras exitosas. El resto debe
  terminar con un mensaje claro de que el producto ya no tiene stock
  disponible en esa tienda (verificable en `/Estado` de cada orden: las que
  no alcanzaron stock deberían quedar rechazadas/canceladas, nunca
  confirmadas), no un error genérico ni una compra fantasma que después no
  se pueda despachar.
- **Qué indicaría una falla real:** que se confirmen más de 3 compras (eso
  es una sobreventa real), o que las rechazadas muestren un error crudo en
  vez de un mensaje de "sin stock" entendible.
- **Cómo recuperarse:** resembrar con el comando de arriba si hace falta
  repetir la prueba.

---

## 4. Problemas conocidos y cómo distinguirlos de una falla real

- **El contenedor todavía está arrancando:** `docker compose ps` lo muestra
  como `starting` en vez de `healthy`. Esperar a que termine antes de dar
  por buena o mala una prueba — un fallo mientras un servicio está
  arrancando no es representativo.
- **Caché del navegador:** si después de recuperar un servicio la página
  sigue mostrando el mismo error, forzar recarga sin caché (`Cmd+Shift+R` en
  Chrome/Firefox en macOS) antes de concluir que el problema persiste.
- **Token de tarjeta vencido a los 900 segundos:** el token que emite
  PayKlik al validar la tarjeta vence 15 minutos después de emitido. Si se
  deja el checkout abierto mucho tiempo entre "Validar tarjeta" y
  "Confirmar y pagar" (por ejemplo, mientras se documentan resultados de
  otra prueba en el medio), hay que volver a tokenizar. El frontend ya
  revalida esto cada 5 segundos y debería avisar antes de dejar confirmar
  con un token vencido — si no avisa y deja pasar un token vencido, eso sí
  es una falla real a reportar.
- **Reserva de inventario vencida por TTL:** una orden que quedó a medias
  en la saga (por ejemplo, durante alguna de las pruebas del bloque 1) tiene
  su reserva de inventario con un TTL; pasado ese tiempo la reserva se
  libera sola. Si al reanudar una prueba interrumpida el stock aparece
  distinto a lo esperado, puede ser esto y no una sobreventa — revisar el
  timestamp de la orden original antes de reportarlo como falla.
- **Saturación de la máquina que corre la prueba:** ver la sección 3.5. Si
  dudás si un problema es del sistema o de tu laptop, es lo primero a
  descartar antes de anotar nada como hallazgo.

---

## 5. Tabla de resultados

Completar una fila por prueba corrida. "Resultado" es ✅ (se comportó como
"qué debería verse") o ❌ (se comportó como "qué indicaría una falla real"),
con una nota de qué pasó exactamente si fue ❌.

| # | Prueba | Fecha | Quién la corrió | Resultado | Notas |
| --- | --- | --- | --- | --- | --- |
| 2.1 | PayKlik LENTO | | | | |
| 2.2 | PayKlik CAIDO | | | | |
| 2.3 | PayKlik contenedor detenido | | | | |
| 2.4 | Mensajería falla forzada | | | | |
| 2.5 | Mensajería contenedor detenido | | | | |
| 2.6 | SAT CAIDO/RECHAZO | | | | |
| 2.7 | SAT contenedor detenido | | | | |
| 2.8 | LogiExpress CAIDO | | | | |
| 2.9 | LogiExpress contenedor detenido | | | | |
| 2.10 | Inventario detenido | | | | |
| 2.11 | Órdenes detenido | | | | |
| 2.12 | Redis detenido | | | | |
| 2.13 | db-auditoria detenida | | | | |
| 2.14 | db-finanzas detenida | | | | |
| 2.15 | db-operacional detenida | | | | |
| 2.16 | RabbitMQ detenido | | | | |
| 2.17 | API Gateway detenido | | | | |
| 3.5 | Carga escalonada — escalón 50 | | | | RPS: ___ / p95: ___ / fallos: ___ |
| 3.5 | Carga escalonada — escalón 100 | | | | RPS: ___ / p95: ___ / fallos: ___ |
| 3.5 | Carga escalonada — escalón 200 | | | | RPS: ___ / p95: ___ / fallos: ___ |
| 3.5 | Carga escalonada — escalón 500 | | | | RPS: ___ / p95: ___ / fallos: ___ |
| 3.7 | Navegación manual en frío | | | | Catálogo: ___s / Checkout: ___s |
| 3.7 | Navegación manual bajo carga | | | | Catálogo: ___s / Checkout: ___s / escalón: ___ |
| 3.8 | Sobreventa PROD-001 (3 unidades) | | | | Compras confirmadas: ___/5 |