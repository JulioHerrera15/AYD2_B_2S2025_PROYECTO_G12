/**
 * MOCK POS — 32 instancias de tienda — SGO-Logic / MegaStore
 * ------------------------------------------------------------
 * Simula el conjunto de terminales POS descrito en el DDA:
 *   - EV-16/EV-40/RES-03: las 32 instancias de POS no se reemplazan; son
 *     CONSUMIDORAS de eventos, nunca fuente (POS es downstream-only). El
 *     sistema no las modifica ni les exige capacidades nuevas.
 *   - ADR-012: el contexto Integración POS envía el evento de venta a la
 *     instancia correspondiente con un identificador único por evento
 *     (idempotencia) y, si no hay respuesta, el mensaje va a cola de
 *     fallidos con reintento (back-off).
 *   - CU-09 / HU-009: propagar el evento de venta al POS con reintentos e
 *     idempotencia, sin duplicar ni perder ventas aunque una tienda esté
 *     fuera de línea.
 */

const express = require("express");

const app = express();
const PORT = process.env.PORT || 4001;
const TOTAL_TIENDAS = 32;

app.use(express.json({ limit: "2mb" }));

// Estado interno: una entrada por cada una de las 32 tiendas
function tiendaId(n) {
  return `T${String(n).padStart(2, "0")}`;
}

function estadoInicialTienda() {
  return {
    enLinea: true,
    modoCaida: { tipo: null, expiraEn: null }, // solo importa si enLinea=false
    eventosProcesados: {}, // eventoId -> evento recibido (para idempotencia)
    bitacora: [],
  };
}

let tiendas = {};
function reiniciarTodasLasTiendas() {
  tiendas = {};
  for (let i = 1; i <= TOTAL_TIENDAS; i++) {
    tiendas[tiendaId(i)] = estadoInicialTienda();
  }
}
reiniciarTodasLasTiendas();

function registrarBitacora(tienda, entrada) {
  tienda.bitacora.unshift({ ...entrada, ts: new Date().toISOString() });
  tienda.bitacora = tienda.bitacora.slice(0, 100);
}

function caidaVigente(tienda) {
  if (tienda.enLinea) return false;
  if (tienda.modoCaida.expiraEn && Date.now() > tienda.modoCaida.expiraEn) {
    tienda.enLinea = true;
    tienda.modoCaida = { tipo: null, expiraEn: null };
    return false;
  }
  return true;
}

function obtenerTienda(id, res) {
  const t = tiendas[id];
  if (!t) {
    res.status(404).json({ estado: "TIENDA_NO_EXISTE", mensaje: `No existe la tienda ${id}. Tiendas válidas: T01..T${TOTAL_TIENDAS}.` });
    return null;
  }
  return t;
}

// POST /pos/:tiendaId/eventos — recepción de evento de venta
app.post("/pos/:tiendaId/eventos", (req, res) => {
  const id = req.params.tiendaId.toUpperCase();
  const tienda = obtenerTienda(id, res);
  if (!tienda) return;

  const evento = req.body;
  if (!evento || !evento.eventoId || !evento.ordenId) {
    return res.status(400).json({
      estado: "FORMATO_INVALIDO",
      mensaje: "El evento debe incluir al menos eventoId (identificador único) y ordenId.",
    });
  }

  // --- 1. Tienda fuera de línea: simula falla de red/terminal apagada ----
  if (caidaVigente(tienda)) {
    const tipo = tienda.modoCaida.tipo;
    registrarBitacora(tienda, { evento: "RECHAZO_TIENDA_OFFLINE", tipo, eventoId: evento.eventoId });

    if (tipo === "timeout") {
      // El worker de Integración POS debe agotar su propio timeout y reintentar (dead-letter + back-off).
      return setTimeout(() => {
        if (!res.headersSent) {
          res.status(504).json({ estado: "TIMEOUT", mensaje: `Tienda ${id} no respondió a tiempo (simulado).` });
        }
      }, 8000);
    }
    if (tipo === "error500") {
      return res.status(500).json({ estado: "ERROR_TERMINAL", mensaje: `Falla interna simulada en la terminal de ${id}.` });
    }
    // 'no_disponible' por defecto (tienda sin conexión / terminal apagada)
    return res.status(503).json({ estado: "NO_DISPONIBLE", mensaje: `Tienda ${id} fuera de línea (simulado).` });
  }

  // -- 2. Idempotencia: mismo eventoId ya procesado por esta tienda --------
  if (tienda.eventosProcesados[evento.eventoId]) {
    registrarBitacora(tienda, { evento: "EVENTO_DUPLICADO", eventoId: evento.eventoId, ordenId: evento.ordenId });
    return res.status(200).json({
      estado: "DUPLICADO",
      mensaje: `El evento ${evento.eventoId} ya fue procesado por ${id}; se ignora sin reprocesar (idempotencia).`,
      eventoOriginal: tienda.eventosProcesados[evento.eventoId],
    });
  }

  // --- 3. Aceptación y "reflejo" de la venta en la terminal -------
  const confirmacion = {
    tienda: id,
    eventoId: evento.eventoId,
    ordenId: evento.ordenId,
    tipo: evento.tipo || "VentaConfirmada",
    estado: "ACEPTADO",
    procesadoEn: new Date().toISOString(),
  };
  tienda.eventosProcesados[evento.eventoId] = { ...evento, ...confirmacion };
  registrarBitacora(tienda, { evento: "EVENTO_ACEPTADO", eventoId: evento.eventoId, ordenId: evento.ordenId });

  return res.status(200).json(confirmacion);
});

