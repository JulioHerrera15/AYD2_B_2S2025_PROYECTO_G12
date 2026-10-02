// HU-025 / EAC-04 — Escalabilidad horizontal por servicio: carga concurrente
// sostenida contra ordenes-api para ejercitar sus N replicas (API) y, via el
// outbox + RabbitMQ, las N replicas de auditoria-worker (Worker) que consume
// TODO evento del sistema (binding "#" sobre sgo.eventos). No certifica un
// SLA de latencia (eso ya lo hacen EAC-01/eac01-hu023-carga.js y
// EAC-02/eac02-hu024-carga.js): el objetivo aqui es generar volumen
// concurrente sostenido para que el panel "Reparto de carga entre instancias"
// de Grafana y la verificacion SQL (verificar.sh) tengan evidencia real bajo
// varias replicas compitiendo por el mismo trabajo.
//
// Corre con k6 en contenedor, igual que los demas scripts de
// scripts/pruebas-carga/ (sin dependencias en la maquina local):
//
//   docker run --rm --network sgo-logic_default \
//     -v "${PWD}/scripts/escalabilidad:/scripts" \
//     -e VUS=20 -e DURACION_SEG=120 -e RUN_ID=$(date +%s) \
//     grafana/k6 run /scripts/carga.js
//
// (la red se confirma con `docker network ls`; si tu proyecto de compose
// tiene otro nombre, ajusta el --network).
//
// Antes de correrlo: levanta las replicas con escalar.sh. Despues de correrlo:
// espera unos 60s a que drene el pipeline asincrono (outbox + consumidores) y
// corre verificar.sh con el mismo RUN_ID.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const ORDENES_URL = (__ENV.ORDENES_URL || 'http://ordenes-api:8080').replace(/\/+$/, '');
const INVENTARIO_URL = (__ENV.INVENTARIO_URL || 'http://inventario-api:8080').replace(/\/+$/, '');
// Tokenizacion de la tarjeta en la boveda de Pagos (no en PayKlik).
const PAGOS_URL = (__ENV.PAGOS_URL || 'http://pagos-api:8080').replace(/\/+$/, '');
const PRECIO_UNITARIO = 100.0;

const VUS = parseInt(__ENV.VUS || '20', 10);
const CALENTAMIENTO_SEG = parseInt(__ENV.CALENTAMIENTO_SEG || '10', 10);
const DURACION_SEG = parseInt(__ENV.DURACION_SEG || '120', 10);
const ENFRIAMIENTO_SEG = parseInt(__ENV.ENFRIAMIENTO_SEG || '10', 10);

// Identifica esta corrida para poder acotar la verificacion SQL a su ventana
// de tiempo y para no mezclar sus datos con los de otras pruebas manuales.
const INICIO_MS = Date.now();
const RUN_ID = __ENV.RUN_ID || `${INICIO_MS}`;
export const PREFIJO_CLIENTE = `CARGA-HU025-${RUN_ID}`;
const SKU = `SKU-ESCALA-HU025-${RUN_ID}`;

const JSON_HEADERS = { headers: { 'Content-Type': 'application/json' } };

const ordenesCreadas = new Counter('sgo_hu025_ordenes_creadas');
const ordenesConfirmadas = new Counter('sgo_hu025_ordenes_confirmadas');
const fallos = new Counter('sgo_hu025_fallos');

