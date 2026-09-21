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

**Esta sección se reescribió el 2026-09-21.** La versión anterior describía
un MVP donde ningún endpoint aceptaba datos de tarjeta porque el paso de
tokenización todavía no existía. Con los cambios que trajo esta rama desde
`develop` (tokenización real de Angel, `cc77cdb`; checkout con tokenización
de Jona, `64f7f94`; ADR-020), eso ya no es así: **ahora sí hay un punto real
donde entra un PAN/CVV**, y es exactamente donde ADR-010 dice que debe estar
— dentro del mock de PayKlik, nunca dentro de SGO-Logic.

### Cómo viaja el dato de tarjeta hoy (verificado en el código, no asumido)

- **`POST /v1/tokens`, en el mock de PayKlik** (`SgoLogic.Mocks.PayKlik/Tokenizacion.cs`,
  `Program.cs`) — es el único endpoint de todo el sistema que recibe
  `numeroTarjeta`, `cvv`, `mesExpiracion` y `anioExpiracion`. Valida el
  número (Luhn, longitud y marca por BIN) y el CVV, y si es válido devuelve
  un token opaco de un solo uso (`tok_pk_...`, vigente 900 s por defecto)
  junto con los últimos cuatro y la marca. El PAN y el CVV **nunca se
  guardan**: `AlmacenTokens` solo retiene `TarjetaTokenizada`
  (últimos cuatro, marca, mes/año) — ni siquiera en memoria del mock
  sobrevive la validación. `PeticionTokenizacion.ToString()` está
  sobrescrito para que un log accidental del propio record no imprima el PAN
  ni el CVV. CORS abierto (`AllowAnyOrigin`) solo en esta ruta, a propósito:
  es la que el navegador debe poder llamar directo, sin pasar por el gateway
  ni por ningún servicio de SGO-Logic.
- **`CrearOrdenRequest.TokenMedioPago`** (nuevo campo opcional) es lo único
  que Órdenes recibe: el token que ya emitió PayKlik. Obligatorio cuando
  `medioPago = TARJETA` (`PoliticaMediosPago.RequiereToken`); si falta,
  `ServicioConfirmacionOrden` lo rechaza con 400 y código
  `token_medio_pago_requerido` (`ExcepcionTokenMedioPagoRequerido`) **antes**
  de crear la orden.
- **`Orden.TokenMedioPago`** sí se persiste, pero de forma deliberadamente
  transitoria: hay una columna real (`ordenes.orden.token_medio_pago`,
  migración `20260921000722_TokenMedioPagoOrden`), pero el propio dominio
  documenta y aplica su ciclo de vida corto — `TomarTokenMedioPago()` lo lee
  y lo pone en `null` en el mismo momento en que arma `CobroSolicitado`, y
  cualquier transición a un estado terminal sin haber llegado a ese punto
  también lo limpia. No es el dato que RES-08 prohíbe (es un token opaco de
  PayKlik, no un PAN), pero el bloque 1.4b de abajo confirma igual que ese
  ciclo de vida se cumple de verdad contra la base real.
- **`CobroSolicitadoData.TokenMedioPago`** ya no es `null` a la fuerza:
  `MapaEventosSagaOrden.cs` ahora lo llena con
  `orden.TomarTokenMedioPago()`. `UltimosCuatro`/`Marca` siguen en `null` en
  este evento a propósito — Órdenes nunca los conoce, los resuelve PayKlik
  al cobrar.
- **`POST /v1/cargos`, en el mismo mock** — recibe el token (nunca PAN/CVV),
  lo consume una sola vez contra `AlmacenTokens` y devuelve los últimos
  cuatro y la marca **reales** de la tarjeta tokenizada. Ya no existe el
  `DerivarTarjeta` que sintetizaba una tarjeta fija (`4242`/`VISA`) para un
  token nulo: un cobro con `TARJETA` y sin token, o con un token
  inexistente/vencido/ya usado, ahora se rechaza con 422 y un código
  distinguible (`TOKEN_REQUERIDO`, `TOKEN_INVALIDO`, `TOKEN_VENCIDO`,
  `TOKEN_YA_USADO`), que Pagos traduce a `RECHAZADO` sin contarlo como falla
  del proveedor (`TokenMedioPagoInvalidoException`, `CodigosToken`).
