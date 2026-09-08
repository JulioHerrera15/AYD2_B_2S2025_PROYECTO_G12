-- Base y rol propios del contexto Ordenes (ADR-001, aislamiento por contexto)
CREATE ROLE ordenes_app WITH LOGIN PASSWORD 'ordenes_dev';
CREATE DATABASE sgo_ordenes OWNER ordenes_app;

\c sgo_ordenes

CREATE SCHEMA IF NOT EXISTS ordenes AUTHORIZATION ordenes_app;
GRANT ALL ON SCHEMA ordenes TO ordenes_app;