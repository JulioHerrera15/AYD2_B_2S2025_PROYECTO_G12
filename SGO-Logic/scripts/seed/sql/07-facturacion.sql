-- Documentos fiscales (serie A) de las ordenes que llegaron a facturarse. El de
-- la orden 5 quedo en ALERTA porque SAT no respondio.

CREATE TEMP TABLE seed_documento (
    correlativo int, n int, nombre text, direccion text, estado text,
    subtotal numeric(18,2), impuestos numeric(18,2), creado_hace interval
) ON COMMIT DROP;

INSERT INTO seed_documento VALUES
    (1, 1, 'Ana Lucia Lopez',   '6a Avenida 12-34 Zona 10, Guatemala',       'CERTIFICADO',  4909.82, 589.18, '20 days'),
    (2, 2, 'Ana Lucia Lopez',   '6a Avenida 12-34 Zona 10, Guatemala',       'CERTIFICADO',  6336.61, 760.39, '3 days'),
    (3, 3, 'Carlos Mendez',     'Ciudad',                                    'CERTIFICADO',  4106.25, 492.75, '2 days'),
    (4, 4, 'Carlos Mendez',     'Calzada Roosevelt 22-43 Zona 11, Mixco',    'CERTIFICADO', 14731.25, 1767.75, '1 day'),
    (5, 5, 'Maria Jose Garcia', '4a Calle 5-67 Zona 1, Quetzaltenango',      'ALERTA',       7052.68, 846.32, '6 hours'),
    (6, 8, 'Carlos Mendez',     'Ciudad',                                    'CERTIFICADO',  1695.54, 203.46, '15 days'),
    (7, 9, 'Maria Jose Garcia', '4a Calle 5-67 Zona 1, Quetzaltenango',      'CERTIFICADO',  7855.36, 942.64, '4 days');

INSERT INTO facturacion.documento_fiscal (
    id, orden_id, transaction_id, tipo_documento, serie, correlativo, numero_documento, estado, moneda,
    subtotal, impuestos, total, nombre_receptor, cui_receptor, direccion_receptor, pais_receptor, xml_generado,
    folio, numero_autorizacion, intentos, ultimo_codigo, ultimo_motivo, proximo_intento_en, certificado_en,
    documento_origen_id, secuencia, creado_en, actualizado_en)
SELECT
    format('b0000000-0000-4000-8000-%s', lpad(correlativo::text, 12, '0'))::uuid,
    format('ORD-2026-%s', lpad(n::text, 6, '0')),
    format('seed-tx-%s', lpad(n::text, 6, '0')),
    'FACTURA', 'A', correlativo, lpad(correlativo::text, 8, '0'), estado, 'GTQ',
    subtotal, impuestos, subtotal + impuestos,
    nombre, 'CF', direccion, 'GT',
    format('<dte:DTE serie="A" numero="%s"><Receptor nit="CF">%s</Receptor><Total moneda="GTQ">%s</Total></dte:DTE>',
           lpad(correlativo::text, 8, '0'), nombre, subtotal + impuestos),
    CASE WHEN estado = 'CERTIFICADO' THEN format('SAT-FOLIO-2026-%s', lpad(correlativo::text, 8, '0')) END,
    CASE WHEN estado = 'CERTIFICADO' THEN format('AUT-%s-2026', lpad(correlativo::text, 10, '0')) END,
    CASE WHEN estado = 'CERTIFICADO' THEN 1 ELSE 3 END,
    CASE WHEN estado = 'CERTIFICADO' THEN 'CERTIFICADO' ELSE 'TIMEOUT' END,
    CASE WHEN estado = 'ALERTA' THEN 'SAT no respondio tras 3 intentos de certificacion' END,
    NULL,
    CASE WHEN estado = 'CERTIFICADO' THEN now() - creado_hace + interval '10 minutes' END,
    NULL,
    CASE WHEN estado = 'CERTIFICADO' THEN 2 ELSE 4 END,
    now() - creado_hace + interval '5 minutes',
    now() - creado_hace + interval '10 minutes'
FROM seed_documento;

INSERT INTO facturacion.documento_fiscal_linea (id, documento_fiscal_id, numero_linea, sku, descripcion, cantidad, precio_unitario, impuesto)
VALUES
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000001', 1, 'PROD-002', 'Smart Ultra HD LED TV 65"',                1,  5499.00,   589.18),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000002', 1, 'PROD-004', 'Barra de Sonido Dolby Atmos 5.1.2 550W',   1,  3299.00,   353.46),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000002', 2, 'PROD-007', 'Microondas Grill Smart Inverter 42L',      2,  1899.00,   406.93),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000003', 1, 'PROD-006', 'Aire Acondicionado Inverter 18000 BTU',    1,  4599.00,   492.75),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000004', 1, 'PROD-005', 'Laptop Gamer Ultra 16" Ryzen 9',           1, 16499.00, 1767.75),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000005', 1, 'PROD-003', 'Lavadora de Carga Frontal EcoBubble 22kg', 1,  7899.00,   846.32),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000006', 1, 'PROD-007', 'Microondas Grill Smart Inverter 42L',      1,  1899.00,   203.46),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000007', 1, 'PROD-002', 'Smart Ultra HD LED TV 65"',                1,  5499.00,   589.18),
    (gen_random_uuid(), 'b0000000-0000-4000-8000-000000000007', 2, 'PROD-004', 'Barra de Sonido Dolby Atmos 5.1.2 550W',   1,  3299.00,   353.46);

DO $$ BEGIN PERFORM setval('facturacion.documento_fiscal_correlativo', GREATEST((SELECT last_value FROM facturacion.documento_fiscal_correlativo), 7)); END $$;