- **`pagos.intento_pago`** guarda exactamente lo mismo que antes en forma
  (`token_medio_pago`, `ultimos_cuatro`, `marca` — mismo esquema, sin
  columnas nuevas), pero ahora con el valor **real** que devolvió PayKlik al
  resolver el token, no un valor fijo de mock.

**Hallazgo real, no corregido aquí — la UI de checkout llama a un endpoint
de tokenización que no existe.** `Views/Checkout/Index.cshtml` sí agregó un
formulario de tarjeta (PAN/CVV/vencimiento) y una función
`tokenizarYConfirmar()` que intenta tokenizar antes de enviar el formulario
— la intención es correcta y coincide con ADR-010. Pero el `fetch` apunta a
`http://localhost:8084/api/tokenizar` (o `/api/pagos/tokenizar` vía gateway
como *fallback*) con el cuerpo `{ pan, cvv, vencimiento }`. **Ninguna de las
dos rutas existe**: se verificó con `grep -rn "tokenizar" src/Services` que
el único endpoint real es `POST /v1/tokens` del mock, con el cuerpo
`{ numeroTarjeta, cvv, mesExpiracion, anioExpiracion }` — ruta y forma del
cuerpo distintas en ambos aspectos. Tal como está, un usuario que use la UI
real con `TARJETA` no lograría tokenizar: el `fetch` fallaría (404) y
`tokenizarYConfirmar()` lo reportaría como error sin enviar la orden. Parece
un caso de dos personas construyendo cada lado (Jona el formulario y el
JavaScript, Angel el mock y el contrato) sobre supuestos distintos, sin
reconciliar antes de fusionar a `develop`. No se corrige aquí porque no es
código propio de esta tarjeta y la instrucción fue no tocar código de
aplicación — se reporta con la ruta exacta y el cuerpo exacto para que quien
lo arregle no tenga que volver a investigarlo.

Consecuencia para esta prueba: el script no puede ejercitar el flujo
tokenizando *a través de la UI* (está roto), así que llama directamente a
`POST /v1/tokens` del mock — exactamente lo que el navegador *debería* hacer
si `tokenizarYConfirmar()` apuntara al lugar correcto. Esto prueba el
backend de punta a punta (mock → Órdenes → Pagos) igual de a fondo; lo que
no prueba es que la UI actual pueda completar ese flujo, porque no puede.

**Evidencia complementaria ya existente:** además del test por reflexión
`Ningun_tipo_del_contexto_Pagos_expone_campos_de_PAN_CVV_o_vencimiento`
(`tests/SgoLogic.Pagos.Tests/Application/ServicioCobroTests.cs`, ya citado
antes de esta reescritura), Angel agregó junto con la tokenización un
paquete de pruebas mucho más específico en
`tests/SgoLogic.Pagos.Tests/Application/CobroTokenizadoTests.cs`. La que
más directamente prueba "no persistencia" es
`Un_cobro_tokenizado_no_deja_PAN_ni_CVV_en_la_entidad_persistida_ni_en_el_payload_del_outbox`:
procesa un cobro tokenizado real (con un PAN de prueba, `4242424242424242`)
contra un `PagosDbContext` en memoria, y recorre recursivamente **todas**
las propiedades de la entidad `Pago` persistida y **todo** el árbol JSON de
cada evento de outbox publicado, buscando el PAN, el CVV o un nombre de
propiedad prohibido — con un test adicional
(`El_detector_de_la_prueba_reconoce_un_PAN_y_un_CVV_escondidos_bajo_cualquier_nombre`)
que confirma que su propio detector funciona, el mismo principio del control
positivo que usa el bloque 1 de este script. Hay además
`El_modelo_de_persistencia_de_Pagos_no_tiene_columnas_para_PAN_CVV_ni_vencimiento`
(recorre `DbContext.Model` completo) y
`Los_datos_del_evento_de_solicitud_de_cobro_y_de_resultado_no_tienen_campos_de_tarjeta`
(la misma guarda de tipos, ahora extendida a `CobroSolicitadoData`).

Estas pruebas son **complementarias, no redundantes**, con el barrido de
este documento: corren contra un `PagosDbContext` con proveedor InMemory y
un doble de PayKlik, en el proceso de xUnit — nunca tocan Postgres de
verdad, RabbitMQ de verdad, Auditoría de verdad, ni los logs de ningún
contenedor. Cubren muy bien el contexto de Pagos en aislamiento; no cubren
la columna nueva `ordenes.orden.token_medio_pago`, la propagación real vía
`sgo.eventos` hasta `eventos_auditoria`, ni si algo termina en los logs del
stack real. Por eso el barrido de bases y logs sigue corriendo como parte de
esta tarjeta.

