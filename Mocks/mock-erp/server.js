/**
 * MOCK ERP-Core (AS/400) — SGO-Logic / MegaStore
 * ------------------------------------------------
 * Simula el comportamiento del ERP legacy descrito en el DDA:
 *   - EV-13/EV-14: plataforma AS/400 que no se reemplaza ni modifica.
 *   - EV-15/RES-01/RES-02: solo recibe resúmenes de venta por lote CSV/XML,
 *     procesables cada 30 minutos (aquí el mock acepta el lote en cualquier
 *     momento; la cadencia de 30 min la impone el worker que lo llama, no el ERP).
 *   - ADR-007: el mock NO conoce el modelo de dominio nuevo; solo entiende
 *     "lotes" con control por número de secuencia y totales de control.
 *   - ADR-008: control por lote (secuencia + totales) y soporte para el
 *     proceso de reconciliación diaria (HU-013 / HU-022).
 */

const express = require("express");
const { XMLParser } = require("fast-xml-parser");

const app = express();
const PORT = process.env.PORT || 4000;

// Estado interno del mock (equivalente, para pruebas, a "lo que el AS/400 ya cargó")
const clientesHistorialBase = [
  {
    codigoCliente: "ERP-CLI-0001",
    email: "cliente1@sgo.local",
    clienteDesde: "2023-09-28T00:00:00.000Z",
    comprasUltimos12Meses: 18,
    montoCompradoUltimos12Meses: 14500,
    moneda: "GTQ",
    pagosPuntuales: 18,
    pagosTardios: 0,
    maxDiasAtraso: 0,
    devoluciones: 1,
    incidentesGraves: 0,
    otrosIncidentes: 0,
    fechaCorte: "2026-10-02T06:00:00.000Z",
    actualizadoEn: "2026-10-02T06:00:00.000Z",
    loteOrigen: "LOTE-HIST-CLI-0001"
  },
  {
    codigoCliente: "ERP-CLI-0002",
    email: "cliente2@sgo.local",
    clienteDesde: "2024-09-20T00:00:00.000Z",
    comprasUltimos12Meses: 9,
    montoCompradoUltimos12Meses: 8400,
    moneda: "GTQ",
    pagosPuntuales: 8,
    pagosTardios: 1,
    maxDiasAtraso: 6,
    devoluciones: 1,
    incidentesGraves: 0,
    otrosIncidentes: 0,
    fechaCorte: "2026-10-02T06:00:00.000Z",
    actualizadoEn: "2026-10-02T06:00:00.000Z",
    loteOrigen: "LOTE-HIST-CLI-0002"
  },
  {
    codigoCliente: "ERP-CLI-0003",
    email: "cliente3@sgo.local",
    clienteDesde: "2025-01-10T00:00:00.000Z",
    comprasUltimos12Meses: 5,
    montoCompradoUltimos12Meses: 6000,
    moneda: "GTQ",
    pagosPuntuales: 4,
    pagosTardios: 1,
    maxDiasAtraso: 12,
    devoluciones: 1,
    incidentesGraves: 0,
    otrosIncidentes: 0,
    fechaCorte: "2026-10-02T06:00:00.000Z",
    actualizadoEn: "2026-10-02T06:00:00.000Z",
    loteOrigen: "LOTE-HIST-CLI-0003"
  },
  {
    codigoCliente: "ERP-CLI-0004",
    email: "cliente4@sgo.local",
    clienteDesde: "2023-12-15T00:00:00.000Z",
    comprasUltimos12Meses: 10,
    montoCompradoUltimos12Meses: 9000,
    moneda: "GTQ",
    pagosPuntuales: 3,
    pagosTardios: 7,
    maxDiasAtraso: 45,
    devoluciones: 2,
    incidentesGraves: 0,
    otrosIncidentes: 1,
    fechaCorte: "2026-10-02T06:00:00.000Z",
    actualizadoEn: "2026-10-02T06:00:00.000Z",
    loteOrigen: "LOTE-HIST-CLI-0004"
  },
  {
    codigoCliente: "ERP-CLI-0005",
    email: "cliente5@sgo.local",
    clienteDesde: "2026-09-19T00:00:00.000Z",
    comprasUltimos12Meses: 2,
    montoCompradoUltimos12Meses: 900,
    moneda: "GTQ",
    pagosPuntuales: 2,
    pagosTardios: 0,
    maxDiasAtraso: 0,
    devoluciones: 0,
    incidentesGraves: 0,
    otrosIncidentes: 0,
    fechaCorte: "2026-10-02T06:00:00.000Z",
    actualizadoEn: "2026-10-02T06:00:00.000Z",
    loteOrigen: "LOTE-HIST-CLI-0005"
  },
  {
    codigoCliente: "ERP-CLI-0006",
    email: "cliente6@sgo.local",
    clienteDesde: "2023-10-10T00:00:00.000Z",
    comprasUltimos12Meses: 14,
    montoCompradoUltimos12Meses: 11000,
    moneda: "GTQ",
    pagosPuntuales: 14,
    pagosTardios: 0,
    maxDiasAtraso: 0,
    devoluciones: 1,
    incidentesGraves: 1,
    otrosIncidentes: 0,
    fechaCorte: "2026-10-02T06:00:00.000Z",
    actualizadoEn: "2026-10-02T06:00:00.000Z",
    loteOrigen: "LOTE-HIST-CLI-0006"
  }
];

