# Pruebas de seguridad del MVP

Última tarjeta de la etiqueta Calidad. Antes de esta tarjeta no existía ningún
script, herramienta ni documento de pruebas de seguridad en el repositorio —
lo único ya demostrado era `PermisosInmutabilidadTests` de Auditoría (bloque 3
de abajo, que esta tarjeta reutiliza sin rehacer).

El enfoque es el mismo que en HU-025 (`ESCALABILIDAD.md`) y en la evidencia de
EAC-03 (`AUDITORIA.md`): un script reproducible que corre contra el sistema
**levantado** y produce un veredicto, más este documento con la evidencia
medida. No son tests unitarios — lo que se defiende es el comportamiento real
del sistema corriendo, no una aserción contra un doble de prueba.

Script: [`scripts/seguridad/verificar.sh`](../../SGO-Logic/scripts/seguridad/verificar.sh)
(usa [`scripts/seguridad/barrido-columnas.sql`](../../SGO-Logic/scripts/seguridad/barrido-columnas.sql)
para el barrido de bases). Comando único:

```bash
cd SGO-Logic
./scripts/seguridad/verificar.sh
```

Requiere el stack arriba (`docker compose up -d`) y `curl`, `python3` y
`docker compose exec`/`psql` disponibles — nada nuevo respecto a lo que ya
usan `scripts/escalabilidad/verificar.sh` y las pruebas de carga.

Igual que `scripts/escalabilidad/verificar.sh`, cada chequeo corre de forma
independiente (sin `set -e`) y el dictamen final es de tres estados:

- **`APROBADO`** (código de salida 0): todos los chequeos bloqueantes
  corrieron y no se encontró ningún hallazgo real.
- **`RECHAZADO`** (código de salida 1): todos corrieron, pero se encontró al
  menos un hallazgo real (PAN/CVV persistido o logueado, o el endpoint
  protegido aceptó un token inválido, o un permiso de Auditoría no bloqueó lo
  que debía).
- **`INCOMPLETO`** (código de salida 2): uno o más chequeos no se pudieron
  evaluar (por ejemplo, la orden canario no se generó). Se listan por nombre;
  no es aprobación ni rechazo.

El hallazgo del API Gateway sin autenticación (bloque 2) es una excepción
deliberada a ese esquema: se reporta siempre, por separado, y **no participa
en el dictamen** — ya se confirmó que existe, y esta tarjeta lo reporta, no lo
corrige.

---

## Bloque 1 — RES-08: ningún PAN ni CVV se persiste

### Qué exige y por qué

RES-08 (DDA, restricción regulatoria/seguridad): *"No se almacenan datos
completos de tarjeta ni códigos de seguridad."* Sostenida por EV-25 (política
corporativa de MegaStore) y resuelta arquitectónicamente por
[ADR-010](../ADR/ADR-010-tokenizacion-no-almacenamiento-datos-tarjeta.md):
la tokenización se delega a PayKlik; Pagos solo guarda el token opaco, los
últimos cuatro dígitos y la marca.

Hasta esta tarjeta, esa decisión estaba escrita en ADR-010 y en comentarios
del código, pero nadie había barrido las bases para confirmarla.

### Cómo viaja el dato de tarjeta (verificado en el código, no asumido)

Antes de escribir el script se revisó todo el camino desde el checkout hasta
PayKlik. El hallazgo central: **en este MVP ningún endpoint acepta número de
tarjeta ni CVV como entrada, en ningún punto del flujo**:

- `POST /api/ordenes` (creación) y `POST /api/ordenes/{id}/confirmar`
  (confirmación) — sus DTOs (`CrearOrdenRequest`, `ConfirmarOrdenRequest`) no
  tienen ningún campo de tarjeta. `MedioPago` es un enum validado
  server-side (`Enum.TryParse<MedioPago>`, ver
  `ServicioConfirmacionOrden.cs`): cualquier valor que no sea uno de los
  medios de pago declarados (`TARJETA`, `EFECTIVO`, etc.) se rechaza con 400
  antes de crear la orden — no es un campo de texto libre donde se pudiera
  colar un PAN.
- El evento `CobroSolicitado` que Órdenes publica hacia Pagos
  (`CobroSolicitadoData`) tiene los campos `TokenMedioPago`, `UltimosCuatro`
  y `Marca` — pero Órdenes los emite siempre en `null`
  (`MapaEventosSagaOrden.cs`): en este MVP no existe todavía un paso de
  checkout que capture y tokenice una tarjeta real antes de solicitar el
  cobro.
