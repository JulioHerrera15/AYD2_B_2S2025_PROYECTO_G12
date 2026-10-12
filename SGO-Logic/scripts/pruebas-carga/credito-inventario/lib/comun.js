// Libreria comun de las pruebas de carga y estres de la ruta de credito y su
// impacto en Inventario (Fase 3). La importan los diez scripts c1..c5 y e1..e5
// de esta carpeta; no se corre sola.
//
// Que resuelve aqui una sola vez:
//   - URLs de los servicios (por defecto, nombres de servicio de la red del
//     compose: k6 corre en contenedor con --network sgo-logic_default).
//   - Identidad de la corrida: RUN_ID, prefijo de clientes y SKU propio. Cada
//     corrida siembra SU PROPIO SKU, asi las cuentas de inventario del
//     verificador no se mezclan con otras pruebas ni con la demo.
//   - Clientes de credito sinteticos: se cargan sus historiales en Credito por
//     POST /interno/credito/historiales (el mismo cuerpo que el evento
//     HistorialClienteErpActualizado, ADR-023) y se firma su JWT con el
//     JWT_SECRET del entorno (ver la nota de "Por que se firma el JWT" abajo).
//   - Crear y confirmar ordenes por cualquiera de las dos rutas (CREDITO o
//     TARJETA) midiendo la latencia que ve el cliente en el checkout.
//
// Por que se firma el JWT en vez de usar /api/auth/login:
//   Ordenes y Credito solo validan la firma HS256 con Auth:JwtSecret y leen el
//   claim "email" (Program.cs de ambos servicios: ValidateIssuer/Audience en
//   false). Registrar cientos de usuarios reales exige el codigo de verificacion
//   que auth-api escribe en sus logs, lo que no escala a una prueba de carga. Lo
//   que se mide aqui es Ordenes -> Inventario -> Credito, no el login: el token
//   firmado con el mismo secreto es indistinguible para esos servicios. El login
//   real ya lo cubren credito-saga.ps1 y las pruebas E2E.

import http from 'k6/http';
import { check } from 'k6';
import crypto from 'k6/crypto';
import encoding from 'k6/encoding';
import { Counter, Trend, Rate } from 'k6/metrics';

// ---------------------------------------------------------------------------
// Configuracion
// ---------------------------------------------------------------------------

const limpiar = (url) => url.replace(/\/+$/, '');

export const URLS = {
  ordenes: limpiar(__ENV.ORDENES_URL || 'http://ordenes-api:8080'),
  inventario: limpiar(__ENV.INVENTARIO_URL || 'http://inventario-api:8080'),
  credito: limpiar(__ENV.CREDITO_URL || 'http://credito-api:8080'),
  pagos: limpiar(__ENV.PAGOS_URL || 'http://pagos-api:8080'),
  payklikAdmin: limpiar(__ENV.PAYKLIK_ADMIN_URL || 'http://payklik-mock:8080'),
};

const JWT_SECRET = __ENV.JWT_SECRET || '';

export const PRECIO_UNITARIO = 100.0;
export const UBICACION = 'CD-01';

// RUN_ID lo genera ejecutar.sh (p. ej. "c3-1011170512") y es lo que despues
// recibe verificar.sh. Solo minusculas, digitos y guiones: el clienteId de
// credito es un email y Credito lo normaliza a minusculas.
export const RUN_ID = (__ENV.RUN_ID || `manual-${Date.now()}`).toLowerCase().replace(/[^a-z0-9-]/g, '');
export const PREFIJO = `cci-${RUN_ID}`;
export const SKU = `SKU-CCI-${RUN_ID.toUpperCase()}`;

const JSON_HEADERS = { 'Content-Type': 'application/json' };

// ---------------------------------------------------------------------------
// Metricas comunes (todas las pruebas las reportan en el resumen de k6)
// ---------------------------------------------------------------------------

// Lo que espera el cliente en el checkout: crear + confirmar, por ruta.
export const latenciaCheckout = new Trend('sgo_latencia_checkout_ms', true);
export const latenciaCrear = new Trend('sgo_latencia_crear_ms', true);
export const latenciaConfirmar = new Trend('sgo_latencia_confirmar_ms', true);
export const ordenesEnviadas = new Counter('sgo_ordenes_enviadas');
export const ordenesConfirmadas = new Counter('sgo_ordenes_confirmadas');
export const fallosSincronos = new Counter('sgo_fallos_sincronos');
export const exitoCheckout = new Rate('sgo_exito_checkout');