let estadoERP = {
  ultimaSecuenciaAceptada: 0,
  lotesProcesados: {}, // loteId -> confirmación (ACEPTADO o RECHAZADO_CUADRE)
  pagosCreditoProcesados: {}, // eventId -> pago aplicado al historial
  bitacora: [],        // historial simple de todas las solicitudes recibidas
  clientesHistorial: clientesHistorialBase.map((c) => ({ ...c })),
  modoCaida: {
    activo: false,
    tipo: null,       // 'timeout' | 'error500' | 'no_disponible'
    expiraEn: null,   // timestamp ms; null = indefinido hasta que se restaure manualmente
  },
};

function registrarBitacora(entrada) {
  estadoERP.bitacora.unshift({ ...entrada, ts: new Date().toISOString() });
  estadoERP.bitacora = estadoERP.bitacora.slice(0, 200); // limitar memoria
}

function round2(n) {
  return Math.round((Number(n) + Number.EPSILON) * 100) / 100;
}

function caidaActiva() {
  if (!estadoERP.modoCaida.activo) return false;
  if (estadoERP.modoCaida.expiraEn && Date.now() > estadoERP.modoCaida.expiraEn) {
    estadoERP.modoCaida = { activo: false, tipo: null, expiraEn: null };
    return false;
  }
  return true;
}

// Middlewares de parseo: el ERP real solo entiende CSV o XML.
// Se admite application/json como atajo para pruebas automatizadas.
app.use(express.json({ limit: "5mb" }));
app.use(express.text({ type: ["text/csv", "application/xml", "text/xml"], limit: "5mb" }));

/**
 * Formato CSV esperado (dos encabezados: metadatos del lote + columnas de registros):
 *
 * LOTE,SECUENCIA,FECHA,CANTIDAD_REGISTROS,MONTO_TOTAL
 * LOTE-2026-09-10-001,42,2026-09-10T14:00:00Z,3,960.00
 * ORDEN,CANAL,TIENDA,MONTO,FECHA,CLIENTE_ID
 * ORD-1001,web,T01,320.00,2026-09-10T13:45:00Z
 * ORD-1002,tienda,T02,320.00,2026-09-10T13:50:00Z
 */
function parseCSV(texto) {
  const lineas = texto
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  if (lineas.length < 3) {
    throw new Error("CSV incompleto: se esperaban al menos metadatos, encabezado de registros y un registro.");
  }

  const metaValores = lineas[0].split(",").map((v) => v.trim());
  const [loteId, secuenciaStr, fechaGeneracion, cantidadStr, montoStr] = metaValores;

  // lineas[1] es el encabezado de columnas de registros y se omite su contenido literal
  const registros = lineas.slice(2).map((linea) => {
    const [ordenId, canal, tienda, monto, fecha, clienteId] = linea.split(",").map((v) => v.trim());
    return { ordenId, canal, tienda, monto: Number(monto), fecha, clienteId: clienteId || null };
  });

  return {
    loteId,
    secuencia: Number(secuenciaStr),
    fechaGeneracion,
    totalesControl: {
      cantidadRegistros: Number(cantidadStr),
      montoTotal: Number(montoStr),
    },
    registros,
  };
}

