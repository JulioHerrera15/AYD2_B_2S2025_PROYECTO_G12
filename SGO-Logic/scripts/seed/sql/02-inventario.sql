-- Existencias del catalogo web (PROD-001..008) en los dos CD y las 32 tiendas,
-- su movimiento de carga inicial y una reserva por cada orden sembrada.
-- Las reservas de ordenes pagadas que aun no salen a ruta (ORD-...04 y 05) quedan
-- CONFIRMADAS y la de pago pendiente (ORD-...06) ACTIVA: las tres retienen stock
-- en CD-CENTRAL. Las ordenes ya en ruta o entregadas tienen su reserva ENTREGADA
-- y la cancelada, LIBERADA.

INSERT INTO inventario.existencia (id, sku, ubicacion_id, tipo_ubicacion, cantidad_fisica, cantidad_reservada, version)
SELECT gen_random_uuid(), sku, ubicacion_id, tipo_ubicacion, cantidad, 0, 0
FROM unnest(ARRAY['PROD-001','PROD-002','PROD-003','PROD-004','PROD-005','PROD-006','PROD-007','PROD-008']) AS sku
CROSS JOIN (
    VALUES
        ('CD-01', 'CENTRO_DISTRIBUCION', 100),
        ('CD-CENTRAL', 'CENTRO_DISTRIBUCION', 100)
    UNION ALL
    SELECT format('TIENDA-%s-%s', to_char(n, 'FM00'), CASE n
        WHEN 1 THEN 'CENTRAL' WHEN 2 THEN 'MIRAFLORES' WHEN 3 THEN 'PORTALES'
        WHEN 4 THEN 'PRADERA' WHEN 5 THEN 'OAKLAND' WHEN 6 THEN 'NARANJO'
        WHEN 7 THEN 'CAYALA' WHEN 8 THEN 'ROOSEVELT' WHEN 9 THEN 'FONTABELLA'
        WHEN 10 THEN 'METRONORTE' WHEN 11 THEN 'MAJADAS' WHEN 12 THEN 'AMERICAS'
        WHEN 13 THEN 'SANCRISTOBAL' WHEN 14 THEN 'VILLANUEVA' WHEN 15 THEN 'CARRETERA'
        WHEN 16 THEN 'CENTRALNORTE' WHEN 17 THEN 'XELA' WHEN 18 THEN 'INTERPLAZA'
        WHEN 19 THEN 'ANTIGUA' WHEN 20 THEN 'ESCUINTLA' WHEN 21 THEN 'MAZATENANGO'
        WHEN 22 THEN 'COBAN' WHEN 23 THEN 'HUEHUETENANGO' WHEN 24 THEN 'CHIMALTENANGO'
        WHEN 25 THEN 'ZACAPA' WHEN 26 THEN 'CHIQUIMULA' WHEN 27 THEN 'PUERTOBARRIOS'
        WHEN 28 THEN 'PETEN' WHEN 29 THEN 'RETALHULEU' WHEN 30 THEN 'COATEPEQUE'
        WHEN 31 THEN 'JUTIAPA' ELSE 'SALAMA' END), 'TIENDA', 50
    FROM generate_series(1, 32) AS n
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
    ('RSV-2026-000001', 'ORD-2026-000001', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'ENTREGADA', now() - interval '20 days'),
    ('RSV-2026-000002', 'ORD-2026-000002', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'ENTREGADA', now() - interval '3 days'),
    ('RSV-2026-000003', 'ORD-2026-000003', 'TIENDA-02-MIRAFLORES', 'TIENDA',              'ENTREGADA', now() - interval '2 days'),
    ('RSV-2026-000004', 'ORD-2026-000004', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'CONFIRMADA', now() - interval '1 day'),
    ('RSV-2026-000005', 'ORD-2026-000005', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'CONFIRMADA', now() - interval '6 hours'),
    ('RSV-2026-000006', 'ORD-2026-000006', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'ACTIVA',   now() - interval '2 hours'),
    ('RSV-2026-000007', 'ORD-2026-000007', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'LIBERADA', now() - interval '10 days'),
    ('RSV-2026-000008', 'ORD-2026-000008', 'TIENDA-01-CENTRAL',    'TIENDA',              'ENTREGADA', now() - interval '15 days'),
    ('RSV-2026-000009', 'ORD-2026-000009', 'CD-CENTRAL',           'CENTRO_DISTRIBUCION', 'ENTREGADA', now() - interval '4 days');

-- Las que retienen stock vencen en 7 dias para que el job de expiracion no las
-- cancele apenas arranca el stack (las CONFIRMADAS ademas nunca expiran).
INSERT INTO inventario.reserva (id, orden_id, ubicacion_id, tipo_ubicacion, estado, ttl_segundos, creada_en, expira_en, secuencia)
SELECT id, orden_id, ubicacion_id, tipo_ubicacion, estado,
       CASE WHEN estado IN ('ACTIVA', 'CONFIRMADA') THEN 604800 ELSE 900 END,
       creada_en,
       CASE WHEN estado IN ('ACTIVA', 'CONFIRMADA') THEN now() + interval '7 days' ELSE creada_en + interval '15 minutes' END,
       CASE WHEN estado IN ('ACTIVA', 'CONFIRMADA') THEN 1 ELSE 2 END
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

-- Stock retenido por las reservas vigentes (ACTIVA o CONFIRMADA). Las lineas de
-- una misma existencia se suman antes de actualizar: un UPDATE ... FROM con varias
-- filas por destino solo aplicaria una.
UPDATE inventario.existencia e
SET cantidad_reservada = e.cantidad_reservada + t.cantidad
FROM (
    SELECT l.sku, r.ubicacion_id, sum(l.cantidad) AS cantidad
    FROM inventario.linea_reserva l
    JOIN inventario.reserva r ON r.id = l.reserva_id AND r.estado IN ('ACTIVA', 'CONFIRMADA')
    GROUP BY l.sku, r.ubicacion_id
) t
WHERE e.sku = t.sku AND e.ubicacion_id = t.ubicacion_id;

-- Que las reservas nuevas no choquen con los ids sembrados.
DO $$ BEGIN PERFORM setval('inventario.reserva_correlativo', GREATEST((SELECT last_value FROM inventario.reserva_correlativo), 9)); END $$;