// ---------------------------------------------------------------------------
// Perfiles de historial ERP (copias de los clientes demo con comportamiento
// conocido en credito-saga.ps1, para que el resultado sea predecible)
// ---------------------------------------------------------------------------

export const PERFILES = {
  // Igual a cliente1: el mejor pagador del seed. Aprueba compras normales.
  BUENO: {
    letra: 'b', dias: 1100, compras: 18, monto: 14500, puntuales: 18, tardios: 0,
    atraso: 0, devoluciones: 1, graves: 0, otros: 0,
  },
  // Igual a cliente3: nivel REGULAR, limite Q1 500 (escenario "cupo" del arnes).
  REGULAR: {
    letra: 'r', dias: 300, compras: 5, monto: 6000, puntuales: 4, tardios: 1,
    atraso: 12, devoluciones: 1, graves: 0, otros: 0,
  },
  // Igual a cliente6: un incidente grave es descarte directo de la politica
  // (MotivoDescarte -> INCIDENTE_GRAVE), sin importar el puntaje minimo del
  // entorno. Toda compra a credito de este perfil se rechaza.
  NO_ELEGIBLE: {
    letra: 'x', dias: 900, compras: 14, monto: 11000, puntuales: 14, tardios: 0,
    atraso: 0, devoluciones: 1, graves: 1, otros: 0,
  },
};

export function emailCliente(perfil, n) {
  return `${PREFIJO}-${perfil.letra}${n}@carga.sgo.local`;
}

// ---------------------------------------------------------------------------
// JWT HS256 con el secreto del entorno
// ---------------------------------------------------------------------------

export function firmarJwt(email) {
  if (!JWT_SECRET) {
    throw new Error('Falta JWT_SECRET (-e JWT_SECRET=...). ejecutar.sh lo lee de ordenes-api.');
  }
  const ahora = Math.floor(Date.now() / 1000);
  const cabecera = encoding.b64encode(JSON.stringify({ alg: 'HS256', typ: 'JWT' }), 'rawurl');
  const cuerpo = encoding.b64encode(JSON.stringify({
    sub: email,
    email,
    nombre: 'Cliente de carga',
    rol: 'CLIENTE',
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'CLIENTE',
    jti: `${email}-${ahora}`,
    iat: ahora,
    nbf: ahora - 60,
    exp: ahora + 4 * 3600,
  }), 'rawurl');
  const firma = crypto.hmac('sha256', JWT_SECRET, `${cabecera}.${cuerpo}`, 'base64rawurl');
  return `${cabecera}.${cuerpo}.${firma}`;
}

// ---------------------------------------------------------------------------
// Preparacion (se llama desde setup())
// ---------------------------------------------------------------------------

// Alta del SKU de la corrida en el catalogo (Ordenes valida el precio contra
// Inventario) y sus existencias en CD-01.
export function sembrarSku(cantidadFisica) {
  const producto = http.put(
    `${URLS.inventario}/api/inventario/productos/${SKU}`,
    JSON.stringify({
      nombre: `Producto de prueba credito-inventario ${RUN_ID}`,
      categoria: 'Pruebas',
      precio: PRECIO_UNITARIO,
      icono: 'inventory_2',
      activo: true,
    }),
    { headers: JSON_HEADERS, tags: { paso: 'setup' } },
  );
  if (producto.status !== 200 && producto.status !== 201) {
    throw new Error(`Alta del producto ${SKU} fallo: ${producto.status} ${producto.body}`);
  }

  const existencia = http.put(
    `${URLS.inventario}/api/inventario/existencias`,
    JSON.stringify({
      sku: SKU,
      ubicacionId: UBICACION,
      tipoUbicacion: 'CENTRO_DISTRIBUCION',
      cantidadFisica,
    }),
    { headers: JSON_HEADERS, tags: { paso: 'setup' } },
  );
  if (existencia.status !== 200 && existencia.status !== 201) {
    throw new Error(`Siembra de ${SKU} fallo: ${existencia.status} ${existencia.body}`);
  }
  console.log(`[${RUN_ID}] SKU ${SKU}: ${cantidadFisica} unidades en ${UBICACION} a Q${PRECIO_UNITARIO}`);
}