- `PagosController` no tiene ningún endpoint que reciba datos de tarjeta.
  `IClientePayKlik`/`PeticionCargoPayKlik` solo aceptan `TokenMedioPago`
  (`string?`), documentado explícitamente como *"Solo token opaco, nunca PAN
  ni CVV"*.
- El mock de PayKlik (`SgoLogic.Mocks.PayKlik`) tampoco recibe ni devuelve
  número de tarjeta o CVV: con un token nulo o vacío (que es siempre el caso
  hoy), sintetiza una tarjeta de prueba fija (`DerivarTarjeta`: últimos
  cuatro `4242`, marca `VISA`) — el mismo número de prueba que usan Stripe y
  otras pasarelas para demos, no un dato real.
- La UI de checkout (`SgoLogic.Web`, `Views/Checkout/Index.cshtml`) muestra un
  aviso de cumplimiento PCI-DSS, pero tampoco tiene ningún campo de captura de
  tarjeta: `medioPago` es un radio button (`TARJETA`/`TRANSFERENCIA`) que
  `CheckoutController.Procesar` reenvía tal cual — no hay script de PayKlik ni
  llamada del navegador al proveedor. Se verificó explícitamente porque se
  reportó lo contrario; no es así en el código actual.

**Evidencia complementaria ya existente, a nivel de tipos:**
`tests/SgoLogic.Pagos.Tests/Application/ServicioCobroTests.cs` tiene
`Ningun_tipo_del_contexto_Pagos_expone_campos_de_PAN_CVV_o_vencimiento`, un
test por reflexión que falla si `Pago`, `IntentoPago`, `EnlaceReintento`,
`SolicitudCobro`, `PeticionCargoPayKlik`, `ResultadoPayKlik` o los DTOs/eventos
de Pagos alguna vez ganan una propiedad con nombre `pan`, `cvv`, `cvc`,
`numerotarjeta`, `cardnumber`, `vencimiento`, `expiracion`, `expiry` o
similar. Es una guarda a nivel de **forma del tipo en tiempo de compilación**,
no de **contenido de la base en tiempo de ejecución** — complementa el
barrido de este documento, no lo reemplaza: ese test no diría nada si alguien
guardara un PAN dentro de un campo `string` ya existente y de nombre inocuo
(como pasaría, por ejemplo, si algo terminara escribiendo texto libre sensible
en `descripcion`). Por eso el barrido de bases sigue corriendo como parte de
esta tarjeta.

Esto es una limitación real a tener presente y no oculta: **no se puede
inyectar un PAN por un campo de pago porque ese campo no existe** — el
riesgo que RES-08 previene (que alguien capture y guarde una tarjeta real) no
tiene hoy una ruta de entrada al sistema. La prueba automatizada, entonces,
no puede simular "un atacante metiendo un PAN en el checkout": simula algo
más útil y verificable, descrito abajo.

### Cómo prueba el script esto

1. Siembra existencias y crea+confirma una orden real (mismo patrón que
   `scripts/pruebas/camino-feliz.ps1` y las pruebas de carga), con un
   **marcador canario** en la descripción de la línea de la orden — el único
   campo de texto libre que un cliente controla y que de verdad viaja hasta
   un evento de dominio:

   ```
   Prueba RES-08 <run-id>: NUMERO_PRUEBA=4111111111111111 CVV_PRUEBA=123 VENC_PRUEBA=12/29
   ```

   `4111111111111111` es el número de prueba estándar de Visa (no es una
   tarjeta real ni pertenece a nadie). Los tres sub-marcadores llevan una
   etiqueta (`NUMERO_PRUEBA=`, `CVV_PRUEBA=`, `VENC_PRUEBA=`) para que
   buscarlos no dispare falsos positivos contra montos, IDs o secuencias que
   por coincidencia contengan `123` o `12/29`.

