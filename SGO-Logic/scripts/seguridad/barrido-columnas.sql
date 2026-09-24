-- Barrido generico de RES-08: busca hasta tres patrones (ILIKE, con sus
-- comodines % ya incluidos) en TODAS las columnas de tipo texto/jsonb de
-- TODOS los esquemas no-sistema de la base a la que este conectado psql,
-- sin asumir de antemano en que tabla deberia o no aparecer cada uno.
--
-- No se limita a las tablas "esperadas" (linea_orden, eventos_auditoria):
-- recorre information_schema.columns dinamicamente, asi que cubre cualquier
-- tabla/columna que exista hoy, incluidas las que se agreguen despues sin
-- tocar este script.
--
-- Uso (los patrones son opcionales; el que no se use se manda como cadena
-- que no puede matchear nada, '\x00nunca\x00'):
--   psql ... -v patron1='%NUMERO_PRUEBA=4111111111111111%' \
--            -v patron2='%CVV_PRUEBA=123%' \
--            -v patron3='%VENC_PRUEBA=12/29%' \
--            -tA -f barrido-columnas.sql
--
-- Salida: una fila por cada columna con al menos una coincidencia,
-- "esquema|tabla|columna|coincidencias_patron1|coincidencias_patron2|coincidencias_patron3".
-- Un valor de -1 en cualquiera de los tres significa que esa columna no se
-- pudo evaluar (por ejemplo, un tipo raro que no soporta el cast a texto) y
-- se salto sin abortar el barrido completo.

\set ON_ERROR_STOP on

-- psql NO sustituye ":'variable'" dentro de un bloque con comillas de dolar
-- (DO $$ ... $$ es opaco a su interpolacion de texto), asi que usar
-- ":'patron1'" directamente adentro del DO fallaba en el servidor con
-- "syntax error at or near ':'" -- verificado a mano contra un Postgres
-- limpio. Ese error quedaba invisible porque este archivo traia
-- "\set ON_ERROR_STOP off" (quitado arriba): con eso apagado, psql traga el
-- error, sigue de largo, y el SELECT final tambien falla en silencio porque
-- la tabla temporal nunca se llego a crear -- exit code 0, cero filas, sin
-- ningun aviso. La sustitucion SI ocurre fuera de comillas de dolar, asi que
-- los patrones se pasan a la sesion con SET (variables de configuracion
-- "custom", no requieren ninguna extension) y el DO los lee con
-- current_setting().
SET sgo_barrido.patron1 = :'patron1';
SET sgo_barrido.patron2 = :'patron2';
SET sgo_barrido.patron3 = :'patron3';

DO $$
DECLARE
    r RECORD;
    n1 BIGINT;
    n2 BIGINT;
    n3 BIGINT;
    patron1 TEXT := current_setting('sgo_barrido.patron1');
    patron2 TEXT := current_setting('sgo_barrido.patron2');
    patron3 TEXT := current_setting('sgo_barrido.patron3');
BEGIN
    DROP TABLE IF EXISTS pg_temp.resultados_barrido;
    CREATE TABLE pg_temp.resultados_barrido (
        esquema TEXT,
        tabla TEXT,
        columna TEXT,
        coincidencias_patron1 BIGINT,
        coincidencias_patron2 BIGINT,
        coincidencias_patron3 BIGINT
    );

    FOR r IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
          AND data_type IN ('text', 'character varying', 'character', 'json', 'jsonb')
        ORDER BY table_schema, table_name, column_name
    LOOP
        BEGIN
            EXECUTE format(
                'SELECT
                    count(*) FILTER (WHERE %I::text ILIKE %L),
                    count(*) FILTER (WHERE %I::text ILIKE %L),
                    count(*) FILTER (WHERE %I::text ILIKE %L)
                 FROM %I.%I',
                r.column_name, patron1,
                r.column_name, patron2,
                r.column_name, patron3,
                r.table_schema, r.table_name
            ) INTO n1, n2, n3;

            IF n1 > 0 OR n2 > 0 OR n3 > 0 THEN
                INSERT INTO pg_temp.resultados_barrido
                VALUES (r.table_schema, r.table_name, r.column_name, n1, n2, n3);
            END IF;
        EXCEPTION WHEN OTHERS THEN
            INSERT INTO pg_temp.resultados_barrido
            VALUES (r.table_schema, r.table_name, r.column_name, -1, -1, -1);
        END;
    END LOOP;
END $$;

SELECT esquema || '|' || tabla || '|' || columna || '|'
    || coincidencias_patron1 || '|' || coincidencias_patron2 || '|' || coincidencias_patron3
FROM pg_temp.resultados_barrido
ORDER BY esquema, tabla, columna;
