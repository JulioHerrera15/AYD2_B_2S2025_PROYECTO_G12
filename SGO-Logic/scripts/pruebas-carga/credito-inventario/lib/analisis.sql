-- Analisis de una corrida de las pruebas de credito + inventario.
-- Lo arma y lo ejecuta verificar.sh en una sesion de sgo_ordenes, despues de
-- cargar como tablas temporales lo extraido de las otras bases:
--   inv_reserva(orden_id, estado, creada, unidades)   reservas del SKU de la corrida
--   inv_existencia(fisica, reservada)                 existencia del SKU en CD-01
--   aud_liberacion(orden_id, momento, tipo, motivo)   ReservaInventarioLiberada/Expirada
--   cr_decision, cr_cuenta, cr_linea                  Credito de los clientes de la corrida
-- __PREFIJO__ y __VENTANA__ los reemplaza verificar.sh.
-- Toda la salida es "clave=valor" (y "minuto|..." para la tabla por minuto).

-- ---------------------------------------------------------------------------
-- Ordenes de la corrida y sus tiempos en la saga
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE o AS
SELECT ord.id,
       ord.cliente_id,
       ord.medio_pago::text AS medio,
       ord.estado::text AS estado,
       extract(epoch FROM ord.creada_en)::double precision AS creada,
       -- grupo del cliente: b (elegible), r (regular), x (no elegible), t (tarjeta)
       substring(ord.cliente_id FROM length('__PREFIJO__') + 2 FOR 1) AS grupo,
       coalesce((SELECT sum(l.cantidad) FROM ordenes.linea_orden l WHERE l.orden_id = ord.id), 0)::int AS unidades
FROM ordenes.orden ord
WHERE ord.cliente_id LIKE '__PREFIJO__-%';

CREATE TEMP TABLE t AS
SELECT tr.orden_id,
       extract(epoch FROM min(tr.cambiado_en) FILTER (
           WHERE tr.estado_nuevo::text = 'INVENTARIO_RESERVADO'))::double precision AS reservado,
       -- Primer resultado de la ruta de pago: aqui termina la espera con stock apartado.
       extract(epoch FROM min(tr.cambiado_en) FILTER (
           WHERE tr.estado_nuevo::text IN ('CREDITO_APROBADO', 'PAGO_APLICADO', 'PAGO_PENDIENTE',
                                          'RECHAZADA', 'CANCELADA_RESERVA_VENCIDA', 'CANCELADA')))::double precision AS decidido,
       coalesce(bool_or(tr.estado_nuevo::text = 'RECHAZADA' AND tr.paso_saga::text = 'RESERVA'), false) AS rech_stock,
       coalesce(bool_or(tr.estado_nuevo::text = 'RECHAZADA'
                        AND tr.motivo LIKE 'No pudimos confirmar tu cr%dito a tiempo%'), false) AS rech_timeout,
       coalesce(bool_or(tr.estado_nuevo::text = 'RECHAZADA' AND tr.paso_saga::text = 'COBRO'), false) AS rech_cobro
FROM ordenes.transicion_orden tr
WHERE tr.orden_id IN (SELECT id FROM o)
GROUP BY tr.orden_id;

CREATE TEMP TABLE r AS
SELECT o.*,
       t.reservado,
       t.decidido,
       coalesce(t.rech_stock, false) AS rech_stock,
       coalesce(t.rech_timeout, false) AS rech_timeout,
       coalesce(t.rech_cobro, false) AS rech_cobro,
       CASE WHEN t.reservado IS NOT NULL AND t.decidido IS NOT NULL THEN t.decidido - t.reservado END AS espera,
       CASE
           WHEN o.estado IN ('PAGO_PENDIENTE', 'PAGO_APLICADO', 'CONFIRMADA_PENDIENTE_FISCAL', 'CONFIRMADA',
                             'DESPACHO_SOLICITADO', 'EN_PREPARACION', 'EN_TRANSITO', 'LISTA_PARA_RECOGER') THEN 'retiene'
           WHEN o.estado IN ('RECHAZADA', 'CANCELADA', 'CANCELADA_RESERVA_VENCIDA') THEN 'negativo'
           WHEN o.estado IN ('RESERVA_SOLICITADA', 'INVENTARIO_RESERVADO', 'EVALUACION_CREDITO_SOLICITADA',
                             'CREDITO_APROBADO', 'COBRO_SOLICITADO') THEN 'transito'
           WHEN o.estado = 'CREADA' THEN 'sin_confirmar'
           ELSE 'otro'
       END AS clase
FROM o LEFT JOIN t ON t.orden_id = o.id;