2. Barre **dinámicamente** las tres instancias de Postgres
   (`db-operacional`, `db-finanzas`, `db-auditoria`): para cada una,
   enumera **todas** sus bases (no una lista fija — si mañana se agrega una
   base nueva, el barrido la cubre sin tocar el script) y, dentro de cada
   base, recorre `information_schema.columns` para revisar **todas** las
   columnas de tipo texto/`varchar`/`jsonb`/`json` de **todos** los esquemas,
   no solo las tablas donde uno esperaría encontrar algo. El payload de
   Auditoría (`eventos_auditoria.payload`, `jsonb`) se revisa igual que
   cualquier columna de texto, castings mediante.
3. Interpreta el mapa de coincidencias:
   - **Control positivo** — el marcador debe aparecer en
     `ordenes.linea_orden.descripcion` (donde se inyectó) y en
     `public.eventos_auditoria.payload` (a donde debería propagarse vía el
     evento de dominio). Si no aparece en ninguno de los dos, el barrido o la
     inyección no son confiables y el chequeo se marca `INCOMPLETO`, no
     `APROBADO` — un barrido que nunca encuentra nada podría simplemente
     estar roto.
   - **Hallazgo bloqueante** — el marcador aparece en `sgo_pagos` o
     `sgo_facturacion`: el contexto de Pagos/Facturación no tiene ningún
     motivo para ver texto libre de la orden, así que su sola presencia ahí
     es indicio de una fuga de datos entre contextos.
4. Chequeo dirigido, acotado a `pagos.pago` y `pagos.intento_pago`: ninguna
   columna de texto debe contener una corrida de 13 a 19 dígitos seguidos
   (forma típica de un número de tarjeta), más allá de los 4 dígitos
   esperados en `ultimos_cuatro`. Confirma además qué quedó realmente para el
   intento de la orden canario: `token_medio_pago` (hoy siempre `NULL`, dado
   el punto anterior), `ultimos_cuatro` y `marca` — lo permitido por
   ADR-010, nada más.
5. Barre los logs de **todos** los contenedores (`docker compose logs`,
   todo el stack) buscando el marcador — un PAN en los logs es tan grave
   como uno en la base.

### Limitación de esta medición, con honestidad

El marcador demuestra que el barrido **funciona** (lo encuentra donde se
inyectó) y que **no aparece** donde no debería (Pagos/Facturación). No
demuestra que sea imposible que un PAN real entre al sistema en el futuro:
si alguien agrega mañana un campo de captura de tarjeta a algún endpoint,
esta prueba no lo sabría de antemano — habría que volver a correrla después
de ese cambio. Tampoco cubre el frontend (`SgoLogic.Web`): esta prueba es
sobre las bases y los logs del backend, no sobre qué campos expone la UI.

---

## Bloque 2 — Validación de JWT

### Qué exige y por qué

A diferencia de RES-08, el DDA no tiene un EAC ni un ADR dedicado a
autenticación/autorización — se documenta eso explícitamente en vez de
inventar una referencia. Lo que sí existe es HU-007 (registro, verificación
de correo, login, carrito unificado), que es de donde sale el único mecanismo
de autenticación real del sistema: JWT emitido por `auth-api`
(`JwtService`, HMAC-SHA256, clave `Auth:JwtSecret`) y validado por
`[Authorize]` en **un solo endpoint de todo el sistema**:
`POST /api/carrito/asociar` (`CarritoController.cs`). El resto de
`CarritoController` (`GET`/`POST items`/`PUT items`/`DELETE items`) es
intencionalmente accesible sin token: el carrito unificado sirve tanto a
usuarios autenticados como a invitados, por diseño de HU-007.

Se verificó `[Authorize]` en cada controlador de cada servicio
(`grep -rl "\[Authorize\]" src/Services`): el único resultado es
`CarritoController.cs`. Ningún endpoint de Órdenes, Pagos, Facturación,
Inventario, Consulta de Estado, Notificaciones, Despacho o Auditoría exige
token.

### Hallazgo confirmado (se reporta, no se corrige aquí)

`src/Services/SgoLogic.ApiGateway/Program.cs` es un proxy puro: `AddSgoDefaults()`
+ `AddReverseProxy().LoadFromConfig(...)` + `MapReverseProxy()`. No hay
`UseAuthentication()`, no hay `UseAuthorization()`, no hay ninguna política
de acceso. **El API Gateway no exige autenticación en ninguna ruta que
enruta, ni siquiera pagos o facturación.** Esto no es una suposición: el
script golpea rutas reales de Pagos, Facturación, Inventario y Órdenes sin
ningún token y confirma que responden sin exigirlo (sección de resultados,
abajo).

