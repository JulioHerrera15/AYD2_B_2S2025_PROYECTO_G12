// EAC-01 / HU-023 — cero ordenes perdidas por indisponibilidad de PayKlik
// durante el pico de Black Friday (8x). Corre con k6 directo en el host
// (Windows o Linux), contra los puertos que publica docker-compose.yml. Desde la
// raiz de SGO-Logic:
//
//   k6 run -e ORDENES_POR_MINUTO=80 scripts/pruebas-carga/eac01-hu023-carga.js
//
// Las URLs por defecto son las del host: ordenes 8083, inventario 8094, pagos 8085
// (tokenizacion) y PayKlik 8084 (solo admin). Cada una se puede cambiar con -e ORDENES_URL,
// -e INVENTARIO_URL, -e PAGOS_URL y -e PAYKLIK_ADMIN_URL.
//
// Con k6 en contenedor, "localhost" es el propio contenedor: hay que apuntar al
// host con host.docker.internal (Docker Desktop) o unirlo a la red del compose y
// usar los nombres de servicio (`--network sgo-logic_default`, puerto 8080):
//
//   docker run --rm -v "${PWD}/scripts/pruebas-carga:/scripts" -e ORDENES_URL=http://host.docker.internal:8083 -e INVENTARIO_URL=http://host.docker.internal:8094 -e PAGOS_URL=http://host.docker.internal:8085 -e PAYKLIK_ADMIN_URL=http://host.docker.internal:8084 -e ORDENES_POR_MINUTO=80 grafana/k6 run /scripts/eac01-hu023-carga.js
//
// ---------------------------------------------------------------------------
// Volumen de la prueba (punto 1 del encargo)
// ---------------------------------------------------------------------------
// El DDA (EV-27, EAC-01, EAC-04, HU-023) fija el factor de escalamiento en
// "hasta ocho veces la demanda de un dia normal", pero en ningun punto del
// documento da ese "dia normal" en unidades absolutas (ordenes por minuto,
// por hora, etc.). No hay un numero oficial que usar tal cual.
//
// Ante esa ausencia, este script deriva uno defendible en vez de inventarlo
// a ciegas, apoyandose en la unica magnitud de escala que el DDA si declara:
// las 32 tiendas POS del canal fisico (EV-16).
//
//   - Piso conservador por tienda en la hora pico de un dia normal:
//     1 orden cada 3 minutos (~20 ordenes/hora/tienda). Se ignora a proposito
//     el canal digital para no inflar el piso.
//   - 32 tiendas x 20 ordenes/hora = 640 ordenes/hora ~= 10.7 ordenes/minuto
//     como piso de "dia normal".
//   - Aplicando el factor 8x declarado: ~85 ordenes/minuto de pico.
//   - Se redondea hacia abajo (conservador) a 80 ordenes/minuto
//     (~1.33 ordenes/seg) sostenidas durante toda la ventana de la prueba.
//
// Es una estimacion de ingenieria, no un dato del cliente: queda documentada
// aca y en el guion (documentacion/docs/PRUEBA-CARGA-EAC01.md) para que el
// equipo la reemplace si aparece una cifra oficial. Se puede ajustar sin
// tocar el script via la variable de entorno ORDENES_POR_MINUTO.
//
// ---------------------------------------------------------------------------
// Flujo por orden virtual (punto 3 del encargo)
// ---------------------------------------------------------------------------
// La saga ya integra Inventario (consumidor de eventos en Ordenes):
//
//   0) POST /api/pagos/tokens de Pagos      -> token de la tarjeta (por orden)
//   1) POST /api/ordenes                    -> CREADA
//   2) POST /api/ordenes/{id}/confirmar     -> RESERVA_SOLICITADA
//   3) Saga avanza sola:
//      - Ordenes emite ReservaInventarioSolicitada (outbox)
//      - Inventario reserva y emite InventarioReservado
//      - Ordenes consume InventarioReservado -> INVENTARIO_RESERVADO
//      - Ordenes emite CobroSolicitado (outbox)
//      - Pagos procesa el cobro contra PayKlik (ServicioCobro)
//
// La prueba EAC-01 no fuerza ninguna transicion: reserva y cobro los maneja la
// saga. El VU de k6 termina despues de confirmar y no espera ni el resultado
// del cobro ni la entrega: EAC-01 mide el cobro, y lo que pasa despues
// (despacho, LogiExpress) queda fuera de esta prueba. La secuencia asincrona
// via outbox es lo que EAC-01 exige validar. La verificacion de que el cobro se
// resolvio (o quedo auditado como fallido) se hace aparte, contra las bases,
// con el segundo script.
//
// ---------------------------------------------------------------------------
// Tres fases (punto 4 del encargo)
// ---------------------------------------------------------------------------
// La carga (ORDENES_POR_MINUTO) es constante durante toda la prueba; lo que
// cambia es el modo del mock de PayKlik, contra su endpoint de administracion
// PUT /admin/modo (ver Program.cs de SgoLogic.Mocks.PayKlik):
//
//   t = 0s                              -> PayKlik en modo NORMAL (calentamiento)
//   t = CALENTAMIENTO_SEG                -> PayKlik pasa a modo CAIDO
//   t = CALENTAMIENTO_SEG + CAIDA_SEG    -> PayKlik vuelve a modo NORMAL
//   fin = CALENTAMIENTO_SEG + CAIDA_SEG + RECUPERACION_SEG
//
// Los tres cambios de modo se agendan como escenarios propios de k6 con
// startTime exacto, para que quede clara la marca de tiempo de cada
// transicion en el resumen de la corrida.
//
// ---------------------------------------------------------------------------
// Tokenizacion de la tarjeta (ADR-010, DDA 14.3.1)
// ---------------------------------------------------------------------------
// Una orden con TARJETA exige tokenMedioPago. Desde la tokenizacion interna el
// token lo emite Pagos (POST /api/pagos/tokens) en su boveda transitoria, sin
// llamar a PayKlik: tokenizar sigue funcionando con PayKlik CAIDO. Por eso cada
// iteracion tokeniza en linea, como un cliente real en el checkout, y durante la
// caida la orden se crea igual y el cobro queda PENDIENTE con el token ACTIVO
// (vigencia de 30 minutos, cubre toda la corrida y el reintento).

