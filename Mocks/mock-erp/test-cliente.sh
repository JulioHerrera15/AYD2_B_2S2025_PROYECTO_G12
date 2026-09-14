#!/usr/bin/env bash

set -e
BASE="http://localhost:4000"

echo "=== 1) Lote JSON correcto (ACEPTADO) ==="
curl -s -X POST "$BASE/erp/lotes" -H "Content-Type: application/json" -d '{
  "loteId": "LOTE-DEMO-001",
  "secuencia": 1,
  "fechaGeneracion": "2026-09-10T14:00:00Z",
  "totalesControl": {"cantidadRegistros": 3, "montoTotal": 960.00},
  "registros": [
    {"ordenId":"ORD-1001","canal":"web","tienda":"T01","monto":320.00,"fecha":"2026-09-10T13:45:00Z"},
    {"ordenId":"ORD-1002","canal":"tienda","tienda":"T02","monto":320.00,"fecha":"2026-09-10T13:50:00Z"},
    {"ordenId":"ORD-1003","canal":"app","tienda":"T01","monto":320.00,"fecha":"2026-09-10T13:55:00Z"}
  ]
}'
echo -e "\n"

echo "=== 2) Mismo lote reenviado (DUPLICADO, idempotencia) ==="
curl -s -X POST "$BASE/erp/lotes" -H "Content-Type: application/json" -d '{
  "loteId": "LOTE-DEMO-001",
  "secuencia": 1,
  "fechaGeneracion": "2026-09-10T14:00:00Z",
  "totalesControl": {"cantidadRegistros": 3, "montoTotal": 960.00},
  "registros": [{"ordenId":"ORD-1001","canal":"web","tienda":"T01","monto":320.00,"fecha":"2026-09-10T13:45:00Z"}]
}'
echo -e "\n"

echo "=== 3) Lote CSV con descuadre de montos (RECHAZADO_CUADRE) ==="
printf 'LOTE-DEMO-002,2,2026-09-10T14:30:00Z,2,999.99\nORDEN,CANAL,TIENDA,MONTO,FECHA\nORD-2001,web,T03,320.00,2026-09-10T14:15:00Z\nORD-2002,app,T04,320.00,2026-09-10T14:20:00Z\n' > /tmp/lote_descuadre.csv
curl -s -X POST "$BASE/erp/lotes" -H "Content-Type: text/csv" --data-binary @/tmp/lote_descuadre.csv
echo -e "\n"

echo "=== 4) Lote XML correcto (ACEPTADO) ==="
curl -s -X POST "$BASE/erp/lotes" -H "Content-Type: application/xml" --data-binary '<Lote id="LOTE-DEMO-003" secuencia="2" fecha="2026-09-10T15:00:00Z">
  <TotalesControl cantidadRegistros="2" montoTotal="640.00"/>
  <Registros>
    <Orden id="ORD-3001" canal="web" tienda="T01" monto="320.00" fecha="2026-09-10T14:45:00Z"/>
    <Orden id="ORD-3002" canal="tienda" tienda="T02" monto="320.00" fecha="2026-09-10T14:50:00Z"/>
  </Registros>
</Lote>'
echo -e "\n"

echo "=== 5) Activar caída provocada (error500, 5s) ==="
curl -s -X POST "$BASE/erp/admin/caida" -H "Content-Type: application/json" -d '{"activar": true, "tipo": "error500", "duracionMs": 5000}'
echo -e "\n"

echo "=== 6) Intento de envío durante la caída (debe fallar HTTP 500) ==="
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST "$BASE/erp/lotes" -H "Content-Type: application/json" -d '{
  "loteId": "LOTE-DEMO-004",
  "secuencia": 3,
  "fechaGeneracion": "2026-09-10T15:30:00Z",
  "totalesControl": {"cantidadRegistros": 1, "montoTotal": 100.00},
  "registros": [{"ordenId":"ORD-4001","canal":"web","tienda":"T01","monto":100.00,"fecha":"2026-09-10T15:25:00Z"}]
}'
echo -e "\n"

echo "=== 7) Esperar que expire la caída (6s) y reintentar el mismo lote (ACEPTADO) ==="
sleep 6
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST "$BASE/erp/lotes" -H "Content-Type: application/json" -d '{
  "loteId": "LOTE-DEMO-004",
  "secuencia": 3,
  "fechaGeneracion": "2026-09-10T15:30:00Z",
  "totalesControl": {"cantidadRegistros": 1, "montoTotal": 100.00},
  "registros": [{"ordenId":"ORD-4001","canal":"web","tienda":"T01","monto":100.00,"fecha":"2026-09-10T15:25:00Z"}]
}'
echo -e "\n"

echo "=== 8) Estado final (bitácora + lotes) ==="
curl -s "$BASE/erp/admin/estado"
echo -e "\n"
curl -s "$BASE/erp/lotes"
echo
