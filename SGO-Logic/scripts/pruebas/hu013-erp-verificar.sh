#!/usr/bin/env bash
set -euo pipefail

ERP_URL="${ERP_URL:-http://localhost:4000}"
LOTE_ID="HU013-$(date +%s)"

curl -fsS -X POST "$ERP_URL/erp/admin/reset" >/dev/null
curl -fsS -X POST "$ERP_URL/erp/lotes" -H 'Content-Type: application/json' -d "{\"loteId\":\"$LOTE_ID\",\"secuencia\":1,\"fechaGeneracion\":\"2026-09-20T12:00:00Z\",\"totalesControl\":{\"cantidadRegistros\":2,\"montoTotal\":125.75},\"registros\":[{\"ordenId\":\"ORD-HU013-1\",\"canal\":\"WEB\",\"tienda\":\"T01\",\"monto\":100.50,\"fecha\":\"2026-09-20T11:00:00Z\"},{\"ordenId\":\"ORD-HU013-2\",\"canal\":\"TIENDA\",\"tienda\":\"T02\",\"monto\":25.25,\"fecha\":\"2026-09-20T11:05:00Z\"}]}" | grep -q 'ACEPTADO'
codigo_duplicado="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$ERP_URL/erp/lotes" -H 'Content-Type: application/json' -d "{\"loteId\":\"$LOTE_ID\",\"secuencia\":1,\"fechaGeneracion\":\"2026-09-20T12:00:00Z\",\"totalesControl\":{\"cantidadRegistros\":2,\"montoTotal\":125.75},\"registros\":[]}")"
[[ "$codigo_duplicado" == "409" ]]
echo "HU-013 mock ERP: OK"
