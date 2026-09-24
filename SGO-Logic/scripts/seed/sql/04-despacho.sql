-- Envios de las ordenes que llegaron a DESPACHO_SOLICITADO: a domicilio con
-- guia de LogiExpress y recogidas en tienda. DES-...06 queda en INCIDENCIA.

INSERT INTO despacho.despacho (
    id, orden_id, reserva_id, estado, metodo_entrega, guia_id, transportista, tracking_url,
    ubicacion_origen_id, tipo_ubicacion_origen, ubicacion_recogida_id, direccion_entrega, detalle_estado,
    creado_en, entregado_en, secuencia, version)
VALUES
    ('DES-2026-000001', 'ORD-2026-000001', 'RSV-2026-000001', 'ENTREGADA', 'DOMICILIO',
        'LGX-2026-100001', 'LogiExpress', 'http://localhost:8098/api/guias/LGX-2026-100001',
        'CD-CENTRAL', 'CENTRO_DISTRIBUCION', NULL, '6a Avenida 12-34 Zona 10, Guatemala, Guatemala',
        'Recibido por el titular', now() - interval '19 days', now() - interval '17 days', 5, 4),
    ('DES-2026-000002', 'ORD-2026-000002', 'RSV-2026-000002', 'EN_TRANSITO', 'DOMICILIO',
        'LGX-2026-100002', 'LogiExpress', 'http://localhost:8098/api/guias/LGX-2026-100002',
        'CD-CENTRAL', 'CENTRO_DISTRIBUCION', NULL, '6a Avenida 12-34 Zona 10, Guatemala, Guatemala',
        'Salio del centro de distribucion', now() - interval '2 days', NULL, 4, 3),
    ('DES-2026-000003', 'ORD-2026-000003', 'RSV-2026-000003', 'LISTA_PARA_RECOGER', 'RECOGIDA_TIENDA',
        NULL, NULL, NULL,
        'TIENDA-02-MIRAFLORES', 'TIENDA', 'TIENDA-02-MIRAFLORES', NULL,
        'Disponible en mostrador de Miraflores', now() - interval '1 day 18 hours', NULL, 4, 3),
    ('DES-2026-000004', 'ORD-2026-000004', 'RSV-2026-000004', 'EN_PREPARACION', 'DOMICILIO',
        'LGX-2026-100004', 'LogiExpress', 'http://localhost:8098/api/guias/LGX-2026-100004',
        'CD-CENTRAL', 'CENTRO_DISTRIBUCION', NULL, 'Calzada Roosevelt 22-43 Zona 11, Mixco, Guatemala',
        'Alistando pedido', now() - interval '12 hours', NULL, 3, 2),
    ('DES-2026-000005', 'ORD-2026-000008', 'RSV-2026-000008', 'ENTREGADA', 'RECOGIDA_TIENDA',
        NULL, NULL, NULL,
        'TIENDA-01-CENTRAL', 'TIENDA', 'TIENDA-01-CENTRAL', NULL,
        'Entregado en mostrador', now() - interval '14 days', now() - interval '12 days', 4, 3),
    ('DES-2026-000006', 'ORD-2026-000009', 'RSV-2026-000009', 'INCIDENCIA', 'DOMICILIO',
        'LGX-2026-100006', 'LogiExpress', 'http://localhost:8098/api/guias/LGX-2026-100006',
        'CD-CENTRAL', 'CENTRO_DISTRIBUCION', NULL, '4a Calle 5-67 Zona 1, Quetzaltenango, Quetzaltenango',
        'Cliente ausente en la direccion de entrega; se reprogramara visita', now() - interval '3 days', NULL, 5, 4);

INSERT INTO despacho.linea_despacho (id, despacho_id, sku, descripcion, cantidad)
VALUES
    (gen_random_uuid(), 'DES-2026-000001', 'PROD-002', 'Smart Ultra HD LED TV 65"',                1),
    (gen_random_uuid(), 'DES-2026-000002', 'PROD-004', 'Barra de Sonido Dolby Atmos 5.1.2 550W',   1),
    (gen_random_uuid(), 'DES-2026-000002', 'PROD-007', 'Microondas Grill Smart Inverter 42L',      2),
    (gen_random_uuid(), 'DES-2026-000003', 'PROD-006', 'Aire Acondicionado Inverter 18000 BTU',    1),
    (gen_random_uuid(), 'DES-2026-000004', 'PROD-005', 'Laptop Gamer Ultra 16" Ryzen 9',           1),
    (gen_random_uuid(), 'DES-2026-000005', 'PROD-007', 'Microondas Grill Smart Inverter 42L',      1),
    (gen_random_uuid(), 'DES-2026-000006', 'PROD-002', 'Smart Ultra HD LED TV 65"',                1),
    (gen_random_uuid(), 'DES-2026-000006', 'PROD-004', 'Barra de Sonido Dolby Atmos 5.1.2 550W',   1);

DO $$ BEGIN PERFORM setval('despacho.despacho_correlativo', GREATEST((SELECT last_value FROM despacho.despacho_correlativo), 6)); END $$;