-- Nueve ordenes que recorren los estados principales de la saga, con sus lineas,
-- el historial completo de transiciones y una devolucion en proceso.
-- Los ids de pago, reserva, documento fiscal y despacho coinciden con los que
-- siembran 02-inventario, 04-despacho, 06-pagos y 07-facturacion.

CREATE TEMP TABLE seed_orden (
    n int, cliente_id text, canal text, metodo_entrega text, medio_pago text,
    ubicacion_solicitada_id text, ubicacion_reserva_id text,
    direccion text, municipio text, departamento text, referencia text,
    impuestos numeric(18,2), creada_hace interval, actualizada_hace interval,
    ruta text[], motivo_final text,
    fiscal_estado text  -- CERTIFICADO, ALERTA o null si no llego a facturarse
) ON COMMIT DROP;

INSERT INTO seed_orden VALUES
    (1, 'cliente1@sgo.local', 'WEB',         'DOMICILIO',       'TARJETA',           NULL,                   'CD-CENTRAL',
        '6a Avenida 12-34 Zona 10', 'Guatemala', 'Guatemala', 'Edificio Torre Azul, apto 502',
        659.88,  '20 days', '17 days',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA','DESPACHO_SOLICITADO','EN_PREPARACION','EN_TRANSITO','ENTREGADA'],
        'Entregada por LogiExpress', 'CERTIFICADO'),
    (2, 'cliente1@sgo.local', 'WEB',         'DOMICILIO',       'TARJETA',           NULL,                   'CD-CENTRAL',
        '6a Avenida 12-34 Zona 10', 'Guatemala', 'Guatemala', 'Edificio Torre Azul, apto 502',
        851.64,  '3 days', '1 day',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA','DESPACHO_SOLICITADO','EN_PREPARACION','EN_TRANSITO'],
        'Paquete en ruta con LogiExpress', 'CERTIFICADO'),
    (3, 'cliente2@sgo.local', 'MOVIL',       'RECOGIDA_TIENDA', 'BILLETERA_DIGITAL', 'TIENDA-02-MIRAFLORES', 'TIENDA-02-MIRAFLORES',
        NULL, NULL, NULL, NULL,
        551.88,  '2 days', '20 hours',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA','DESPACHO_SOLICITADO','EN_PREPARACION','LISTA_PARA_RECOGER'],
        'Lista para recoger en Miraflores', 'CERTIFICADO'),
    (4, 'cliente2@sgo.local', 'WEB',         'DOMICILIO',       'TARJETA',           NULL,                   'CD-CENTRAL',
        'Calzada Roosevelt 22-43 Zona 11', 'Mixco', 'Guatemala', 'Frente al centro comercial',
        1979.88, '1 day', '6 hours',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA','DESPACHO_SOLICITADO','EN_PREPARACION'],
        'Alistando pedido en CD-CENTRAL', 'CERTIFICADO'),
    (5, 'cliente3@sgo.local', 'CALL_CENTER', 'DOMICILIO',       'TRANSFERENCIA',     NULL,                   'CD-CENTRAL',
        '4a Calle 5-67 Zona 1', 'Quetzaltenango', 'Quetzaltenango', NULL,
        947.88,  '6 hours', '5 hours',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL'],
        'Pago aplicado, certificacion SAT pendiente', 'ALERTA'),
    (6, 'cliente3@sgo.local', 'WEB',         'DOMICILIO',       'TARJETA',           NULL,                   'CD-CENTRAL',
        '4a Calle 5-67 Zona 1', 'Quetzaltenango', 'Quetzaltenango', NULL,
        467.88,  '2 hours', '1 hour 45 minutes',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_PENDIENTE'],
        'PayKlik no respondio; se envio enlace de reintento', NULL),
    (7, 'cliente1@sgo.local', 'WEB',         'DOMICILIO',       'TARJETA',           NULL,                   'CD-CENTRAL',
        '6a Avenida 12-34 Zona 10', 'Guatemala', 'Guatemala', 'Edificio Torre Azul, apto 502',
        1799.88, '10 days', '9 days 23 hours',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','CANCELADA'],
        'Cobro rechazado: fondos insuficientes', NULL),
    (8, 'cliente2@sgo.local', 'TIENDA',      'RECOGIDA_TIENDA', 'TARJETA',           'TIENDA-01-CENTRAL',    'TIENDA-01-CENTRAL',
        NULL, NULL, NULL, NULL,
        227.88,  '15 days', '2 days',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA','DESPACHO_SOLICITADO','EN_PREPARACION','LISTA_PARA_RECOGER','ENTREGADA','DEVOLUCION_EN_PROCESO'],
        'Cliente solicita devolucion: producto no enciende', 'CERTIFICADO'),
    (9, 'cliente3@sgo.local', 'WEB',         'DOMICILIO',       'TARJETA',           NULL,                   'CD-CENTRAL',
        '4a Calle 5-67 Zona 1', 'Quetzaltenango', 'Quetzaltenango', 'Casa esquinera porton negro',
        1055.76, '4 days', '1 day 12 hours',
        ARRAY['CREADA','RESERVA_SOLICITADA','INVENTARIO_RESERVADO','COBRO_SOLICITADO','PAGO_APLICADO','CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA','DESPACHO_SOLICITADO','EN_PREPARACION','EN_TRANSITO'],
        'En ruta; transportista reporto incidencia', 'CERTIFICADO');

