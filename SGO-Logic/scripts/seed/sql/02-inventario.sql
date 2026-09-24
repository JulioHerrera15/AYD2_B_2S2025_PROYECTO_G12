-- Existencias del catalogo web (PROD-001..008) en los CD y las cuatro tiendas,
-- su movimiento de carga inicial y una reserva por cada orden sembrada.
-- Las reservas de ordenes en curso (ORD-...05 y 06) quedan ACTIVAS y retienen
-- stock en CD-CENTRAL; el resto ya se libero.

INSERT INTO inventario.existencia (id, sku, ubicacion_id, tipo_ubicacion, cantidad_fisica, cantidad_reservada, version)
SELECT gen_random_uuid(), sku, ubicacion_id, tipo_ubicacion, cantidad, 0, 0
FROM unnest(ARRAY['PROD-001','PROD-002','PROD-003','PROD-004','PROD-005','PROD-006','PROD-007','PROD-008']) AS sku
CROSS JOIN (VALUES
    ('CD-01',                'CENTRO_DISTRIBUCION', 100),
    ('CD-CENTRAL',           'CENTRO_DISTRIBUCION', 100),
    ('TIENDA-01-CENTRAL',    'TIENDA',               50),
    ('TIENDA-02-MIRAFLORES', 'TIENDA',               50),
    ('TIENDA-03-PORTALES',   'TIENDA',               50),
    ('TIENDA-04-PRADERA',    'TIENDA',               50)
) AS u(ubicacion_id, tipo_ubicacion, cantidad);

INSERT INTO inventario.movimiento_existencia (id, sku, ubicacion_id, tipo, motivo, cantidad, cantidad_fisica_resultante, registrado_en)
SELECT gen_random_uuid(), sku, ubicacion_id, 'ENTRADA', 'CARGA_INICIAL', cantidad_fisica, cantidad_fisica, now() - interval '30 days'
FROM inventario.existencia;

-- Algunos productos con poco stock en tiendas, para que el catalogo muestre variedad.
UPDATE inventario.existencia SET cantidad_fisica = 3  WHERE sku = 'PROD-001' AND ubicacion_id = 'TIENDA-03-PORTALES';
UPDATE inventario.existencia SET cantidad_fisica = 0  WHERE sku = 'PROD-005' AND ubicacion_id = 'TIENDA-04-PRADERA';
INSERT INTO inventario.movimiento_existencia (id, sku, ubicacion_id, tipo, motivo, cantidad, cantidad_fisica_resultante, registrado_en)
VALUES
    (gen_random_uuid(), 'PROD-001', 'TIENDA-03-PORTALES', 'SALIDA', 'AJUSTE_MANUAL', 47, 3, now() - interval '5 days'),
    (gen_random_uuid(), 'PROD-005', 'TIENDA-04-PRADERA',  'SALIDA', 'AJUSTE_MANUAL', 50, 0, now() - interval '4 days');

CREATE TEMP TABLE seed_reserva (id text, orden_id text, ubicacion_id text, tipo_ubicacion text, estado text, creada_en timestamptz) ON COMMIT DROP;
INSERT INTO seed_reserva VALUES
    ('RSV-2026-000001', 'ORD-2026-000001', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'LIBERADA', now() - interval '20 days'),
    ('RSV-2026-000002', 'ORD-2026-000002', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'LIBERADA', now() - interval '3 days'),
    ('RSV-2026-000003', 'ORD-2026-000003', 'TIENDA-02-MIRAFLORES', 'TIENDA',              'LIBERADA', now() - interval '2 days'),
    ('RSV-2026-000004', 'ORD-2026-000004', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'LIBERADA', now() - interval '1 day'),
    ('RSV-2026-000005', 'ORD-2026-000005', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'ACTIVA',   now() - interval '6 hours'),
    ('RSV-2026-000006', 'ORD-2026-000006', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'ACTIVA',   now() - interval '2 hours'),
    ('RSV-2026-000007', 'ORD-2026-000007', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'LIBERADA', now() - interval '10 days'),
    ('RSV-2026-000008', 'ORD-2026-000008', 'TIENDA-01-CENTRAL',    'TIENDA',              'LIBERADA', now() - interval '15 days'),
    ('RSV-2026-000009', 'ORD-2026-000009', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'LIBERADA', now() - interval '4 days');

-- Las ACTIVAS vencen en 7 dias para que el job de expiracion no las cancele
-- apenas arranca el stack.
INSERT INTO inventario.reserva (id, orden_id, ubicacion_id, tipo_ubicacion, estado, ttl_segundos, creada_en, expira_en, secuencia)
SELECT id, orden_id, ubicacion_id, tipo_ubicacion, estado,
       CASE estado WHEN 'ACTIVA' THEN 604800 ELSE 900 END,
       creada_en,
       CASE estado WHEN 'ACTIVA' THEN now() + interval '7 days' ELSE creada_en + interval '15 minutes' END,
       CASE estado WHEN 'ACTIVA' THEN 1 ELSE 2 END
FROM seed_reserva;

INSERT INTO inventario.linea_reserva (id, reserva_id, sku, cantidad)
VALUES
    (gen_random_uuid(), 'RSV-2026-000001', 'PROD-002', 1),
    (gen_random_uuid(), 'RSV-2026-000002', 'PROD-004', 1),
    (gen_random_uuid(), 'RSV-2026-000002', 'PROD-007', 2),
    (gen_random_uuid(), 'RSV-2026-000003', 'PROD-006', 1),
    (gen_random_uuid(), 'RSV-2026-000004', 'PROD-005', 1),
    (gen_random_uuid(), 'RSV-2026-000005', 'PROD-003', 1),
    (gen_random_uuid(), 'RSV-2026-000006', 'PROD-008', 1),
    (gen_random_uuid(), 'RSV-2026-000007', 'PROD-001', 1),
    (gen_random_uuid(), 'RSV-2026-000008', 'PROD-007', 1),
    (gen_random_uuid(), 'RSV-2026-000009', 'PROD-002', 1),
    (gen_random_uuid(), 'RSV-2026-000009', 'PROD-004', 1);

-- Stock retenido por las reservas activas.
UPDATE inventario.existencia e
SET cantidad_reservada = e.cantidad_reservada + l.cantidad
FROM inventario.linea_reserva l
JOIN inventario.reserva r ON r.id = l.reserva_id AND r.estado = 'ACTIVA'
WHERE e.sku = l.sku AND e.ubicacion_id = r.ubicacion_id;

-- Que las reservas nuevas no choquen con los ids sembrados.
DO $$ BEGIN PERFORM setval('inventario.reserva_correlativo', GREATEST((SELECT last_value FROM inventario.reserva_correlativo), 9)); END $$;