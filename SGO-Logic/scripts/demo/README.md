# Datos de demo

`sembrar-demo.sh` prepara los datos minimos para la demostracion:

- usuario `demo@sgo.local` (o `DEMO_EMAIL`),
- verificacion y login del usuario,
- existencias repetibles para tres SKUs en centro de distribucion y tienda.

El repositorio no tiene un servicio de catalogo independiente; por eso los nombres, precios y descripciones del catalogo se siguen enviando desde el flujo de carrito/orden. El script siembra la parte persistida que existe hoy: identidad e inventario.

## Uso

Desde `SGO-Logic`, con el stack levantado por Docker Compose:

```bash
./scripts/demo/sembrar-demo.sh
```

La primera ejecucion registra el usuario y termina solicitando el codigo de verificacion. El codigo se muestra en los logs de `auth-api`:

```bash
docker compose logs auth-api | grep -i codigo
DEMO_VERIFICATION_CODE=123456 ./scripts/demo/sembrar-demo.sh
```

Se pueden cambiar `BASE_URL`, `DEMO_EMAIL`, `DEMO_PASSWORD`, `DEMO_NOMBRE` y `DEMO_VERIFICATION_CODE` mediante variables de entorno.