-- Correlativo del documento fiscal de cada orden facturada (serie A).
CREATE TEMP TABLE seed_fiscal (n int, correlativo int) ON COMMIT DROP;
INSERT INTO seed_fiscal VALUES (1,1),(2,2),(3,3),(4,4),(5,5),(8,6),(9,7);

INSERT INTO ordenes.orden (
    id, numero_orden, cliente_id, canal, metodo_entrega, moneda, medio_pago, estado, impuestos, version, creada_en,
    ubicacion_solicitada_id, receptor_cui,
    direccion_entrega_direccion, direccion_entrega_municipio, direccion_entrega_departamento, direccion_entrega_pais, direccion_entrega_referencia,
    pago_id, reserva_id, ubicacion_reserva_id, token_medio_pago,
    documento_fiscal_id, documento_fiscal_serie, documento_fiscal_numero, documento_fiscal_folio,
    documento_fiscal_numero_autorizacion, documento_fiscal_certificado_en,
    alerta_fiscal, alerta_fiscal_en, alerta_fiscal_motivo)
SELECT
    format('ORD-2026-%s', lpad(o.n::text, 6, '0')),
    lpad(o.n::text, 6, '0'),
    o.cliente_id, o.canal, o.metodo_entrega, 'GTQ', o.medio_pago,
    o.ruta[array_length(o.ruta, 1)],
    o.impuestos,
    array_length(o.ruta, 1),
    now() - o.creada_hace,
    o.ubicacion_solicitada_id, 'CF',
    o.direccion, o.municipio, o.departamento, CASE WHEN o.direccion IS NOT NULL THEN 'GT' END, o.referencia,
    format('a0000000-0000-4000-8000-%s', lpad(o.n::text, 12, '0')),
    format('RSV-2026-%s', lpad(o.n::text, 6, '0')),
    o.ubicacion_reserva_id,
    CASE WHEN o.medio_pago = 'TARJETA' AND o.ruta[array_length(o.ruta, 1)] <> 'CANCELADA' THEN 'tok_demo_visa_4242' END,
    CASE WHEN f.n IS NOT NULL THEN format('b0000000-0000-4000-8000-%s', lpad(f.correlativo::text, 12, '0')) END,
    CASE WHEN f.n IS NOT NULL THEN 'A' END,
    CASE WHEN f.n IS NOT NULL THEN lpad(f.correlativo::text, 8, '0') END,
    CASE WHEN o.fiscal_estado = 'CERTIFICADO' THEN format('SAT-FOLIO-2026-%s', lpad(f.correlativo::text, 8, '0')) END,
    CASE WHEN o.fiscal_estado = 'CERTIFICADO' THEN format('AUT-%s-2026', lpad(f.correlativo::text, 10, '0')) END,
    CASE WHEN o.fiscal_estado = 'CERTIFICADO' THEN now() - o.creada_hace + interval '10 minutes' END,
    coalesce(o.fiscal_estado = 'ALERTA', false),
    CASE WHEN o.fiscal_estado = 'ALERTA' THEN now() - o.actualizada_hace END,
    CASE WHEN o.fiscal_estado = 'ALERTA' THEN 'SAT no respondio tras 3 intentos de certificacion' END
FROM seed_orden o
LEFT JOIN seed_fiscal f ON f.n = o.n;