function cuerpoHistorial(email, perfil, indice) {
  const ahora = new Date();
  const desde = new Date(ahora.getTime() - perfil.dias * 86400000);
  return JSON.stringify({
    clienteId: email,
    codigoClienteErp: `ERP-CCI-${perfil.letra.toUpperCase()}${indice}`,
    clienteDesde: desde.toISOString(),
    comprasUltimos12Meses: perfil.compras,
    montoCompradoUltimos12Meses: perfil.monto,
    moneda: 'GTQ',
    pagosPuntuales: perfil.puntuales,
    pagosTardios: perfil.tardios,
    maxDiasAtraso: perfil.atraso,
    devoluciones: perfil.devoluciones,
    incidentesGraves: perfil.graves,
    otrosIncidentes: perfil.otros,
    fechaCorte: ahora.toISOString(),
    loteOrigen: `carga-${RUN_ID}`,
  });
}

// Crea `cantidad` clientes del perfil: historial en Credito + JWT firmado.
// Devuelve [{ email, token }]. Va en lotes para no tardar minutos en el setup.
export function crearClientes(perfil, cantidad) {
  const clientes = [];
  const LOTE = 20;
  for (let inicio = 0; inicio < cantidad; inicio += LOTE) {
    const peticiones = [];
    const emails = [];
    for (let i = inicio; i < Math.min(cantidad, inicio + LOTE); i++) {
      const email = emailCliente(perfil, i);
      emails.push(email);
      peticiones.push({
        method: 'POST',
        url: `${URLS.credito}/interno/credito/historiales`,
        body: cuerpoHistorial(email, perfil, i),
        params: { headers: JSON_HEADERS, tags: { paso: 'setup' } },
      });
    }
    const respuestas = http.batch(peticiones);
    respuestas.forEach((r, k) => {
      if (r.status !== 200) {
        throw new Error(
          `Historial de ${emails[k]} rechazado: ${r.status} ${r.body}. ` +
          '¿credito-api tiene Credito__EndpointsInternos=true (docker-compose.yml)?');
      }
      clientes.push({ email: emails[k], token: firmarJwt(emails[k]) });
    });
  }

  // Confirma con el endpoint real del checkout que el perfil quedo como se espera.
  const muestra = clientes[0];
  const oferta = http.get(`${URLS.credito}/api/credito/elegibilidad?monto=${PRECIO_UNITARIO}`, {
    headers: { Authorization: `Bearer ${muestra.token}` },
    tags: { paso: 'setup' },
  });
  console.log(`[${RUN_ID}] ${cantidad} clientes ${perfil.letra}: elegibilidad de ${muestra.email} -> ${oferta.status} ${oferta.body}`);
  if (oferta.status === 401) {
    throw new Error('Credito respondio 401 al JWT firmado: JWT_SECRET no coincide con el del stack.');
  }
  return clientes;
}

export function payKlikNormal() {
  const res = http.put(
    `${URLS.payklikAdmin}/admin/modo`,
    JSON.stringify({ modo: 'NORMAL', latenciaMs: null, probabilidadFallo: null }),
    { headers: JSON_HEADERS, tags: { paso: 'setup' } },
  );
  console.log(`[${RUN_ID}] PayKlik -> NORMAL (${res.status})`);
}

// ---------------------------------------------------------------------------
// Flujo de una orden (lo llama cada iteracion de los VUs)
// ---------------------------------------------------------------------------

function tokenizarTarjeta() {
  const res = http.post(
    `${URLS.pagos}/api/pagos/tokens`,
    JSON.stringify({
      numero: '4111111111111111',
      mesExp: 12,
      anioExp: new Date().getFullYear() + 3,
      cvv: '123',
      titular: 'Cliente Carga',
      guardar: false,
    }),
    { headers: JSON_HEADERS, tags: { paso: 'tokenizar' } },
  );
  if (res.status !== 201) {
    return null;
  }
  return JSON.parse(res.body).token;
}