CREATE TEMP TABLE reservas_vivas AS
SELECT orden_id, sum(unidades)::int AS unidades
FROM inv_reserva WHERE estado IN ('ACTIVA', 'CONFIRMADA') GROUP BY orden_id;

CREATE TEMP TABLE lib AS
SELECT orden_id, min(momento) AS momento FROM aud_liberacion GROUP BY orden_id;

-- ---------------------------------------------------------------------------
-- Volumen y estados finales
-- ---------------------------------------------------------------------------
SELECT 'ordenes_total=' || count(*) FROM r;
SELECT 'ordenes_credito=' || count(*) FILTER (WHERE medio = 'CREDITO') FROM r;
SELECT 'ordenes_tarjeta=' || count(*) FILTER (WHERE medio = 'TARJETA') FROM r;
SELECT 'ordenes_en_transito=' || count(*) FILTER (WHERE clase = 'transito') FROM r;
SELECT 'ordenes_sin_confirmar=' || count(*) FILTER (WHERE clase = 'sin_confirmar') FROM r;
SELECT 'estado.' || medio || '.' || estado || '=' || count(*) FROM r GROUP BY medio, estado ORDER BY medio, estado;

-- ---------------------------------------------------------------------------
-- Tiempo con stock apartado esperando el resultado del pago (segundos)
-- ---------------------------------------------------------------------------
SELECT 'decision_' || lower(medio) || '_p50_s=' || round(percentile_cont(0.50) WITHIN GROUP (ORDER BY espera)::numeric, 2)
       || E'\n' || 'decision_' || lower(medio) || '_p95_s=' || round(percentile_cont(0.95) WITHIN GROUP (ORDER BY espera)::numeric, 2)
       || E'\n' || 'decision_' || lower(medio) || '_max_s=' || round(max(espera)::numeric, 2)
FROM r WHERE espera IS NOT NULL GROUP BY medio ORDER BY medio;
SELECT 'retencion_credito_max_s=' || coalesce(round(max(espera)::numeric, 2)::text, '')
FROM r WHERE medio = 'CREDITO' AND espera IS NOT NULL;
-- Lo que espera el cliente desde que crea la orden hasta saber si se acepto.
SELECT 'creacion_a_resultado_' || lower(medio) || '_p95_s=' ||
       round(percentile_cont(0.95) WITHIN GROUP (ORDER BY decidido - creada)::numeric, 2)
FROM r WHERE decidido IS NOT NULL GROUP BY medio ORDER BY medio;
SELECT 'recuperacion_decision_credito_p95_s=' ||
       coalesce(round(percentile_cont(0.95) WITHIN GROUP (ORDER BY espera)::numeric, 2)::text, '')
FROM r WHERE medio = 'CREDITO' AND espera IS NOT NULL
  AND creada >= (SELECT max(creada) FROM r) - __VENTANA__;

-- ---------------------------------------------------------------------------
-- Por que terminaron mal las que terminaron mal
-- ---------------------------------------------------------------------------
SELECT 'rechazos_stock_tarjeta=' || count(*) FILTER (WHERE rech_stock AND medio = 'TARJETA') FROM r;
SELECT 'rechazos_stock_credito=' || count(*) FILTER (WHERE rech_stock AND medio = 'CREDITO') FROM r;
SELECT 'rechazos_credito=' || count(*) FILTER (WHERE medio = 'CREDITO' AND rech_cobro AND NOT rech_timeout) FROM r;
SELECT 'timeouts_credito=' || count(*) FILTER (WHERE rech_timeout) FROM r;
SELECT 'rechazos_cobro_tarjeta=' || count(*) FILTER (WHERE medio = 'TARJETA' AND rech_cobro) FROM r;
SELECT 'canceladas_reserva_vencida=' || count(*) FILTER (WHERE estado = 'CANCELADA_RESERVA_VENCIDA') FROM r;
SELECT 'rechazadas_grupo_b=' || count(*) FILTER (WHERE grupo = 'b' AND clase = 'negativo') FROM r;
SELECT 'rechazadas_grupo_t=' || count(*) FILTER (WHERE grupo = 't' AND clase = 'negativo') FROM r;
SELECT 'no_rechazadas_grupo_x=' || count(*) FILTER (WHERE grupo = 'x' AND clase <> 'negativo') FROM r;

