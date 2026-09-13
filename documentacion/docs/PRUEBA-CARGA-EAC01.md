# Prueba de carga EAC-01 / HU-023 — cero ordenes perdidas si PayKlik falla

Esta prueba responde una sola pregunta: si PayKlik (la pasarela de pago) se
cae durante el pico de Black Friday, ¿se pierde alguna orden? El criterio de
aceptacion de HU-023 es cero.

## Como correrla

Requiere el stack local arriba (`docker compose up -d` desde `SGO-Logic/`).
No instala nada en la maquina: k6 y `psql` corren dentro de contenedores.

```bash
docker run --rm --network sgo-logic_default -v "${PWD}/scripts/pruebas-carga:/scripts" -e ORDENES_POR_MINUTO=80 grafana/k6 run /scripts/eac01-hu023-carga.js
```

Al terminar, esperar al menos un minuto (el procesamiento del cobro es
asincrono: outbox + cola + reintentos) y correr la verificacion:

```bash
bash scripts/pruebas-carga/eac01-hu023-verificar.sh
```

Ambos comandos se ejecutan desde `SGO-Logic/`. La red `sgo-logic_default` es
la que crea este `docker-compose.yml` (confirmada con `docker network ls`);
si tu proyecto de compose tiene otro nombre, ajusta el `--network`.

## Que mide

El script de carga (`eac01-hu023-carga.js`) crea ordenes de forma continua
durante toda la corrida y, por cada una, la lleva hasta el punto donde
arranca el cobro real contra PayKlik: crear → confirmar → forzar
`INVENTARIO_RESERVADO` → forzar `COBRO_SOLICITADO`. Las dos transiciones se
fuerzan por el endpoint generico de transiciones porque el servicio de
Inventario todavia no existe en este MVP; es la unica via disponible para
llegar a `COBRO_SOLICITADO`, y queda anotado en el propio script.

La corrida tiene tres fases, con el mock de PayKlik cambiando de modo contra
su endpoint de administracion mientras la carga sigue constante:

1. **Calentamiento** (90 s) — PayKlik en modo normal.
2. **Caida** (120 s) — PayKlik responde 503 a todo cobro.
3. **Recuperacion** (90 s) — PayKlik vuelve a modo normal.

El volumen de carga es 80 ordenes por minuto. El DDA no fija un numero
absoluto para el pico de "ocho veces" — solo dice que es 8x un dia normal sin
decir cuanto es un dia normal. Se derivo ese numero de la unica magnitud de
escala que el DDA si da (las 32 tiendas del canal fisico, EV-16): a un ritmo
conservador de 1 orden cada 3 minutos por tienda en la hora pico de un dia
normal, el canal fisico solo ya da ~11 ordenes/minuto; aplicando el factor 8x
declarado da ~85/minuto, redondeado hacia abajo a 80. Es una estimacion de
ingenieria, documentada como tal, no un dato oficial del cliente — se ajusta
sin tocar el script con la variable `ORDENES_POR_MINUTO`.

El script de carga no espera el resultado del cobro: la llamada que fuerza
`COBRO_SOLICITADO` responde en cuanto Ordenes guarda el cambio de estado, no
cuando PayKlik contesta. Esa es justamente la garantia que EAC-01 exige
(el fallo del proveedor no bloquea el flujo), y es lo que permite que la
carga generadora corra a ritmo constante sin importar si PayKlik esta caido.

La verificacion (`eac01-hu023-verificar.sh`) corre despues, contra las bases
reales (`sgo_ordenes`, `sgo_consulta_estado` y `sgo_pagos`), y cuenta:

- ordenes creadas,
- cuantas quedaron en un estado consultable (con fila en el modelo de
  lectura de Consulta de Estado),
- cuantas en `PAGO_APLICADO` y cuantas en `PAGO_PENDIENTE`,
- cuantas quedaron atascadas en `COBRO_SOLICITADO` sin ningun intento de
  pago registrado,
- cuantos intentos de pago hay por resultado (iniciado/exitoso/fallido/
  rechazado).

## Umbral de aprobacion

Una orden se cuenta como **perdida** si, al terminar la espera de drenado,
no tiene ningun intento de pago registrado en `pagos.intento_pago`, o si
quedo en un estado no consultable. El umbral es **cero órdenes perdidas**.
Si el conteo da cero, la prueba aprueba; cualquier numero mayor que cero es
un fallo de HU-023, sin importar cuantas ordenes se hayan procesado bien.

## Que queda fuera de esta prueba, y por que

- **El 99.9% de disponibilidad de Pagos que tambien pide EAC-01.** Eso es una
  metrica de uptime sobre una ventana operativa real; una corrida local de
  unos minutos no la puede medir de forma significativa. Esta prueba valida
  la garantia de no-perdida, no la disponibilidad del servicio.
- **El autoescalado horizontal (HPA).** Las tareas de HU-023 lo mencionan,
  pero el entorno de este MVP es docker compose de una sola instancia por
  servicio (ADR-013), no Kubernetes: no hay HPA que probar todavia. Esta
  prueba corre contra esa unica instancia.
- **EAC-02 (consulta de estado bajo 5 s) y EAC-04 (escalabilidad sostenida).**
  Son HU-024 y HU-025, con sus propias pruebas. Esta prueba toca la vista de
  Consulta de Estado solo para saber si la orden es consultable, no mide su
  latencia.
- **El numero exacto de "ocho veces".** El DDA no lo fija; el numero usado
  aqui (80 ordenes/minuto) es una estimacion documentada arriba, no una cifra
  contractual. Si el negocio publica una cifra oficial, se reemplaza
  cambiando una sola variable de entorno.
