# Mocks de sistemas legacy/externos — SGO-Logic

Paquete combinado con los dos simuladores que el equipo necesita para desarrollar y
probar los contextos **Integración ERP** e **Integración POS** sin depender de los
sistemas reales:

| Mock | Puerto por defecto (Node puro) | Puerto de host (Docker Compose de este directorio) | Simula | Ver |
|---|---|---|---|---|
| `mock-erp/` | `4000` | `4100` | ERP-Core sobre AS/400 | `mock-erp/README.md` |
| `mock-pos/` | `4001` | `4101` | 32 instancias de POS | `mock-pos/README.md` |

## Levantar ambos con Docker Compose

```bash
docker compose up --build
```

- Mock ERP: http://localhost:4100
- Mock POS: http://localhost:4101

Puertos de host 4100/4101 (no 4000/4001) a propósito: el compose principal
del proyecto (`SGO-Logic/docker-compose.yml`) ya publica `mock-erp` y
`mock-pos` en 4000/4001. Con puertos de host distintos, este compose se
puede levantar para desarrollar/probar los mocks de forma aislada sin
chocar con el principal aunque los dos estén arriba a la vez. El puerto
interno del contenedor no cambia (sigue siendo 4000/4001).

Para correr en segundo plano: `docker compose up -d --build`.
Para detener y limpiar: `docker compose down`.
Para ver logs de un servicio: `docker compose logs -f mock-erp` (o `mock-pos`).

## Levantar sin Docker (alternativa)

Si el equipo no tiene Docker disponible, cada mock corre igual con Node.js puro:

```bash
cd mock-erp && npm install && npm start   # puerto 4000
cd mock-pos && npm install && npm start   # puerto 4001
```

**Nota:** en este modo cada mock escucha en su puerto por defecto (4000 /
4001) — distinto del puerto de host que publica el `docker-compose.yml` de
este directorio (4100 / 4101). `test-cliente.sh` y `test-integracion.sh`
apuntan a los puertos de Docker (4100/4101): para probarlos contra mocks
levantados con `npm start`, ajusta la URL base al inicio de cada script.

## Estructura

```
sgo-logic-mocks/
├── docker-compose.yml
├── mock-erp/
│   ├── Dockerfile
│   ├── server.js
│   ├── package.json
│   ├── README.md
│   └── test-cliente.sh
└── mock-pos/
    ├── Dockerfile
    ├── server.js
    ├── package.json
    ├── README.md
    └── test-cliente.sh
```

## Prueba de extremo a extremo de cada mock

Con ambos servicios levantados con Docker Compose (`docker compose up
--build` en este directorio; los scripts apuntan a los puertos de host
4100/4101, ver nota arriba si vas a probar contra `npm start`):

```bash
bash mock-erp/test-cliente.sh
bash mock-pos/test-cliente.sh
```

## Prueba de integración (ambos mocks a la vez)

`test-integracion.sh` simula, de forma simplificada, lo que hace Órdenes al confirmar
una venta : propaga el evento al POS de la tienda de origen y,
por separado, la incluye en el siguiente lote de consolidación hacia el ERP:

```bash
bash test-integracion.sh
```

Este script no reemplaza la lógica real de Órdenes ni del worker de Integración ERP/POS,
solo dispara ambos mocks con datos consistentes para verificar rápidamente que los dos
servicios responden como se espera cuando se les habla en el mismo escenario de venta.

## Notas de mapeo al DDA

- Ninguno de los dos mocks expone una forma de "modificar" al
  sistema legacy más allá de recibir lo que el mundo real recibiría (un lote en el ERP,
  un evento de venta en el POS). Los endpoints `/admin/*` son exclusivos del mock, para
  poder forzar condiciones de prueba (caídas, reinicios); no existen en los sistemas reales.
- Control por secuencia + cuadre de totales, sin que el ERP conozca el modelo de dominio nuevo.
- Idempotencia por evento y aislamiento de fallas por tienda. Ninguna de las 32 instancias afecta a las demás si está fuera de línea.
- Ambos mocks son deliberadamente **stateful pero volátiles** (memoria del proceso): al
  reiniciar el contenedor se pierde el estado, lo cual es intencional para pruebas
  repetibles. No usar para nada que necesite persistencia entre corridas.