// Consultas de apoyo (para verificar qué "vio" cada tienda)
app.get("/pos/:tiendaId/eventos", (req, res) => {
  const id = req.params.tiendaId.toUpperCase();
  const tienda = obtenerTienda(id, res);
  if (!tienda) return;
  res.json({
    tienda: id,
    enLinea: tienda.enLinea,
    totalEventosProcesados: Object.keys(tienda.eventosProcesados).length,
    eventos: Object.values(tienda.eventosProcesados),
  });
});

app.get("/pos/:tiendaId/estado", (req, res) => {
  const id = req.params.tiendaId.toUpperCase();
  const tienda = obtenerTienda(id, res);
  if (!tienda) return;
  res.json({
    tienda: id,
    enLinea: tienda.enLinea,
    modoCaida: tienda.enLinea ? null : tienda.modoCaida,
    totalEventosProcesados: Object.keys(tienda.eventosProcesados).length,
    ultimosEventos: tienda.bitacora.slice(0, 10),
  });
});

// Vista consolidada de las 32 tiendas (equivalente a un panel de operación interno,
// no forma parte del POS real y es solo para verificar el estado del mock en pruebas).
app.get("/pos", (_req, res) => {
  const resumen = Object.entries(tiendas).map(([id, t]) => ({
    tienda: id,
    enLinea: t.enLinea,
    tipoCaida: t.enLinea ? null : t.modoCaida.tipo,
    totalEventosProcesados: Object.keys(t.eventosProcesados).length,
  }));
  res.json({
    totalTiendas: TOTAL_TIENDAS,
    tiendasFueraDeLinea: resumen.filter((r) => !r.enLinea).map((r) => r.tienda),
    tiendas: resumen,
  });
});

// Administración del mock (no existe en el POS real, solo para pruebas)
app.post("/pos/admin/:tiendaId/caida", (req, res) => {
  const id = req.params.tiendaId.toUpperCase();
  const tienda = obtenerTienda(id, res);
  if (!tienda) return;

  const { activar = true, tipo = "no_disponible", duracionMs } = req.body || {};
  if (!["timeout", "error500", "no_disponible"].includes(tipo)) {
    return res.status(400).json({ mensaje: "tipo debe ser 'timeout', 'error500' o 'no_disponible'." });
  }

  if (activar) {
    tienda.enLinea = false;
    tienda.modoCaida = { tipo, expiraEn: duracionMs ? Date.now() + Number(duracionMs) : null };
  } else {
    tienda.enLinea = true;
    tienda.modoCaida = { tipo: null, expiraEn: null };
  }
  registrarBitacora(tienda, { evento: activar ? "TIENDA_DESCONECTADA" : "TIENDA_RECONECTADA", tipo, duracionMs });

  res.json({ mensaje: `Estado de conexión de ${id} actualizado.`, tienda: id, enLinea: tienda.enLinea, modoCaida: tienda.modoCaida });
});

// Desconectar/reconectar varias tiendas de una vez (simular un incidente regional)
app.post("/pos/admin/caida-masiva", (req, res) => {
  const { tiendas: idsTiendas = [], activar = true, tipo = "no_disponible", duracionMs } = req.body || {};
  const afectadas = [];
  idsTiendas.forEach((id) => {
    const t = tiendas[id.toUpperCase()];
    if (!t) return;
    if (activar) {
      t.enLinea = false;
      t.modoCaida = { tipo, expiraEn: duracionMs ? Date.now() + Number(duracionMs) : null };
    } else {
      t.enLinea = true;
      t.modoCaida = { tipo: null, expiraEn: null };
    }
    registrarBitacora(t, { evento: activar ? "TIENDA_DESCONECTADA" : "TIENDA_RECONECTADA", tipo, duracionMs });
    afectadas.push(id.toUpperCase());
  });
  res.json({ mensaje: "Estado actualizado.", tiendasAfectadas: afectadas });
});

app.post("/pos/admin/reset", (_req, res) => {
  reiniciarTodasLasTiendas();
  res.json({ mensaje: "Estado de las 32 tiendas reiniciado." });
});

app.get("/", (_req, res) => {
  res.json({
    servicio: "Mock POS (32 instancias) — SGO-Logic",
    nota: "Simulador de pruebas. Las tiendas van de T01 a T32 (RES-03: el POS no se reemplaza ni se modifica).",
    endpoints: {
      "POST /pos/:tiendaId/eventos": "Enviar evento de venta a una tienda (body: {eventoId, ordenId, tipo, ...})",
      "GET /pos/:tiendaId/eventos": "Eventos ya procesados por una tienda",
      "GET /pos/:tiendaId/estado": "Estado de conexión y últimos eventos de una tienda",
      "GET /pos": "Resumen de las 32 tiendas (en línea / fuera de línea)",
      "POST /pos/admin/:tiendaId/caida": "{ activar, tipo: 'timeout'|'error500'|'no_disponible', duracionMs? }",
      "POST /pos/admin/caida-masiva": "{ tiendas: ['T05','T06'], activar, tipo, duracionMs? }",
      "POST /pos/admin/reset": "Reiniciar el estado de las 32 tiendas",
    },
  });
});

app.listen(PORT, () => {
  console.log(`Mock POS (${TOTAL_TIENDAS} tiendas) escuchando en http://localhost:${PORT}`);
});
