#!/usr/bin/env bash

set -e
BASE="http://localhost:4101"

echo "=== 1) Evento a T05 (ACEPTADO) ==="
curl -s -X POST "$BASE/pos/T05/eventos" -H "Content-Type: application/json" -d '{
  "eventoId": "EVT-DEMO-001",
  "ordenId": "ORD-9001",
  "tipo": "VentaConfirmada",
  "canal": "web",
  "monto": 450.00,
  "fecha": "2026-09-10T14:00:00Z"
}'
echo -e "\n"

echo "=== 2) Mismo evento reenviado a T05 (DUPLICADO, no se reprocesa) ==="
curl -s -X POST "$BASE/pos/T05/eventos" -H "Content-Type: application/json" -d '{
  "eventoId": "EVT-DEMO-001",
  "ordenId": "ORD-9001",
  "tipo": "VentaConfirmada",
  "canal": "web",
  "monto": 450.00,
  "fecha": "2026-09-10T14:00:00Z"
}'
echo -e "\n"

echo "=== 3) Desconectar T12 (no_disponible, 5s) ==="
curl -s -X POST "$BASE/pos/admin/T12/caida" -H "Content-Type: application/json" -d '{"activar": true, "tipo": "no_disponible", "duracionMs": 5000}'
echo -e "\n"

echo "=== 4) Intento de enviar evento a T12 mientras está fuera de línea (503) ==="
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST "$BASE/pos/T12/eventos" -H "Content-Type: application/json" -d '{
  "eventoId": "EVT-DEMO-002",
  "ordenId": "ORD-9002",
  "tipo": "VentaConfirmada",
  "canal": "tienda",
  "monto": 120.00,
  "fecha": "2026-09-10T14:05:00Z"
}'
echo -e "\n"

echo "=== 5) Reintento tras esperar 6s a que T12 se reconecte (ACEPTADO) ==="
sleep 6
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST "$BASE/pos/T12/eventos" -H "Content-Type: application/json" -d '{
  "eventoId": "EVT-DEMO-002",
  "ordenId": "ORD-9002",
  "tipo": "VentaConfirmada",
  "canal": "tienda",
  "monto": 120.00,
  "fecha": "2026-09-10T14:05:00Z"
}'
echo -e "\n"

echo "=== 6) Caída masiva regional (T20, T21, T22 offline sin expiración) ==="
curl -s -X POST "$BASE/pos/admin/caida-masiva" -H "Content-Type: application/json" -d '{"tiendas": ["T20","T21","T22"], "activar": true, "tipo": "error500"}'
echo -e "\n"

echo "=== 7) Resumen de las 32 tiendas ==="
curl -s "$BASE/pos"
echo -e "\n"

echo "=== 8) Reconectar T20, T21, T22 ==="
curl -s -X POST "$BASE/pos/admin/caida-masiva" -H "Content-Type: application/json" -d '{"tiendas": ["T20","T21","T22"], "activar": false}'
echo -e "\n"

echo "=== 9) Estado final de T05 ==="
curl -s "$BASE/pos/T05/estado"
echo