/**
 * Formato XML:
 *
 * <Lote id="LOTE-2026-09-10-001" secuencia="42" fecha="2026-09-10T14:00:00Z">
 *   <TotalesControl cantidadRegistros="3" montoTotal="960.00"/>
 *   <Registros>
 *     <Orden id="ORD-1001" canal="web" tienda="T01" monto="320.00" fecha="2026-09-10T13:45:00Z"/>
 *     <Orden id="ORD-1002" canal="tienda" tienda="T02" monto="320.00" fecha="2026-09-10T13:50:00Z"/>
 *     <Orden id="ORD-1003" canal="app" tienda="T01" monto="320.00" fecha="2026-09-10T13:55:00Z"/>
 *   </Registros>
 * </Lote>
 */
function parseXML(texto) {
  const parser = new XMLParser({ ignoreAttributes: false, attributeNamePrefix: "" });
  const doc = parser.parse(texto);
  const lote = doc.Lote;
  if (!lote) throw new Error("XML inválido: no se encontró el nodo <Lote>.");

  let ordenes = lote.Registros?.Orden ?? [];
  if (!Array.isArray(ordenes)) ordenes = [ordenes]; // fast-xml-parser no arma array si hay un solo hijo

  const registros = ordenes.map((o) => ({
    ordenId: o.id,
    canal: o.canal,
    tienda: o.tienda,
    monto: Number(o.monto),
    fecha: o.fecha,
    clienteId: o.clienteId || null,
  }));

  return {
    loteId: lote.id,
    secuencia: Number(lote.secuencia),
    fechaGeneracion: lote.fecha,
    totalesControl: {
      cantidadRegistros: Number(lote.Registros ? lote.TotalesControl.cantidadRegistros : 0) || Number(lote.TotalesControl.cantidadRegistros),
      montoTotal: Number(lote.TotalesControl.montoTotal),
    },
    registros,
  };
}

function interpretarCuerpo(req) {
  const contentType = (req.headers["content-type"] || "").toLowerCase();
  if (contentType.includes("application/json")) return req.body;
  if (contentType.includes("csv")) return parseCSV(req.body);
  if (contentType.includes("xml")) return parseXML(req.body);
  // Por defecto, intenta JSON (útil si el cliente no manda content-type explícito)
  if (typeof req.body === "object") return req.body;
  throw new Error(`Content-Type no soportado: ${contentType || "(vacío)"}. Use application/json, text/csv o application/xml.`);
}

function aplicarVentasAlHistorial(registros, loteId) {
  const ventasPorCliente = new Map();
  for (const registro of registros) {
    const email = String(registro.clienteId || "").trim().toLowerCase();
    const monto = Number(registro.monto || 0);
    if (!email || monto <= 0) continue;

    const venta = ventasPorCliente.get(email) || { cantidad: 0, monto: 0 };
    venta.cantidad += 1;
    venta.monto += monto;
    ventasPorCliente.set(email, venta);
  }

  const actualizadoEn = new Date().toISOString();
  for (const [email, ventas] of ventasPorCliente) {
    const historial = estadoERP.clientesHistorial.find(
      (cliente) => cliente.email.trim().toLowerCase() === email
    );
    if (!historial) continue;

    historial.comprasUltimos12Meses += ventas.cantidad;
    historial.montoCompradoUltimos12Meses = round2(
      historial.montoCompradoUltimos12Meses + ventas.monto
    );
    historial.actualizadoEn = actualizadoEn;
    historial.fechaCorte = actualizadoEn;
    historial.loteOrigen = loteId;
  }
}

// POST /erp/lotes  — Recepción de lote (equivalente a la transferencia CSV/XML)
app.post("/erp/lotes", (req, res) => {
  let lote;
  try {
    lote = interpretarCuerpo(req);
  } catch (err) {
    registrarBitacora({ evento: "LOTE_ILEGIBLE", detalle: err.message });
    return res.status(400).json({ estado: "FORMATO_INVALIDO", mensaje: err.message });
  }

  if (!lote.loteId || !lote.secuencia || !lote.totalesControl || !Array.isArray(lote.registros)) {
    return res.status(400).json({
      estado: "FORMATO_INVALIDO",
      mensaje: "El lote debe incluir loteId, secuencia, totalesControl{cantidadRegistros, montoTotal} y registros[].",
    });
  }

  // --- 1. Caída provocada: simula al AS/400 fuera de línea o degradado -----
  if (caidaActiva()) {
    const tipo = estadoERP.modoCaida.tipo;
    registrarBitacora({ evento: "CAIDA_SIMULADA", tipo, loteId: lote.loteId, secuencia: lote.secuencia });

    if (tipo === "timeout") {
      // No se responde dentro de un tiempo razonable: el cliente (worker ERP)
      // debe agotar su propio timeout y tratarlo como falla (ADR-008/ADR-009 en espíritu).
      return setTimeout(() => {
        if (!res.headersSent) {
          res.status(504).json({ estado: "TIMEOUT", mensaje: "El AS/400 no respondió a tiempo (simulado)." });
        }
      }, 8000);
    }
    if (tipo === "error500") {
      return res.status(500).json({ estado: "ERROR_INTERNO", mensaje: "Falla interna simulada del AS/400." });
    }
    // 'no_disponible' por defecto
    return res.status(503).json({ estado: "NO_DISPONIBLE", mensaje: "AS/400 en ventana de mantenimiento (simulado)." });
  }

  // --- 2. Idempotencia / control de secuencia --------------------
  if (estadoERP.lotesProcesados[lote.loteId]) {
    const previa = estadoERP.lotesProcesados[lote.loteId];
    registrarBitacora({ evento: "LOTE_DUPLICADO", loteId: lote.loteId });
    return res.status(409).json({
      estado: "DUPLICADO",
      mensaje: `El lote ${lote.loteId} ya fue procesado anteriormente; se ignora el reenvío.`,
      confirmacionPrevia: previa,
    });
  }
  if (lote.secuencia <= estadoERP.ultimaSecuenciaAceptada) {
    registrarBitacora({ evento: "SECUENCIA_INVALIDA", loteId: lote.loteId, secuencia: lote.secuencia });
    return res.status(409).json({
      estado: "SECUENCIA_INVALIDA",
      mensaje: `Secuencia ${lote.secuencia} ya fue superada. Última secuencia aceptada: ${estadoERP.ultimaSecuenciaAceptada}.`,
    });
  }

  // --- 3. Cuadre de totales --------------------------------------------------
  const cantidadCalculada = lote.registros.length;
  const montoCalculado = round2(lote.registros.reduce((acc, r) => acc + Number(r.monto || 0), 0));
  const discrepancias = [];

  if (cantidadCalculada !== Number(lote.totalesControl.cantidadRegistros)) {
    discrepancias.push(
      `Cantidad de registros declarada (${lote.totalesControl.cantidadRegistros}) no coincide con la recibida (${cantidadCalculada}).`
    );
  }
  if (Math.abs(montoCalculado - Number(lote.totalesControl.montoTotal)) > 0.01) {
    discrepancias.push(
      `Monto total declarado (${lote.totalesControl.montoTotal}) no coincide con la suma de registros (${montoCalculado.toFixed(2)}).`
    );
  }

  if (discrepancias.length > 0) {
    const confirmacionRechazo = {
      loteId: lote.loteId,
      secuencia: lote.secuencia,
      estado: "RECHAZADO_CUADRE",
      cantidadRegistrosRecibidos: cantidadCalculada,
      montoTotalCalculado: montoCalculado,
      discrepancias,
      procesadoEn: new Date().toISOString(),
    };
    // Nota: No se avanza ultimaSecuenciaAceptada ni se marca como procesado.
    // El worker de Integración ERP debe corregir y reenviar el mismo número de secuencia.
    registrarBitacora({ evento: "LOTE_RECHAZADO_CUADRE", loteId: lote.loteId, discrepancias });
    return res.status(422).json(confirmacionRechazo);
  }

  // --- 4. Aceptación y "carga" al ERP ----------------------------------------
  estadoERP.ultimaSecuenciaAceptada = lote.secuencia;
  const confirmacion = {
    loteId: lote.loteId,
    secuencia: lote.secuencia,
    estado: "ACEPTADO",
    cantidadRegistrosProcesados: cantidadCalculada,
    montoTotalProcesado: montoCalculado,
    discrepancias: [],
    procesadoEn: new Date().toISOString(),
  };
  estadoERP.lotesProcesados[lote.loteId] = confirmacion;
  aplicarVentasAlHistorial(lote.registros, lote.loteId);
  registrarBitacora({ evento: "LOTE_ACEPTADO", loteId: lote.loteId, secuencia: lote.secuencia, montoCalculado });

  return res.status(200).json(confirmacion);
});

app.post("/erp/clientes/pagos", (req, res) => {
  const pagos = req.body?.pagos;
  if (!Array.isArray(pagos) || pagos.length === 0) {
    return res.status(400).json({ estado: "FORMATO_INVALIDO", mensaje: "Se esperaba un arreglo no vacio en pagos[]." });
  }

  const normalizados = pagos.map((pago) => ({
    eventId: String(pago?.eventId || "").trim(),
    clienteId: String(pago?.clienteId || "").trim().toLowerCase(),
    cuentaId: String(pago?.cuentaId || "").trim(),
    ordenId: String(pago?.ordenId || "").trim(),
    pagadaEn: pago?.pagadaEn,
    puntual: pago?.puntual,
  }));
  const ids = new Set(normalizados.map((pago) => pago.eventId));
  if (normalizados.some((pago) => !pago.eventId || !pago.clienteId || !pago.cuentaId || !pago.ordenId
      || !Number.isFinite(Date.parse(pago.pagadaEn)) || typeof pago.puntual !== "boolean")
      || ids.size !== normalizados.length) {
    return res.status(400).json({ estado: "FORMATO_INVALIDO", mensaje: "Cada pago requiere eventId, cuentaId, ordenId, clienteId, pagadaEn y puntual booleano, sin eventId repetidos." });
  }

  if (caidaActiva()) {
    const tipo = estadoERP.modoCaida.tipo;
    registrarBitacora({ evento: "PAGOS_CREDITO_CAIDA_SIMULADA", tipo, cantidad: pagos.length });
    if (tipo === "timeout") {
      return setTimeout(() => res.status(504).json({ estado: "TIMEOUT", mensaje: "ERP no respondió pagos de crédito a tiempo." }), 8000);
    }
    if (tipo === "error500") {
      return res.status(500).json({ estado: "ERROR_INTERNO", mensaje: "Falla interna al actualizar pagos de crédito." });
    }
    return res.status(503).json({ estado: "NO_DISPONIBLE", mensaje: "ERP no disponible para pagos de crédito." });
  }

  const clientes = new Map(estadoERP.clientesHistorial.map((cliente) => [cliente.email.trim().toLowerCase(), cliente]));
  const desconocido = normalizados.find((pago) => !clientes.has(pago.clienteId));
  if (desconocido) {
    return res.status(404).json({ estado: "CLIENTE_NO_ENCONTRADO", clienteId: desconocido.clienteId });
  }

  let aplicados = 0;
  let duplicados = 0;
  const actualizadoEn = new Date().toISOString();
  for (const pago of normalizados) {
    if (estadoERP.pagosCreditoProcesados[pago.eventId]) {
      duplicados++;
      continue;
    }

    const historial = clientes.get(pago.clienteId);
    if (pago.puntual) historial.pagosPuntuales++;
    else historial.pagosTardios++;
    historial.actualizadoEn = actualizadoEn;
    historial.fechaCorte = actualizadoEn;
    historial.loteOrigen = `PAGO-CREDITO-${pago.eventId}`;
    estadoERP.pagosCreditoProcesados[pago.eventId] = {
      clienteId: pago.clienteId,
      cuentaId: pago.cuentaId,
      ordenId: pago.ordenId,
      pagadaEn: pago.pagadaEn,
      puntual: pago.puntual,
      aplicadoEn: actualizadoEn,
    };
    aplicados++;
  }

  registrarBitacora({ evento: "PAGOS_CREDITO_APLICADOS", aplicados, duplicados });
  return res.json({ estado: aplicados > 0 ? "ACEPTADO" : "DUPLICADO", aplicados, duplicados, actualizadoEn });
});

// Consultas de apoyo a la reconciliación
app.get("/erp/lotes/:loteId", (req, res) => {
  const confirmacion = estadoERP.lotesProcesados[req.params.loteId];
  if (!confirmacion) {
    return res.status(404).json({ estado: "NO_ENCONTRADO", mensaje: `No existe confirmación para el lote ${req.params.loteId}.` });
  }
  res.json(confirmacion);
});

app.get("/erp/lotes", (_req, res) => {
  res.json({
    ultimaSecuenciaAceptada: estadoERP.ultimaSecuenciaAceptada,
    totalLotesAceptados: Object.values(estadoERP.lotesProcesados).filter((l) => l.estado === "ACEPTADO").length,
    lotes: Object.values(estadoERP.lotesProcesados),
  });
});

app.post("/erp/clientes", (req, res) => {
  const email = String(req.body?.email || "").trim().toLowerCase();
  const clienteDesde = new Date(req.body?.clienteDesde);
  const eventId = String(req.body?.eventId || "").trim();
  if (!email || !email.includes("@") || !eventId || Number.isNaN(clienteDesde.getTime())) {
    return res.status(400).json({ estado: "FORMATO_INVALIDO", mensaje: "Se requiere email, clienteDesde y eventId validos." });
  }

  const existente = estadoERP.clientesHistorial.find(
    (cliente) => cliente.email.trim().toLowerCase() === email
  );
  if (existente) {
    return res.json({ estado: "DUPLICADO", email, cliente: existente });
  }

  const actualizadoEn = new Date().toISOString();
  const cliente = {
    codigoCliente: `ERP-CLI-${Date.now()}`,
    email,
    clienteDesde: clienteDesde.toISOString(),
    comprasUltimos12Meses: 0,
    montoCompradoUltimos12Meses: 0,
    moneda: "GTQ",
    pagosPuntuales: 0,
    pagosTardios: 0,
    maxDiasAtraso: 0,
    devoluciones: 0,
    incidentesGraves: 0,
    otrosIncidentes: 0,
    fechaCorte: actualizadoEn,
    actualizadoEn,
    loteOrigen: `ALTA-CLIENTE-${eventId}`,
  };

  estadoERP.clientesHistorial.push(cliente);
  registrarBitacora({ evento: "CLIENTE_VERIFICADO_REGISTRADO", email, eventId });
  return res.status(201).json({ estado: "CREADO", email, cliente });
});

app.get("/erp/clientes/historial", (req, res) => {
  const { modificadosDesde } = req.query;

  if (caidaActiva()) {
    const tipo = estadoERP.modoCaida.tipo;
    registrarBitacora({ evento: "HISTORIAL_CAIDA_SIMULADA", tipo, modificadosDesde });

    if (tipo === "timeout") return setTimeout(() => res.status(504).json({ estado: "TIMEOUT", mensaje: "ERP no respondió historial a tiempo." }), 8000);
    if (tipo === "error500") return res.status(500).json({ estado: "ERROR_INTERNO", mensaje: "Falla interna al consultar historial crediticio." });
    return res.status(503).json({ estado: "NO_DISPONIBLE", mensaje: "ERP no disponible para historial crediticio." });
  }

  const fechaDesde = modificadosDesde ? new Date(modificadosDesde) : null;
  const clientes = estadoERP.clientesHistorial.filter((cliente) => {
    if (!fechaDesde) return true;
    const fechaActualizado = new Date(cliente.actualizadoEn);
    return fechaActualizado >= fechaDesde;
  });

  res.json({
    fechaCorte: clientes[0]?.fechaCorte || new Date().toISOString(),
    total: clientes.length,
    clientes: clientes.map((cliente) => ({
      codigoCliente: cliente.codigoCliente,
      email: cliente.email,
      clienteDesde: cliente.clienteDesde,
      comprasUltimos12Meses: cliente.comprasUltimos12Meses,
      montoCompradoUltimos12Meses: cliente.montoCompradoUltimos12Meses,
      moneda: cliente.moneda,
      pagosPuntuales: cliente.pagosPuntuales,
      pagosTardios: cliente.pagosTardios,
      maxDiasAtraso: cliente.maxDiasAtraso,
      devoluciones: cliente.devoluciones,
      incidentesGraves: cliente.incidentesGraves,
      otrosIncidentes: cliente.otrosIncidentes,
      actualizadoEn: cliente.actualizadoEn,
      fechaCorte: cliente.fechaCorte,
      loteOrigen: cliente.loteOrigen,
    }))
  });
});

// Panel de administración del mock (solo para pruebas, no existe en el AS/400 real)
app.get("/erp/admin/estado", (_req, res) => {
  res.json({
    ultimaSecuenciaAceptada: estadoERP.ultimaSecuenciaAceptada,
    modoCaida: estadoERP.modoCaida,
    lotesProcesados: Object.keys(estadoERP.lotesProcesados).length,
    ultimosEventos: estadoERP.bitacora.slice(0, 10),
  });
});

app.post("/erp/admin/caida", express.json(), (req, res) => {
  const { activar = true, tipo = "no_disponible", duracionMs } = req.body || {};
  if (!["timeout", "error500", "no_disponible"].includes(tipo)) {
    return res.status(400).json({ mensaje: "tipo debe ser 'timeout', 'error500' o 'no_disponible'." });
  }
  estadoERP.modoCaida = {
    activo: Boolean(activar),
    tipo: activar ? tipo : null,
    expiraEn: activar && duracionMs ? Date.now() + Number(duracionMs) : null,
  };
  registrarBitacora({ evento: activar ? "CAIDA_ACTIVADA" : "CAIDA_DESACTIVADA", tipo, duracionMs });
  res.json({ mensaje: "Estado de caída actualizado.", modoCaida: estadoERP.modoCaida });
});

app.put("/erp/admin/clientes/:email/historial", express.json(), (req, res) => {
  const { email } = req.params;
  const payload = req.body || {};
  if (!payload || typeof payload !== "object") {
    return res.status(400).json({ estado: "FORMATO_INVALIDO", mensaje: "Se esperaba un JSON con el historial del cliente." });
  }

  const clave = (email || "").trim().toLowerCase();
  const registro = {
    codigoCliente: payload.codigoCliente || `ERP-CLI-${Date.now()}`,
    email: clave,
    clienteDesde: payload.clienteDesde || new Date().toISOString(),
    comprasUltimos12Meses: Number(payload.comprasUltimos12Meses || 0),
    montoCompradoUltimos12Meses: Number(payload.montoCompradoUltimos12Meses || 0),
    moneda: payload.moneda || "GTQ",
    pagosPuntuales: Number(payload.pagosPuntuales || 0),
    pagosTardios: Number(payload.pagosTardios || 0),
    maxDiasAtraso: Number(payload.maxDiasAtraso || 0),
    devoluciones: Number(payload.devoluciones || 0),
    incidentesGraves: Number(payload.incidentesGraves || 0),
    otrosIncidentes: Number(payload.otrosIncidentes || 0),
    fechaCorte: payload.fechaCorte || new Date().toISOString(),
    actualizadoEn: new Date().toISOString(),
    loteOrigen: payload.loteOrigen || "ADMIN-HISTORIAL"
  };

  const indice = estadoERP.clientesHistorial.findIndex((cliente) => cliente.email.toLowerCase() === clave);
  if (indice >= 0) estadoERP.clientesHistorial[indice] = registro;
  else estadoERP.clientesHistorial.push(registro);

  registrarBitacora({ evento: "HISTORIAL_CLIENTE_ACTUALIZADO", email: clave, loteOrigen: registro.loteOrigen });
  res.status(200).json({ estado: "ACTUALIZADO", email: clave, cliente: registro });
});

app.post("/erp/admin/reset", (_req, res) => {
  estadoERP = {
    ultimaSecuenciaAceptada: 0,
    lotesProcesados: {},
    pagosCreditoProcesados: {},
    bitacora: [],
    clientesHistorial: clientesHistorialBase.map((c) => ({ ...c })),
    modoCaida: { activo: false, tipo: null, expiraEn: null },
  };
  res.json({ mensaje: "Estado del mock ERP reiniciado." });
});

app.get("/", (_req, res) => {
  res.json({
    servicio: "Mock ERP-Core (AS/400) — SGO-Logic",
    nota: "Simulador de pruebas. No representa un endpoint real del AS/400 (RES-01/RES-02).",
    endpoints: {
      "POST /erp/lotes": "Recibir lote (JSON, text/csv o application/xml)",
      "GET /erp/lotes/:loteId": "Confirmación de un lote ya procesado",
      "GET /erp/lotes": "Listado de lotes procesados y última secuencia",
      "POST /erp/clientes": "Registrar idempotentemente un cliente con correo verificado e historial inicial vacio",
      "GET /erp/clientes/historial": "Snapshot de historial crediticio por cliente, listo para Crédito",
      "POST /erp/clientes/pagos": "Batch idempotente de cuentas de crédito pagadas; actualiza pagos puntuales/tardíos del snapshot",
      "GET /erp/admin/estado": "Estado interno del mock (para depuración)",
      "POST /erp/admin/caida": "{ activar, tipo: 'timeout'|'error500'|'no_disponible', duracionMs? }",
      "PUT /erp/admin/clientes/:email/historial": "Actualizar el snapshot de historial para un cliente de prueba",
      "POST /erp/admin/reset": "Reiniciar el estado del mock",
    },
  });
});

app.listen(PORT, () => {
  console.log(`Mock ERP-Core (AS/400) escuchando en http://localhost:${PORT}`);
});