import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';

// Puertos publicados en el host por docker-compose.yml (ordenes-api 8083,
// inventario-api 8094, pagos-api 8085, payklik-mock 8084). Es lo que k6 ve al
// correr fuera de Docker; con k6 en contenedor se sobreescriben con -e (ver el
// encabezado).
const ORDENES_URL = (__ENV.ORDENES_URL || 'http://localhost:8083').replace(/\/+$/, '');
const INVENTARIO_URL = (__ENV.INVENTARIO_URL || 'http://localhost:8094').replace(/\/+$/, '');
const PAGOS_URL = (__ENV.PAGOS_URL || 'http://localhost:8085').replace(/\/+$/, '');
const PAYKLIK_ADMIN_URL = (__ENV.PAYKLIK_ADMIN_URL || 'http://localhost:8084').replace(/\/+$/, '');

// SKU de la prueba y su precio: se registra en el catalogo de Inventario en
// setup(), porque Ordenes rechaza (422) una linea cuyo precio no coincide.
const SKU = 'SKU-CARGA-EAC01';
const PRECIO_UNITARIO = 100.0;

const ORDENES_POR_MINUTO = Number(__ENV.ORDENES_POR_MINUTO || 80);
const CALENTAMIENTO_SEG = Number(__ENV.CALENTAMIENTO_SEG || 90);
const CAIDA_SEG = Number(__ENV.CAIDA_SEG || 120);
const RECUPERACION_SEG = Number(__ENV.RECUPERACION_SEG || 90);

// Marca de tiempo de carga del script (compartida por todos los escenarios:
// el codigo de alcance de modulo corre una vez en el contexto init y k6 lo
// reusa para cada VU). Sirve solo para loguear el "t=" de cada transicion de
// modo; no participa en la logica de la prueba.
const INICIO_MS = Date.now();

// Identifica las ordenes de esta corrida para que el script de verificacion
// pueda filtrarlas sin tocar datos de otras pruebas o de la demo manual.
const RUN_ID = __ENV.RUN_ID || `${INICIO_MS}`;
export const PREFIJO_CLIENTE = `CARGA-EAC01-${RUN_ID}`;

const DURACION_TOTAL_SEG = CALENTAMIENTO_SEG + CAIDA_SEG + RECUPERACION_SEG;
const ORDENES_TOTALES = ORDENES_POR_MINUTO * Math.ceil(DURACION_TOTAL_SEG / 60);
const INVENTARIO_POR_ORDEN = Number(__ENV.INVENTARIO_POR_ORDEN || 5);
const STOCK_TOTAL = ORDENES_TOTALES * INVENTARIO_POR_ORDEN;

