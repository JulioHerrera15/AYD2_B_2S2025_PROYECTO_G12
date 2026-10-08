-- Usuarios de demo. :hash es el BCrypt de '1234' que calcula sembrar.sh.
-- Ya verificados, para poder iniciar sesion sin el codigo por correo.
INSERT INTO auth.usuarios (id, email, hash_contrasena, nombre_completo, email_verificado, rol, creado_en)
VALUES
    ('11111111-0000-4000-8000-000000000001', 'admin@sgo.local',    :'hash', 'Administrador General',       true, 'ADMIN',    now() - interval '60 days'),
    ('11111111-0000-4000-8000-000000000002', 'admin2@sgo.local',   :'hash', 'Supervisora de Operaciones',  true, 'ADMIN',    now() - interval '60 days'),
    ('11111111-0000-4000-8000-000000000003', 'operador@sgo.local', :'hash', 'Operador de Tienda Central',  true, 'OPERADOR', now() - interval '45 days'),
    ('11111111-0000-4000-8000-000000000004', 'cliente1@sgo.local', :'hash', 'Ana Lucia Lopez',             true, 'CLIENTE',  now() - interval '30 days'),
    ('11111111-0000-4000-8000-000000000005', 'cliente2@sgo.local', :'hash', 'Carlos Mendez',               true, 'CLIENTE',  now() - interval '25 days'),
    ('11111111-0000-4000-8000-000000000006', 'cliente3@sgo.local', :'hash', 'Maria Jose Garcia',           true, 'CLIENTE',  now() - interval '20 days'),
    ('11111111-0000-4000-8000-000000000007', 'cliente4@sgo.local', :'hash', 'Luis Morales',                true, 'CLIENTE',  now() - interval '20 days'),
    ('11111111-0000-4000-8000-000000000008', 'cliente5@sgo.local', :'hash', 'Sofia Herrera',               true, 'CLIENTE',  now() - interval '15 days'),
    ('11111111-0000-4000-8000-000000000009', 'cliente6@sgo.local', :'hash', 'Jorge Castillo',              true, 'CLIENTE',  now() - interval '15 days')
ON CONFLICT (email) DO NOTHING;