INSERT INTO ordenes.linea_orden (id, orden_id, sku, descripcion, cantidad, precio_unitario)
VALUES
    (gen_random_uuid(), 'ORD-2026-000001', 'PROD-002', 'Smart Ultra HD LED TV 65"',                  1,  5499.00),
    (gen_random_uuid(), 'ORD-2026-000002', 'PROD-004', 'Barra de Sonido Dolby Atmos 5.1.2 550W',     1,  3299.00),
    (gen_random_uuid(), 'ORD-2026-000002', 'PROD-007', 'Microondas Grill Smart Inverter 42L',        2,  1899.00),
    (gen_random_uuid(), 'ORD-2026-000003', 'PROD-006', 'Aire Acondicionado Inverter 18000 BTU',      1,  4599.00),
    (gen_random_uuid(), 'ORD-2026-000004', 'PROD-005', 'Laptop Gamer Ultra 16" Ryzen 9',             1, 16499.00),
    (gen_random_uuid(), 'ORD-2026-000005', 'PROD-003', 'Lavadora de Carga Frontal EcoBubble 22kg',   1,  7899.00),
    (gen_random_uuid(), 'ORD-2026-000006', 'PROD-008', 'Aspiradora Robot Laser con Fregado',         1,  3899.00),
    (gen_random_uuid(), 'ORD-2026-000007', 'PROD-001', 'Lumina Smart Refrigerator Pro',              1, 14999.00),
    (gen_random_uuid(), 'ORD-2026-000008', 'PROD-007', 'Microondas Grill Smart Inverter 42L',        1,  1899.00),
    (gen_random_uuid(), 'ORD-2026-000009', 'PROD-002', 'Smart Ultra HD LED TV 65"',                  1,  5499.00),
    (gen_random_uuid(), 'ORD-2026-000009', 'PROD-004', 'Barra de Sonido Dolby Atmos 5.1.2 550W',     1,  3299.00);

-- Historial: una transicion por cada paso de la ruta, repartidas en el tiempo
-- entre la creacion y la ultima actualizacion. Secuencia arranca en 2 (la 1 es
-- la creacion de la orden), igual que Orden.TransicionarA.
INSERT INTO ordenes.transicion_orden (id, orden_id, secuencia, estado_anterior, estado_nuevo, motivo, paso_saga, es_compensacion, cambiado_en)
SELECT
    gen_random_uuid(),
    format('ORD-2026-%s', lpad(o.n::text, 6, '0')),
    i,
    o.ruta[i - 1],
    o.ruta[i],
    CASE WHEN i = array_length(o.ruta, 1) THEN o.motivo_final END,
    CASE
        WHEN o.ruta[i] IN ('RESERVA_SOLICITADA','INVENTARIO_RESERVADO')                     THEN 'RESERVA'
        WHEN o.ruta[i] IN ('COBRO_SOLICITADO','PAGO_APLICADO','PAGO_PENDIENTE','CANCELADA') THEN 'COBRO'
        WHEN o.ruta[i] IN ('CONFIRMADA_PENDIENTE_FISCAL','CONFIRMADA')                      THEN 'FISCAL'
        WHEN o.ruta[i] IN ('DEVOLUCION_EN_PROCESO','DEVUELTA')                              THEN 'DEVOLUCION'
        ELSE 'DESPACHO'
    END,
    o.ruta[i] = 'CANCELADA',
    (now() - o.creada_hace)
        + ((o.creada_hace - o.actualizada_hace) * (i - 1)) / (array_length(o.ruta, 1) - 1)
FROM seed_orden o
CROSS JOIN LATERAL generate_series(2, array_length(o.ruta, 1)) AS i;

-- Devolucion en proceso de la orden 8 (microondas que no enciende).
INSERT INTO ordenes.devolucion (
    id, orden_original_id, cliente_id, documento_fiscal_original_id, pago_original_id, tipo, es_parcial, estado,
    moneda, monto_reembolso, ubicacion_reingreso_id, registrada_por, registrada_en)
VALUES (
    'DEV-2026-000001', 'ORD-2026-000008', 'cliente2@sgo.local',
    'b0000000-0000-4000-8000-000000000006', 'a0000000-0000-4000-8000-000000000008',
    'DEVOLUCION', false, 'REGISTRADA', 'GTQ', 2126.88, 'TIENDA-01-CENTRAL', 'operador@sgo.local', now() - interval '2 days');

INSERT INTO ordenes.linea_devolucion (id, devolucion_id, sku, cantidad, precio_unitario, motivo, sku_reemplazo)
VALUES (gen_random_uuid(), 'DEV-2026-000001', 'PROD-007', 1, 1899.00, 'El producto no enciende al conectarlo', NULL);

DO $$ BEGIN PERFORM setval('ordenes.orden_correlativo',      GREATEST((SELECT last_value FROM ordenes.orden_correlativo), 9)); END $$;
DO $$ BEGIN PERFORM setval('ordenes.devolucion_correlativo', GREATEST((SELECT last_value FROM ordenes.devolucion_correlativo), 1)); END $$;