function cuerpoOrden(clienteId, medioPago, cantidad, tokenMedioPago) {
  const cuerpo = {
    clienteId,
    canal: 'WEB',
    metodoEntrega: 'DOMICILIO',
    moneda: 'GTQ',
    medioPago,
    impuestos: 0.0,
    direccionEntrega: {
      pais: 'GT',
      departamento: 'Guatemala',
      municipio: 'Guatemala',
      direccion: '5a avenida 3-21 zona 2',
      referencia: `Prueba ${RUN_ID}`,
    },
    lineas: [{
      sku: SKU,
      descripcion: 'Producto de prueba credito-inventario',
      cantidad,
      precioUnitario: PRECIO_UNITARIO,
    }],
  };
  if (tokenMedioPago) {
    cuerpo.tokenMedioPago = tokenMedioPago;
  }
  return JSON.stringify(cuerpo);
}

// Crea y confirma una orden. `cliente` es { email, token } para CREDITO; para
// TARJETA basta con { email } (el clienteId de un invitado). Devuelve el id de la
// orden o null. Desde confirmar la saga sigue sola: reserva -> credito o cobro.
export function comprar(cliente, medioPago, cantidad = 1, tagsExtra = {}) {
  const ruta = medioPago === 'CREDITO' ? 'credito' : 'tarjeta';
  const tags = Object.assign({ ruta }, tagsExtra);
  const cabeceras = Object.assign({}, JSON_HEADERS);
  let tokenMedioPago = null;

  if (medioPago === 'CREDITO') {
    cabeceras.Authorization = `Bearer ${cliente.token}`;
  } else {
    tokenMedioPago = tokenizarTarjeta();
    if (!tokenMedioPago) {
      fallosSincronos.add(1, tags);
      exitoCheckout.add(false, tags);
      return null;
    }
  }

  ordenesEnviadas.add(1, tags);
  const crear = http.post(
    `${URLS.ordenes}/api/ordenes`,
    cuerpoOrden(cliente.email, medioPago, cantidad, tokenMedioPago),
    { headers: cabeceras, tags: Object.assign({ paso: 'crear' }, tags) },
  );
  latenciaCrear.add(crear.timings.duration, tags);
  if (!check(crear, { [`orden ${ruta} creada (201)`]: (r) => r.status === 201 })) {
    console.warn(`[${RUN_ID}] crear ${ruta} -> ${crear.status} ${String(crear.body).slice(0, 200)}`);
    fallosSincronos.add(1, tags);
    exitoCheckout.add(false, tags);
    return null;
  }
  const ordenId = JSON.parse(crear.body).id;

  const confirmar = http.post(
    `${URLS.ordenes}/api/ordenes/${ordenId}/confirmar`,
    JSON.stringify({ motivo: `prueba ${RUN_ID}` }),
    { headers: cabeceras, tags: Object.assign({ paso: 'confirmar' }, tags) },
  );
  latenciaConfirmar.add(confirmar.timings.duration, tags);
  latenciaCheckout.add(crear.timings.duration + confirmar.timings.duration, tags);
  const ok = check(confirmar, { [`orden ${ruta} confirmada (200)`]: (r) => r.status === 200 });
  exitoCheckout.add(ok, tags);
  if (!ok) {
    console.warn(`[${RUN_ID}] confirmar ${ruta} -> ${confirmar.status} ${String(confirmar.body).slice(0, 200)}`);
    fallosSincronos.add(1, tags);
    return null;
  }
  ordenesConfirmadas.add(1, tags);
  return ordenId;
}

// clienteId de un comprador con tarjeta (invitado): unico por iteracion y con el
// prefijo de la corrida para que el verificador lo encuentre.
export function clienteTarjeta(indice) {
  return { email: `${PREFIJO}-t${indice}@carga.sgo.local` };
}

// Opciones que comparten todas las pruebas.
export const OPCIONES_BASE = {
  setupTimeout: '180s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  // Las peticiones del setup no cuentan para los umbrales.
  thresholds: {},
};

export function resumenFinal(nombre, extra = '') {
  console.log(
    `[${RUN_ID}] ${nombre}: fin de la generacion de carga. La saga sigue en curso: ` +
    `espera el drenado y corre verificar.sh con RUN_ID=${RUN_ID} (ejecutar.sh lo hace solo). ${extra}`);
}