Esto queda fuera de alcance para corregir en esta tarjeta — tocar el gateway
o agregar `[Authorize]` a otros servicios es trabajo de otro integrante y no
es lo que esta tarjeta pidió resolver. Se documenta como hallazgo para que el
equipo decida, aparte, si amerita una historia de usuario o un ADR.

### Cómo prueba el script esto

Contra `POST /api/carrito/asociar` (el único endpoint protegido):

1. Sin ningún header `Authorization` → se espera `401`.
2. Con un token malformado (`Bearer esto-no-es-un-jwt`) → se espera `401`.
3. Con un token firmado con la clave real (`Auth__JwtSecret`, leída del
   contenedor `auth-api` en tiempo de ejecución) pero con `exp` una hora en
   el pasado → se espera `401`.
4. Con un token bien formado pero firmado con una clave distinta a la real
   → se espera `401`.
5. Con un token real: el script se registra (`POST /api/auth/registro`),
   lee el código de verificación de los **logs de `auth-api`** (ver nota
   abajo), lo verifica (`POST /api/auth/verificar`) e inicia sesión
   (`POST /api/auth/login`) para obtener un JWT genuino → se espera **algo
   distinto de `401`**. Sin este control positivo, un validador roto que
   rechazara todo pasaría los cuatro chequeos anteriores por las razones
   equivocadas.

Nota, ya documentada en el `README.md` raíz: `auth-api` no envía correo
real — el código de verificación se escribe en sus logs
(`_logger.LogInformation("DEMO — Codigo de verificacion para {Email}: {Codigo}")`
en `AuthService.cs`), y el script lo lee de ahí con
`docker compose logs auth-api`.

Después, sin ningún token, contra `GET /api/pagos/ordenes/{id}`,
`GET /api/facturacion/documentos/{id}`, `GET /api/inventario/existencias` y
`POST /api/ordenes` vía el gateway: se reporta el código obtenido (se espera
que ninguno sea `401`/`403`, confirmando el hallazgo de arriba). Esto es
informativo, no bloqueante — ya se sabe la respuesta.

---

## Bloque 3 — Permisos de Auditoría (evidencia reutilizada)

Ya demostrado por `tests/SgoLogic.Auditoria.Tests/PermisosInmutabilidadTests`
y documentado en [`AUDITORIA.md`](AUDITORIA.md#inmutabilidad-a-nivel-de-motor).
Esta tarjeta no reimplementa esa lógica: corre las mismas tres aserciones
como SQL directo contra la base real, para tener la evidencia en el mismo
script y la misma corrida que los otros dos bloques.

1. `UPDATE` sobre `eventos_auditoria` con el rol restringido de la
   aplicación (`Auditoria__AppUsuario`) → se espera `permission denied`
   (SQLSTATE `42501`).
2. `DELETE` sobre `eventos_auditoria` con el mismo rol restringido → se
   espera `42501`.
3. `UPDATE` sobre `eventos_auditoria` con el usuario **dueño** de la tabla
   (`POSTGRES_USER`, que sí tendría el permiso) → se espera que el trigger
   `trg_bloquear_modificacion_eventos_auditoria` lo bloquee de todas formas
   (SQLSTATE `P0001`, mensaje *"eventos_auditoria es de solo insercion..."*)
   — defensa en profundidad, no solo el `GRANT`.

Las tres consultas apuntan a la fila más reciente de `eventos_auditoria`
(`ORDER BY fecha_recepcion DESC LIMIT 1`) porque el trigger es `FOR EACH
ROW`: un `UPDATE`/`DELETE` que no matchea ninguna fila no lo dispara, así
que hace falta una fila real para que la prueba signifique algo.

---

## Resultados

*(Pendiente de completar por quien corra `scripts/seguridad/verificar.sh` —
ver la guía de arriba. No se rellena con cifras de memoria: se pega aquí la
salida real de la corrida.)*

- Fecha de la corrida:
- Dictamen general (`APROBADO` / `RECHAZADO` / `INCOMPLETO`):
- Bloque 1 (RES-08) — controles positivos alcanzados, hallazgos si los hubo:
- Bloque 2 (JWT) — códigos obtenidos en cada caso, confirmación del hallazgo
  del gateway:
- Bloque 3 (Auditoría) — resultado de las tres consultas:
- Salida completa del script: