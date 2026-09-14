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
let estadoERP = {
  ultimaSecuenciaAceptada: 0,
  lotesProcesados: {}, // loteId -> confirmación (ACEPTADO o RECHAZADO_CUADRE)
  bitacora: [],        // historial simple de todas las solicitudes recibidas
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
 * ORDEN,CANAL,TIENDA,MONTO,FECHA
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
    const [ordenId, canal, tienda, monto, fecha] = linea.split(",").map((v) => v.trim());
    return { ordenId, canal, tienda, monto: Number(monto), fecha };
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
  registrarBitacora({ evento: "LOTE_ACEPTADO", loteId: lote.loteId, secuencia: lote.secuencia, montoCalculado });

  return res.status(200).json(confirmacion);
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

app.post("/erp/admin/reset", (_req, res) => {
  estadoERP = {
    ultimaSecuenciaAceptada: 0,
    lotesProcesados: {},
    bitacora: [],
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
      "GET /erp/admin/estado": "Estado interno del mock (para depuración)",
      "POST /erp/admin/caida": "{ activar, tipo: 'timeout'|'error500'|'no_disponible', duracionMs? }",
      "POST /erp/admin/reset": "Reiniciar el estado del mock",
    },
  });
});

app.listen(PORT, () => {
  console.log(`Mock ERP-Core (AS/400) escuchando en http://localhost:${PORT}`);
});
