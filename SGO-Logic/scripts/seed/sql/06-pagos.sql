-- Un pago por orden sembrada, con sus intentos contra PayKlik. La orden 6 queda
-- PENDIENTE con enlace de reintento vigente y la 7 RECHAZADA.

CREATE TEMP TABLE seed_pago (
    n int, cliente_id text, canal text, medio_pago text, monto numeric(18,2), estado text, creado_hace interval
) ON COMMIT DROP;

INSERT INTO seed_pago VALUES
    (1, 'cliente1@sgo.local', 'WEB',         'TARJETA',            6158.88, 'APLICADO',  '20 days'),
    (2, 'cliente1@sgo.local', 'WEB',         'TARJETA',            7948.64, 'APLICADO',  '3 days'),
    (3, 'cliente2@sgo.local', 'MOVIL',       'BILLETERA_DIGITAL',  5150.88, 'APLICADO',  '2 days'),
    (4, 'cliente2@sgo.local', 'WEB',         'TARJETA',           18478.88, 'APLICADO',  '1 day'),
    (5, 'cliente3@sgo.local', 'CALL_CENTER', 'TRANSFERENCIA',      8846.88, 'APLICADO',  '6 hours'),
    (6, 'cliente3@sgo.local', 'WEB',         'TARJETA',            4366.88, 'PENDIENTE', '2 hours'),
    (7, 'cliente1@sgo.local', 'WEB',         'TARJETA',           16798.88, 'RECHAZADO', '10 days'),
    (8, 'cliente2@sgo.local', 'TIENDA',      'TARJETA',            2126.88, 'APLICADO',  '15 days'),
    (9, 'cliente3@sgo.local', 'WEB',         'TARJETA',            9853.76, 'APLICADO',  '4 days');

INSERT INTO pagos.pago (id, orden_id, cliente_id, canal, medio_pago, moneda, monto, estado, secuencia, creado_en, actualizado_en)
SELECT
    format('a0000000-0000-4000-8000-%s', lpad(n::text, 12, '0'))::uuid,
    format('ORD-2026-%s', lpad(n::text, 6, '0')),
    cliente_id, canal, medio_pago, 'GTQ', monto, estado,
    2,
    now() - creado_hace + interval '2 minutes',
    now() - creado_hace + interval '3 minutes'
FROM seed_pago;

INSERT INTO pagos.intento_pago (
    id, pago_id, intento_numero, medio_pago, moneda, monto, token_medio_pago, marca, ultimos_cuatro,
    estado, autorizacion_id, codigo_respuesta, mensaje_respuesta, latencia_ms, iniciado_en, finalizado_en)
SELECT
    gen_random_uuid(),
    format('a0000000-0000-4000-8000-%s', lpad(n::text, 12, '0'))::uuid,
    1, medio_pago, 'GTQ', monto,
    CASE WHEN medio_pago = 'TARJETA' THEN 'tok_demo_visa_4242' END,
    CASE WHEN medio_pago = 'TARJETA' THEN 'VISA' END,
    CASE WHEN medio_pago = 'TARJETA' THEN '4242' END,
    CASE estado WHEN 'APLICADO' THEN 'EXITOSO' WHEN 'PENDIENTE' THEN 'FALLIDO' ELSE 'RECHAZADO' END,
    CASE WHEN estado = 'APLICADO' THEN format('PK-AUT-%s', lpad(n::text, 8, '0')) END,
    CASE estado WHEN 'APLICADO' THEN '00' WHEN 'PENDIENTE' THEN 'TIMEOUT' ELSE '51' END,
    CASE estado WHEN 'APLICADO' THEN 'Aprobado' WHEN 'PENDIENTE' THEN 'PayKlik no respondio a tiempo' ELSE 'Fondos insuficientes' END,
    CASE estado WHEN 'PENDIENTE' THEN 5000 ELSE 350 + n * 20 END,
    now() - creado_hace + interval '2 minutes',
    now() - creado_hace + interval '3 minutes'
FROM seed_pago;

INSERT INTO pagos.enlace_reintento (id, orden_id, token, creado_en, expira_en, usado)
VALUES (gen_random_uuid(), 'ORD-2026-000006', 'reintento-demo-000006', now() - interval '1 hour 45 minutes', now() + interval '22 hours', false);