-- ---------------------------------------------------------------------------
-- Inventario: integridad
-- ---------------------------------------------------------------------------
SELECT 'stock_fisico=' || coalesce((SELECT sum(fisica) FROM inv_existencia)::text, 'NA');
SELECT 'stock_reservado=' || coalesce((SELECT sum(reservada) FROM inv_existencia)::text, 'NA');
SELECT 'stock_disponible_final=' || coalesce((SELECT sum(fisica - reservada) FROM inv_existencia)::text, 'NA');
SELECT 'sobreventa=' || count(*) FROM inv_existencia WHERE reservada > fisica OR reservada < 0;
SELECT 'inventario_dif_existencia_vs_reservas=' ||
       ((SELECT coalesce(sum(reservada), 0) FROM inv_existencia) - (SELECT coalesce(sum(unidades), 0) FROM reservas_vivas));
SELECT 'inventario_dif_existencia_vs_ordenes=' ||
       ((SELECT coalesce(sum(reservada), 0) FROM inv_existencia)
        - (SELECT coalesce(sum(unidades), 0) FROM r WHERE clase IN ('retiene', 'transito')));
SELECT 'fugas_stock=' || count(*) FROM r JOIN reservas_vivas v ON v.orden_id = r.id WHERE r.clase = 'negativo';
SELECT 'huecos_stock=' || count(*) FROM r
WHERE r.clase = 'retiene' AND NOT EXISTS (SELECT 1 FROM reservas_vivas v WHERE v.orden_id = r.id);
SELECT 'unidades_vendidas_tarjeta=' || coalesce(sum(unidades) FILTER (WHERE medio = 'TARJETA' AND clase = 'retiene'), 0) FROM r;
SELECT 'unidades_vendidas_credito=' || coalesce(sum(unidades) FILTER (WHERE medio = 'CREDITO' AND clase = 'retiene'), 0) FROM r;

-- ---------------------------------------------------------------------------
-- Impacto del credito en Inventario: stock apartado por compras que no se vendieron
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE esteril AS
SELECT r.id, r.medio, r.unidades, r.decidido, r.rech_timeout,
       ir.creada AS reserva_creada,
       lib.momento AS liberada
FROM r
JOIN (SELECT orden_id, min(creada) AS creada FROM inv_reserva GROUP BY orden_id) ir ON ir.orden_id = r.id
LEFT JOIN lib ON lib.orden_id = r.id
WHERE r.clase = 'negativo';

SELECT 'credito_negativas_con_reserva=' || count(*) FILTER (WHERE medio = 'CREDITO') FROM esteril;
SELECT 'rechazos_sin_evento_liberacion=' || count(*) FILTER (WHERE medio = 'CREDITO' AND liberada IS NULL) FROM esteril;
SELECT 'liberacion_tras_rechazo_p50_s=' || coalesce(round(percentile_cont(0.50) WITHIN GROUP (ORDER BY liberada - decidido)::numeric, 2)::text, '')
       || E'\n' || 'liberacion_tras_rechazo_p95_s=' || coalesce(round(percentile_cont(0.95) WITHIN GROUP (ORDER BY liberada - decidido)::numeric, 2)::text, '')
       || E'\n' || 'liberacion_tras_rechazo_max_s=' || coalesce(round(max(liberada - decidido)::numeric, 2)::text, '')
FROM esteril WHERE medio = 'CREDITO' AND liberada IS NOT NULL AND decidido IS NOT NULL;
-- Retencion esteril: desde que Inventario aparto las unidades hasta que volvieron,
-- en compras a credito que al final NO se vendieron.
SELECT 'retencion_esteril_p50_s=' || coalesce(round(percentile_cont(0.50) WITHIN GROUP (ORDER BY liberada - reserva_creada)::numeric, 2)::text, '')
       || E'\n' || 'retencion_esteril_p95_s=' || coalesce(round(percentile_cont(0.95) WITHIN GROUP (ORDER BY liberada - reserva_creada)::numeric, 2)::text, '')
       || E'\n' || 'retencion_esteril_max_s=' || coalesce(round(max(liberada - reserva_creada)::numeric, 2)::text, '')
       || E'\n' || 'retencion_esteril_unidades=' || coalesce(sum(unidades), 0)
       || E'\n' || 'retencion_esteril_unidad_segundos=' || coalesce(round(sum(unidades * (liberada - reserva_creada))::numeric, 1), 0)
FROM esteril WHERE medio = 'CREDITO' AND liberada IS NOT NULL;
-- Compradores con tarjeta que recibieron "sin stock" en un momento en que habia
-- unidades apartadas por compras a credito que al final se rechazaron (E4).
SELECT 'tarjetas_rechazadas_mientras_credito_retenia=' || count(*)
FROM r c
WHERE c.medio = 'TARJETA' AND c.rech_stock AND c.decidido IS NOT NULL
  AND EXISTS (SELECT 1 FROM esteril e
              WHERE e.medio = 'CREDITO' AND e.reserva_creada <= c.decidido
                AND coalesce(e.liberada, 1e12) > c.decidido);