### Cómo prueba el script esto

1. Tokeniza una tarjeta de prueba **contra el mock real** (`POST
   http://localhost:8084/v1/tokens`, el mismo endpoint al que debería llamar
   el navegador): PAN `4111111111111111` (Visa de prueba estándar, no una
   tarjeta real), CVV `123`, vencimiento a 3 años. Guarda el token, los
   últimos cuatro y la marca que devuelve PayKlik.
2. Confirma que `POST /api/ordenes` con `medioPago: TARJETA` y **sin**
   `tokenMedioPago` sigue respondiendo 400 (`ExcepcionTokenMedioPagoRequerido`)
   — si esto alguna vez responde 201, alguien quitó la validación.
3. Crea y confirma una orden real con ese token (mismo patrón que
   `scripts/pruebas/camino-feliz.ps1` y las pruebas de carga), cargando
   además un **marcador canario** en la descripción de la línea — el campo
   de texto libre que un cliente controla y que de verdad viaja hasta un
   evento de dominio:

   ```
   Prueba RES-08 <run-id>: NUMERO_PRUEBA=4111111111111111 CVV_PRUEBA=123 VENC_PRUEBA=<mes/año>
   ```

   Los sub-marcadores de CVV y vencimiento llevan una etiqueta (`CVV_PRUEBA=`,
   `VENC_PRUEBA=`) para que buscarlos no dispare falsos positivos contra
   montos, IDs o secuencias que por coincidencia contengan `123` o una fecha
   parecida. El PAN se busca **sin** etiqueta (dieciséis dígitos exactos no
   necesitan una: nada más en el sistema debería coincidir por azar), lo que
   de paso cubre tanto el marcador de texto libre como una fuga real del PAN
   tokenizado en el paso 1, con una sola búsqueda.

4. Barre **dinámicamente** las tres instancias de Postgres
   (`db-operacional`, `db-finanzas`, `db-auditoria`): para cada una,
   enumera **todas** sus bases (no una lista fija — si mañana se agrega una
   base nueva, el barrido la cubre sin tocar el script) y, dentro de cada
   base, recorre `information_schema.columns` para revisar **todas** las
   columnas de tipo texto/`varchar`/`jsonb`/`json` de **todos** los esquemas,
   no solo las tablas donde uno esperaría encontrar algo. El payload de
   Auditoría (`eventos_auditoria.payload`, `jsonb`) se revisa igual que
   cualquier columna de texto, castings mediante.
5. Interpreta el mapa de coincidencias:
   - **Control positivo** — el marcador debe aparecer en
     `ordenes.linea_orden.descripcion` (donde se inyectó) y en
     `public.eventos_auditoria.payload` (a donde debería propagarse vía el
     evento de dominio). Si no aparece en ninguno de los dos, el barrido o la
     inyección no son confiables y el chequeo se marca `INCOMPLETO`, no
     `APROBADO` — un barrido que nunca encuentra nada podría simplemente
     estar roto.
   - **Hallazgo bloqueante** — el marcador (o el PAN tokenizado) aparece en
     `sgo_pagos` o `sgo_facturacion`: ninguno de los dos tiene motivo para
     ver texto libre de la orden ni, mucho menos, un PAN.
6. Chequeo dirigido, acotado a `pagos.pago` y `pagos.intento_pago`: ninguna
   columna de texto debe contener una corrida de 13 a 19 dígitos seguidos
   (forma típica de un número de tarjeta), más allá de los 4 dígitos
   esperados en `ultimos_cuatro`.
7. Confirma que lo persistido para el intento de la orden canario es
   **exactamente** lo que devolvió la tokenización real en el paso 1 —
   `ultimos_cuatro` y `marca` deben coincidir con el valor real, no con
   cualquier valor fijo — y que `token_medio_pago` coincide con el token
   emitido.
8. Confirma que `ordenes.orden.token_medio_pago` quedó en `NULL` para esa
   orden después de que la saga cursó el cobro, tal como documenta
   `Orden.TomarTokenMedioPago()`.