// La tasa se expresa por minuto (timeUnit 1m) para que ORDENES_POR_MINUTO se
// cumpla tal cual: redondearla a ordenes por segundo convertia 80/min en 60/min.
const TASA_POR_MINUTO = Math.max(1, ORDENES_POR_MINUTO);
const RAMPA_INICIAL_SEG = Math.min(30, CALENTAMIENTO_SEG);
const RESTO_CALENTAMIENTO_SEG = Math.max(1, CALENTAMIENTO_SEG - RAMPA_INICIAL_SEG);

const JSON_HEADERS = { headers: { 'Content-Type': 'application/json' } };

const ordenesCreadas = new Counter('sgo_ordenes_creadas');
const ordenesConCobroSolicitado = new Counter('sgo_ordenes_cobro_solicitado');
const fallosSincronos = new Counter('sgo_fallos_sincronos');
// Latencia de las peticiones sincronas de la orden (crear y confirmar), sin la
// tokenizacion previa: es la que se reporta como p95 y p99 de EAC-01.
const latenciaOrden = new Trend('sgo_latencia_orden_ms', true);

export const options = {
  setupTimeout: '60s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  scenarios: {
    // Fase unica de carga: la tasa no cambia entre calentamiento, caida y
    // recuperacion (solo cambia el modo de PayKlik, via los escenarios de
    // abajo). El primer stage es una rampa corta para no golpear la tasa
    // objetivo de un solo salto.
    carga_ordenes: {
      executor: 'ramping-arrival-rate',
      exec: 'flujoOrden',
      startRate: 0,
      timeUnit: '1m',
      preAllocatedVUs: 20,
      maxVUs: 60,
      stages: [
        { target: TASA_POR_MINUTO, duration: `${RAMPA_INICIAL_SEG}s` },
        { target: TASA_POR_MINUTO, duration: `${RESTO_CALENTAMIENTO_SEG}s` },
        { target: TASA_POR_MINUTO, duration: `${CAIDA_SEG}s` },
        { target: TASA_POR_MINUTO, duration: `${RECUPERACION_SEG}s` },
      ],
    },
    // Los tres cambios de modo del mock, agendados con startTime absoluto
    // sobre la misma linea de tiempo que carga_ordenes.
    payklik_normal_inicio: {
      executor: 'shared-iterations',
      exec: 'ponerModoPayKlik',
      vus: 1,
      iterations: 1,
      startTime: '0s',
      env: { SGO_MODO: 'NORMAL' },
    },
    payklik_caida: {
      executor: 'shared-iterations',
      exec: 'ponerModoPayKlik',
      vus: 1,
      iterations: 1,
      startTime: `${CALENTAMIENTO_SEG}s`,
      env: { SGO_MODO: 'CAIDO' },
    },
    payklik_recuperacion: {
      executor: 'shared-iterations',
      exec: 'ponerModoPayKlik',
      vus: 1,
      iterations: 1,
      startTime: `${CALENTAMIENTO_SEG + CAIDA_SEG}s`,
      env: { SGO_MODO: 'NORMAL' },
    },
  },
};

// Alta (o actualizacion) del SKU de la prueba en el catalogo de Inventario con el
// precio que mandan las ordenes.
function registrarProducto() {
  const body = JSON.stringify({
    nombre: 'Producto de prueba de carga EAC-01/HU-023',
    categoria: 'Pruebas',
    precio: PRECIO_UNITARIO,
    icono: 'inventory_2',
    activo: true,
  });
  const res = http.put(`${INVENTARIO_URL}/api/inventario/productos/${SKU}`, body, JSON_HEADERS);
  if (res.status !== 200 && res.status !== 201) {
    throw new Error(`Registro del producto ${SKU} fallo con status ${res.status}: ${res.body}`);
  }
  console.log(`[eac01-hu023] producto ${SKU} registrado a Q${PRECIO_UNITARIO}`);
}

function sembrarExistencias() {
  const body = JSON.stringify({
    sku: SKU,
    ubicacionId: 'CD-01',
    tipoUbicacion: 'CENTRO_DISTRIBUCION',
    cantidadFisica: STOCK_TOTAL,
  });
  const res = http.put(`${INVENTARIO_URL}/api/inventario/existencias`, body, JSON_HEADERS);
  if (res.status !== 200) {
    console.error(`[eac01-hu023] FALLO siembra de existencias: status=${res.status} body=${res.body}`);
    throw new Error(`Siembra de inventario fallo con status ${res.status}`);
  }
  console.log(`[eac01-hu023] existencias sembradas: ${SKU} x${STOCK_TOTAL} en CD-01`);
}

