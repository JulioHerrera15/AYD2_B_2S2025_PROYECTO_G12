# ADR-020: La autenticación se valida en el borde, en el API Gateway, con el JWT emitido por Auth

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-22

## Contexto

La sección 6.1 del DDA asigna al Borde / API Gateway la responsabilidad de ser el punto único de entrada, con autenticación y ruteo, y le atribuye la validación de acceso. En el MVP el gateway solo cumple la segunda parte: `SgoLogic.ApiGateway` registra `AddReverseProxy` con YARP y enruta por prefijo hacia cada contexto, sin autenticar nada. Auth emite un JWT firmado con HMAC-SHA256 y el secreto `Auth:JwtSecret`, pero el único punto donde se valida es el atributo `[Authorize]` de `CarritoController`, dentro del propio servicio Auth. Las rutas de órdenes, pagos, facturación, inventario, despacho y auditoría se sirven hoy a cualquier llamador que alcance el gateway, aunque son las que crean cobros, documentos fiscales y movimientos de inventario.

Esa brecha contradice la trazabilidad de quién ejecutó cada operación (PRE-05) y el tratamiento de datos personales y de medios de pago (PRE-08), y deja sin cumplir lo que el propio DDA declara del borde. Faltaba decidir dónde se valida el token y qué hacen los servicios internos con él.

## Decisión

El JWT emitido por Auth se valida en el API Gateway y no en cada servicio. La decisión comprende seis reglas:

1. **Validación con JwtBearer y secreto compartido.** El gateway registra `AddAuthentication` con `AddJwtBearer` y valida la firma con el mismo secreto `Auth:JwtSecret` que usa Auth para emitir el token, con los mismos parámetros de validación que Auth (firma y vencimiento, con 30 segundos de tolerancia de reloj; sin validación de emisor ni de audiencia). Con la bandera encendida y sin `Auth:JwtSecret`, el gateway no arranca.
2. **Rutas protegidas.** Se exige un token válido en las rutas de órdenes, pagos, facturación, inventario, despacho y auditoría, tanto en su prefijo `/api/<contexto>` como en su forma corta `/<contexto>` cuando la ruta la tiene. La implementación protege además `/api/erp/*`, la ruta de administración de Integración ERP. Cada ruta protegida declara en `appsettings.json` del gateway la política de YARP `RequireAuthenticatedUser`. Una petición sin token o con un token inválido o vencido recibe 401 en el gateway y no llega al servicio.
3. **Rutas anónimas.** Quedan sin exigencia de token `/api/auth/*` y `/auth/*` (registro e inicio de sesión, que son las rutas que emiten el token) y los puntos de salud `/health/*` del gateway, que no son rutas del proxy. Las rutas de carrito siguen protegidas por Auth con su propio `[Authorize]`; esta decisión no las modifica.
4. **Bandera de configuración.** La exigencia se activa con la bandera `Gateway:ExigirJwt` del gateway (variable de entorno `Gateway__ExigirJwt`). Con la bandera en `false`, el gateway se comporta como hasta ahora: registra la política `RequireAuthenticatedUser` como una que admite toda petición y enruta sin validar. Con la bandera en `true`, registra la autenticación JwtBearer y aplica las reglas 2 y 3. El valor por defecto en `appsettings.json` y `appsettings.Development.json` del gateway es `false`, para no romper los scripts de carga y de camino feliz que hoy llaman al gateway sin token.
5. **Los servicios internos confían en la red interna.** Órdenes, Pagos, Facturación, Inventario, Despacho y Auditoría no revalidan el token: el gateway es el único punto donde se decide el acceso, y los servicios asumen que toda petición que llega por la red interna de Docker ya fue admitida. El gateway no reescribe ni propaga claims como encabezados adicionales; reenvía la petición con su encabezado `Authorization` original.
6. **Los scripts cumplen la exigencia.** Los scripts k6 y el de camino feliz que llamen al gateway deben enviar el token obtenido en `POST /api/auth/login` o correr con la bandera apagada. Los scripts que llaman directamente al puerto de un servicio, sin pasar por el gateway, no se ven afectados.

## Alternativas consideradas

Se evaluaron tres opciones: (1) validar el JWT una sola vez en el borde, con confianza en la red interna (elegida); (2) validar el JWT en cada servicio, descartada porque obliga a repetir la configuración de autenticación, el secreto y las rutas anónimas en seis contextos más, con el riesgo de que uno quede sin proteger, y porque el DDA ya asigna la validación de acceso al borde, no a los contextos; y (3) mTLS entre servicios, descartada porque exige una autoridad certificadora, emisión y rotación de certificados por servicio, y una configuración de red que el MVP en Docker Compose no necesita demostrar ni el equipo puede sostener con sus competencias actuales (PRE-17, RES-12). Se eligió la primera porque cierra la brecha del borde con un solo punto de cambio y sin tocar el código de los contextos.

## Consecuencias

### Positivas
- La autenticación queda en un único punto, coherente con la sección 6.1 del DDA, y los contextos no incorporan dependencias de autenticación ni cambian su código.
- Las rutas que crean cobros, documentos fiscales y movimientos de inventario dejan de ser accesibles sin una identidad emitida por Auth.
- La bandera permite adoptar la exigencia sin romper las pruebas de carga y de camino feliz existentes: se enciende para la demostración y para producción, y se apaga para los scripts que aún no envían token.

### Negativas
- En el MVP el puerto de cada servicio sigue publicado en el host, solo para pruebas y demostración: quien alcance ese puerto evade el gateway y, con él, la autenticación. Esta decisión solo es efectiva si en producción se expone únicamente el gateway y los servicios quedan en la red interna.
- Un servicio que reciba una petición desde la red interna no puede distinguir a quién representa ni si el gateway la admitió. La confianza en la red interna reemplaza la defensa en profundidad: un componente interno comprometido puede llamar a cualquier otro sin token.
- El token solo autentica: el JWT de Auth no incluye roles, de modo que el borde admite o rechaza al llamador pero no distingue qué operaciones puede ejecutar cada persona. La autorización por rol queda pendiente.
- Auth y el gateway comparten un secreto simétrico. Quien lo conozca puede emitir tokens válidos, y su rotación exige reiniciar ambos servicios con el mismo valor.
- Con la bandera apagada, que es el valor por defecto, el MVP no exige autenticación en el borde. Las rutas de consulta de estado, incidencias, notificaciones y mensajes quedan fuera del alcance de esta decisión y permanecen anónimas aunque la bandera se encienda.
- Límite del MVP: el servicio `api-gateway` de `SGO-Logic/docker-compose.yml` no recibe hoy ni `Gateway__ExigirJwt` ni `Auth__JwtSecret`, y `.env.example` no define una variable para la bandera. En el stack de Docker Compose la exigencia queda siempre apagada; para encenderla hay que agregar al servicio `api-gateway` las variables `Gateway__ExigirJwt: "true"` y `Auth__JwtSecret: ${JWT_SECRET}`. La configuración del compose del gateway corresponde a su responsable (202300353).

## Preocupaciones y evidencia que atiende

PRE-05, PRE-08, PRE-17. Evidencia: EV-04, sección 6.1 del DDA (Borde / API Gateway).