9. Barre los logs de **todos** los contenedores (`docker compose logs`,
   todo el stack) buscando el marcador y el PAN — un PAN en los logs es tan
   grave como uno en la base, y ahora hay un PAN real que sí entra a un
   proceso del sistema (el mock), aunque sea nunca destinado a persistir.

### Limitación de esta medición, con honestidad

El marcador y el PAN tokenizado demuestran que el barrido **funciona** (los
encuentra donde se inyectan/tokenizan) y que **no aparecen** donde no
deberían (Pagos/Facturación). No demuestra que sea imposible que un PAN real
entre al sistema por una ruta distinta en el futuro: si alguien agrega otro
campo de captura de tarjeta en algún endpoint, esta prueba no lo sabría de
antemano — habría que volver a correrla después de ese cambio. Tampoco
ejercita la UI real (`SgoLogic.Web`): como se documentó arriba, su llamada de
tokenización está rota (endpoint y forma del cuerpo equivocados), así que
esta prueba llama al mock directamente, igual que *debería* hacerlo el
navegador. Es una prueba del backend de punta a punta, no una prueba de que
la UI actual pueda completarlo.

---

## Bloque 2 — Validación de JWT

### Qué exige y por qué

Cuando se escribió la primera versión de esta sección, el DDA no tenía un EAC
ni un ADR dedicado a autenticación/autorización. Eso cambió el 2026-09-21:
[ADR-020](../ADR/ADR-020-autenticacion-en-el-borde.md) ("La autenticación se
valida en el borde, en el API Gateway, con el JWT emitido por Auth") ya
documenta la decisión — validar el JWT una sola vez en el gateway, con una
bandera `Gateway:ExigirJwt` (`GATEWAY_EXIGIR_JWT` en `.env`) por defecto
**apagada**, sobre las rutas de órdenes, pagos, facturación, inventario,
despacho y auditoría. **Se verificó explícitamente si esa decisión ya tiene
código, y no lo tiene**: `SgoLogic.ApiGateway` sigue teniendo un único
archivo fuente, `Program.cs`, sin `AddAuthentication`, sin `AddJwtBearer` y
sin ninguna referencia a `ExigirJwt` en todo el repositorio (`grep -rn
"ExigirJwt" .` no devuelve nada fuera del propio ADR). Es una decisión de
arquitectura registrada, no algo implementado todavía — el comportamiento
medido por este bloque sigue siendo el de antes de ADR-020.

Lo que sí existe y sigue sin cambios es HU-007 (registro, verificación de
correo, login, carrito unificado), de donde sale el único mecanismo de
autenticación real hoy: JWT emitido por `auth-api` (`JwtService`,
HMAC-SHA256, clave `Auth:JwtSecret`) y validado por `[Authorize]` en **un
solo endpoint de todo el sistema**: `POST /api/carrito/asociar`
(`CarritoController.cs`). El resto de `CarritoController`
(`GET`/`POST items`/`PUT items`/`DELETE items`) es intencionalmente
accesible sin token: el carrito unificado sirve tanto a usuarios
autenticados como a invitados, por diseño de HU-007.

Se verificó `[Authorize]` en cada controlador de cada servicio
(`grep -rl "\[Authorize\]" src/Services`): el único resultado sigue siendo
`CarritoController.cs`. Ningún endpoint de Órdenes, Pagos, Facturación,
Inventario, Consulta de Estado, Notificaciones, Despacho o Auditoría exige
token.

### Hallazgo confirmado (se reporta, no se corrige aquí)

`src/Services/SgoLogic.ApiGateway/Program.cs` sigue siendo un proxy puro:
`AddSgoDefaults()` + `AddReverseProxy().LoadFromConfig(...)` +
`MapReverseProxy()`. No hay `UseAuthentication()`, no hay
`UseAuthorization()`, no hay ninguna política de acceso, ni la bandera que
ADR-020 propone. **El API Gateway no exige autenticación en ninguna ruta que
enruta, ni siquiera pagos o facturación.** Esto no es una suposición: el
script golpea rutas reales de Pagos, Facturación, Inventario y Órdenes sin
ningún token y confirma que responden sin exigirlo (sección de resultados,
abajo).

Esto queda fuera de alcance para corregir en esta tarjeta — tocar el gateway
o agregar `[Authorize]` a otros servicios es trabajo de otro integrante y no
es lo que esta tarjeta pidió resolver. ADR-020 ya le da a ese trabajo un
diseño aceptado y un lugar donde implementarlo; falta la implementación.

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