-- Ventas con tarjeta que se perdieron mientras el credito retenia unidades que
-- despues volvieron al disponible y nadie compro (relevante en E4).
SELECT 'ventas_tarjeta_perdidas_por_retencion=' ||
       least((SELECT count(*) FROM r WHERE rech_stock AND medio = 'TARJETA'),
             greatest(0, coalesce((SELECT sum(fisica - reservada) FROM inv_existencia), 0)));

-- ---------------------------------------------------------------------------
-- Credito: integridad de la linea y de las cuentas por cobrar
-- ---------------------------------------------------------------------------
SELECT 'credito_decision.' || resultado || '=' || count(*) FROM cr_decision GROUP BY resultado ORDER BY resultado;
SELECT 'credito_motivo.' || codigo_motivo || '=' || count(*) FROM cr_decision
WHERE codigo_motivo <> '' GROUP BY codigo_motivo ORDER BY codigo_motivo;
SELECT 'credito_cuentas.' || estado || '=' || count(*) FROM cr_cuenta GROUP BY estado ORDER BY estado;
SELECT 'lineas_sobre_limite=' || count(*) FROM cr_linea WHERE saldo > limite + 0.005;
SELECT 'lineas_incoherentes=' || count(*) FROM cr_linea l
WHERE abs(l.saldo - coalesce((SELECT sum(c.saldo_pendiente) FROM cr_cuenta c
                              WHERE c.cliente_id = l.cliente_id AND c.estado IN ('VIGENTE', 'VENCIDA')), 0)) > 0.005;
SELECT 'cargos_huerfanos=' || count(*) FROM cr_cuenta c JOIN r ON r.id = c.orden_id
WHERE r.clase = 'negativo' AND c.estado IN ('VIGENTE', 'VENCIDA');
SELECT 'max_aprobadas_por_cliente=' || coalesce(max(n), 0)
FROM (SELECT cliente_id, count(*) AS n FROM cr_decision WHERE resultado = 'APROBADA' GROUP BY cliente_id) x;
SELECT 'max_uso_linea_pct=' || coalesce(round(max(100 * saldo / nullif(limite, 0))::numeric, 1)::text, '')
FROM cr_linea;

-- ---------------------------------------------------------------------------
-- Vista por minuto (desde la primera orden de la corrida)
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE base AS SELECT min(creada) AS t0 FROM r;

CREATE TEMP TABLE pm AS
SELECT floor((r.creada - base.t0) / 60)::int + 1 AS minuto,
       count(*) AS ordenes,
       count(*) FILTER (WHERE medio = 'CREDITO') AS credito,
       percentile_cont(0.95) WITHIN GROUP (ORDER BY espera) FILTER (WHERE medio = 'CREDITO') AS p95,
       max(espera) FILTER (WHERE medio = 'CREDITO') AS maximo,
       count(*) FILTER (WHERE rech_timeout) AS timeouts,
       count(*) FILTER (WHERE rech_stock) AS rech_stock
FROM r, base
GROUP BY 1;

SELECT 'minuto|' || pm.minuto || '|' || pm.ordenes || '|' || pm.credito || '|' ||
       coalesce(round(pm.p95::numeric, 2)::text, '-') || '|' ||
       coalesce(round(pm.maximo::numeric, 2)::text, '-') || '|' ||
       pm.timeouts || '|' || pm.rech_stock || '|' ||
       -- unidades de ordenes a credito con stock apartado y sin decision al cierre del minuto
       (SELECT coalesce(sum(x.unidades), 0) FROM r x, base
        WHERE x.medio = 'CREDITO' AND x.reservado <= base.t0 + 60 * pm.minuto
          AND coalesce(x.decidido, 1e12) > base.t0 + 60 * pm.minuto)
FROM pm ORDER BY pm.minuto;

SELECT 'primer_minuto_degradado=' || coalesce(min(minuto)::text, 'ninguno')
FROM pm WHERE p95 > 5 OR timeouts > 0;
SELECT 'ordenes_en_primer_minuto_degradado=' ||
       coalesce((SELECT ordenes FROM pm WHERE minuto = (SELECT min(minuto) FROM pm WHERE p95 > 5 OR timeouts > 0))::text, 'ninguno');
SELECT 'pico_unidades_esperando_credito=' || coalesce(max(v), 0) FROM (
  SELECT (SELECT coalesce(sum(x.unidades), 0) FROM r x, base
          WHERE x.medio = 'CREDITO' AND x.reservado <= base.t0 + 60 * pm.minuto
            AND coalesce(x.decidido, 1e12) > base.t0 + 60 * pm.minuto) AS v
  FROM pm) s;