export const options = {
  scenarios: {
    // Carga sostenida: rampa corta, meseta al valor objetivo durante toda la
    // ventana de medicion, rampa de bajada. La tasa NO varia dentro de la
    // meseta: lo que se quiere ver es reparto entre replicas bajo carga
    // constante, no un patron de picos.
    carga_sostenida: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: `${CALENTAMIENTO_SEG}s`, target: VUS },
        { duration: `${DURACION_SEG}s`, target: VUS },
        { duration: `${ENFRIAMIENTO_SEG}s`, target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
};

function sembrarExistencias() {
  const body = JSON.stringify({
    sku: SKU,
    ubicacionId: 'CD-01',
    tipoUbicacion: 'CENTRO_DISTRIBUCION',
    cantidadFisica: 1000000,
  });
  const res = http.put(`${INVENTARIO_URL}/api/inventario/existencias`, body, JSON_HEADERS);
  if (res.status !== 200) {
    console.error(`[hu025-escala] FALLO siembra de existencias: status=${res.status} body=${res.body}`);
    throw new Error(`Siembra de inventario fallo con status ${res.status}`);
  }
  console.log(`[hu025-escala] existencias sembradas: ${SKU} en CD-01`);
}

// Ordenes valida el precio de cada linea contra el catalogo de Inventario: el SKU
// de la corrida se registra con el precio que mandan las ordenes.
function registrarProducto() {
  const body = JSON.stringify({
    nombre: 'Producto de prueba de escalabilidad HU-025/EAC-04',
    categoria: 'Pruebas',
    precio: PRECIO_UNITARIO,
    icono: 'inventory_2',
    activo: true,
  });
  const res = http.put(`${INVENTARIO_URL}/api/inventario/productos/${SKU}`, body, JSON_HEADERS);
  if (res.status !== 200 && res.status !== 201) {
    throw new Error(`Registro del producto ${SKU} fallo con status ${res.status}: ${res.body}`);
  }
}

// Una orden con TARJETA exige el token de la boveda de Pagos. Null si no se emitio.
function tokenizarTarjeta() {
  const res = http.post(
    `${PAGOS_URL}/api/pagos/tokens`,
    JSON.stringify({
      numero: '4111111111111111',
      mesExp: 12,
      anioExp: new Date().getFullYear() + 3,
      cvv: '123',
      titular: 'Cliente Escalabilidad',
      guardar: false,
    }),
    { ...JSON_HEADERS, tags: { paso: 'tokenizar' } }
  );
  return check(res, { 'tarjeta tokenizada (201)': (r) => r.status === 201 }) ? JSON.parse(res.body).token : null;
}

export function setup() {
  registrarProducto();
  sembrarExistencias();
  console.log(
    `[hu025-escala] arrancando: ${VUS} VUs sostenidos, calentamiento=${CALENTAMIENTO_SEG}s ` +
    `duracion=${DURACION_SEG}s enfriamiento=${ENFRIAMIENTO_SEG}s, prefijo=${PREFIJO_CLIENTE}`
  );
  return { inicio: Date.now() };
}

export default function () {
  const clienteId = `${PREFIJO_CLIENTE}-${__VU}-${__ITER}`;

  const tokenMedioPago = tokenizarTarjeta();
  if (!tokenMedioPago) {
    fallos.add(1);
    sleep(0.1);
    return;
  }

  const crearBody = JSON.stringify({
    clienteId,
    canal: 'WEB',
    metodoEntrega: 'DOMICILIO',
    moneda: 'GTQ',
    medioPago: 'TARJETA',
    tokenMedioPago,
    impuestos: 12.0,
    lineas: [
      {
        sku: SKU,
        descripcion: 'Producto de prueba de escalabilidad HU-025/EAC-04',
        cantidad: 1,
        precioUnitario: PRECIO_UNITARIO,
      },
    ],
  });

  const resCrear = http.post(`${ORDENES_URL}/api/ordenes`, crearBody, {
    ...JSON_HEADERS,
    tags: { paso: 'crear' },
  });

  if (!check(resCrear, { 'orden creada (201)': (r) => r.status === 201 })) {
    fallos.add(1);
    sleep(0.1);
    return;
  }
  ordenesCreadas.add(1);

  const ordenId = JSON.parse(resCrear.body).id;

  // Confirmar dispara la saga asincrona (reserva de inventario -> cobro),
  // que es lo que produce el volumen sostenido de eventos de outbox que
  // termina consumiendo auditoria-worker via su binding "#".
  const resConfirmar = http.post(
    `${ORDENES_URL}/api/ordenes/${ordenId}/confirmar`,
    JSON.stringify({ motivo: 'confirmacion automatica - prueba de escalabilidad HU-025/EAC-04' }),
    { ...JSON_HEADERS, tags: { paso: 'confirmar' } }
  );

  if (check(resConfirmar, { 'orden confirmada (200)': (r) => r.status === 200 })) {
    ordenesConfirmadas.add(1);
  } else {
    fallos.add(1);
  }

  sleep(0.05 + Math.random() * 0.1);
}

export function handleSummary(data) {
  const totalReqs = data.metrics['http_reqs'] ? data.metrics['http_reqs'].values.count : 0;
  const creadas = data.metrics['sgo_hu025_ordenes_creadas'] ? data.metrics['sgo_hu025_ordenes_creadas'].values.count : 0;
  const confirmadas = data.metrics['sgo_hu025_ordenes_confirmadas'] ? data.metrics['sgo_hu025_ordenes_confirmadas'].values.count : 0;
  const fallidas = data.metrics['sgo_hu025_fallos'] ? data.metrics['sgo_hu025_fallos'].values.count : 0;

  const texto = `
===============================================================================
 CARGA HU-025 / EAC-04 — ESCALABILIDAD HORIZONTAL
===============================================================================
 RUN_ID                 : ${RUN_ID}
 Prefijo cliente_id      : ${PREFIJO_CLIENTE}
 SKU sembrado            : ${SKU}
 Peticiones HTTP totales : ${totalReqs}
 Ordenes creadas         : ${creadas}
 Ordenes confirmadas     : ${confirmadas}
 Fallos                  : ${fallidas}

 Siguiente paso: espera ~60s a que drene el outbox y corre, anotando la hora
 de inicio de esta corrida como DESDE (UTC, ISO 8601):
   DESDE="<hora de inicio>" ./verificar.sh
===============================================================================
`;
  console.log(texto);

  return {
    'stdout': texto,
    '/scripts/hu025-resultado.json': JSON.stringify(data, null, 2),
    '/scripts/hu025-resultado.txt': texto,
  };
}