// Tarjeta de prueba (Luhn valido). Solo viaja de k6 a Pagos, que la cifra en su
// boveda; Ordenes recibe unicamente el token. Null si Pagos no la tokenizo.
function tokenizarTarjeta() {
  const body = JSON.stringify({
    numero: '4111111111111111',
    mesExp: 12,
    anioExp: new Date().getFullYear() + 3,
    cvv: '123',
    titular: 'Cliente Carga',
    guardar: false,
  });
  const res = http.post(`${PAGOS_URL}/api/pagos/tokens`, body, {
    ...JSON_HEADERS,
    tags: { paso: 'tokenizar' },
  });
  if (!check(res, { 'tarjeta tokenizada (201)': (r) => r.status === 201 })) {
    console.error(`[eac01-hu023] tokenizacion fallo: status=${res.status} body=${res.body}`);
    return null;
  }
  return JSON.parse(res.body).token;
}

export function setup() {
  registrarProducto();
  sembrarExistencias();
  console.log(
    `[eac01-hu023] arrancando: ${TASA_POR_MINUTO} ordenes/min, ` +
    `calentamiento=${CALENTAMIENTO_SEG}s caida=${CAIDA_SEG}s recuperacion=${RECUPERACION_SEG}s, ` +
    `stock=${STOCK_TOTAL} unidades, prefijo=${PREFIJO_CLIENTE}`
  );
  return { inicio: Date.now() };
}

export function ponerModoPayKlik() {
  const modo = __ENV.SGO_MODO;
  const segundoTranscurrido = Math.round((Date.now() - INICIO_MS) / 1000);

  const res = http.put(
    `${PAYKLIK_ADMIN_URL}/admin/modo`,
    JSON.stringify({ modo, latenciaMs: null, probabilidadFallo: null }),
    JSON_HEADERS
  );

  check(res, { [`PayKlik -> ${modo} (200)`]: (r) => r.status === 200 });
  console.log(`[eac01-hu023] t=${segundoTranscurrido}s PayKlik admin/modo -> ${modo} (status ${res.status})`);
}

export function flujoOrden(data) {
  const clienteId = `${PREFIJO_CLIENTE}-${__VU}-${__ITER}`;

  // Un token distinto por orden, como un cliente en el checkout. No depende de
  // PayKlik: tambien se emite durante la caida.
  const tokenMedioPago = tokenizarTarjeta();
  if (!tokenMedioPago) {
    fallosSincronos.add(1);
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
        descripcion: 'Producto de prueba de carga EAC-01/HU-023',
        cantidad: 1,
        precioUnitario: PRECIO_UNITARIO,
      },
    ],
  });

  const resCrear = http.post(`${ORDENES_URL}/api/ordenes`, crearBody, {
    ...JSON_HEADERS,
    tags: { paso: 'crear' },
  });

  latenciaOrden.add(resCrear.timings.duration, { paso: 'crear' });
  if (!check(resCrear, { 'orden creada (201)': (r) => r.status === 201 })) {
    fallosSincronos.add(1);
    return;
  }
  ordenesCreadas.add(1);

  const ordenId = JSON.parse(resCrear.body).id;

  const resConfirmar = http.post(
    `${ORDENES_URL}/api/ordenes/${ordenId}/confirmar`,
    JSON.stringify({ motivo: 'confirmacion automatica - prueba de carga EAC-01/HU-023' }),
    { ...JSON_HEADERS, tags: { paso: 'confirmar' } }
  );
  latenciaOrden.add(resConfirmar.timings.duration, { paso: 'confirmar' });
  if (!check(resConfirmar, { 'orden confirmada (200)': (r) => r.status === 200 })) {
    fallosSincronos.add(1);
    return;
  }

  // La saga avanza sola desde aqui: RESERVA_SOLICITADA -> INVENTARIO_RESERVADO
  // -> COBRO_SOLICITADO -> PAGO_APLICADO (o PAGO_PENDIENTE si PayKlik esta
  // caido) y, con pago aplicado, sigue con la certificacion fiscal y la solicitud
  // de despacho. El VU termina aqui: no fuerza ninguna transicion y no espera el
  // resultado del cobro ni la entrega.

  // La parte sincrona de la orden (crear + confirmar) salio bien: la saga ya
  // tiene el encargo de solicitar el cobro.
  ordenesConCobroSolicitado.add(1);
}

export function teardown(data) {
  const duracionSeg = Math.round((Date.now() - data.inicio) / 1000);
  console.log(
    `[eac01-hu023] fin de la generacion de carga tras ~${duracionSeg}s. ` +
    `Espera al menos 60s a que drene el pipeline asincrono (outbox + consumidor de Pagos) ` +
    `y corre eac01-hu023-verificar.sh con RUN_ID=${RUN_ID} (prefijo ${PREFIJO_CLIENTE}).`
  );
}
