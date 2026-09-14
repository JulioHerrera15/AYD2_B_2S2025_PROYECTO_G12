#!/usr/bin/env bash
# Prueba de integración: simula, de forma simplificada, lo que sucede cuando
# Órdenes confirma una venta y esa confirmación debe:
#   1) propagarse al POS de la tienda de origen, y
#   2) quedar incluida en el siguiente lote de consolidación hacia el ERP.
#
# Requiere que ambos mocks ya estén corriendo:
#   docker compose up --build
#   (o) cd mock-erp && npm start   /   cd mock-pos && npm start

set -e
ERP="http://localhost:4000"
POS="http://localhost:4001"

ORDEN_ID="ORD-INT-001"
TIENDA="T07"
MONTO="480.00"

echo "=== 1) Propagar la venta al POS de la tienda de origen ($TIENDA) ==="
curl -s -X POST "$POS/pos/$TIENDA/eventos" -H "Content-Type: application/json" -d "{
  \"eventoId\": \"EVT-$ORDEN_ID\",
  \"ordenId\": \"$ORDEN_ID\",
  \"tipo\": \"VentaConfirmada\",
  \"canal\": \"tienda\",
  \"monto\": $MONTO,
  \"fecha\": \"2026-09-12T15:00:00Z\"
}"
echo -e "\n"

echo "=== 2) Incluir la misma venta en el lote de consolidación hacia el ERP ==="
curl -s -X POST "$ERP/erp/lotes" -H "Content-Type: application/json" -d "{
  \"loteId\": \"LOTE-INT-001\",
  \"secuencia\": 1,
  \"fechaGeneracion\": \"2026-09-12T15:30:00Z\",
  \"totalesControl\": {\"cantidadRegistros\": 1, \"montoTotal\": $MONTO},
  \"registros\": [
    {\"ordenId\": \"$ORDEN_ID\", \"canal\": \"tienda\", \"tienda\": \"$TIENDA\", \"monto\": $MONTO, \"fecha\": \"2026-09-12T15:00:00Z\"}
  ]
}"
echo -e "\n"

echo "=== 3) Verificar consistencia: la orden aparece reflejada en ambos lados ==="
echo "--- POS ($TIENDA) ---"
curl -s "$POS/pos/$TIENDA/eventos"
echo -e "\n"
echo "--- ERP (lote LOTE-INT-001) ---"
curl -s "$ERP/erp/lotes/LOTE-INT-001"
echo -e "\n"

echo "=== 4) Escenario de falla: la tienda está fuera de línea cuando llega la venta ==="
curl -s -X POST "$POS/pos/admin/T15/caida" -H "Content-Type: application/json" -d '{"activar": true, "tipo": "no_disponible", "duracionMs": 4000}' > /dev/null
echo "T15 desconectada por 4s."

echo "--- Intento de propagación (debe fallar 503) ---"
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST "$POS/pos/T15/eventos" -H "Content-Type: application/json" -d '{
  "eventoId": "EVT-ORD-INT-002",
  "ordenId": "ORD-INT-002",
  "tipo": "VentaConfirmada",
  "canal": "tienda",
  "monto": 199.00,
  "fecha": "2026-09-12T15:10:00Z"
}'
echo -e "\n"

echo "--- Mientras tanto, el ERP no se ve afectado (contextos independientes) ---"
curl -s -X POST "$ERP/erp/lotes" -H "Content-Type: application/json" -d '{
  "loteId": "LOTE-INT-002",
  "secuencia": 2,
  "fechaGeneracion": "2026-09-12T15:30:00Z",
  "totalesControl": {"cantidadRegistros": 1, "montoTotal": 199.00},
  "registros": [
    {"ordenId": "ORD-INT-002", "canal": "tienda", "tienda": "T15", "monto": 199.00, "fecha": "2026-09-12T15:10:00Z"}
  ]
}'
echo -e "\n"

echo "=== 5) Reintento a T15 tras reconectarse (5s de espera) ==="
sleep 5
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST "$POS/pos/T15/eventos" -H "Content-Type: application/json" -d '{
  "eventoId": "EVT-ORD-INT-002",
  "ordenId": "ORD-INT-002",
  "tipo": "VentaConfirmada",
  "canal": "tienda",
  "monto": 199.00,
  "fecha": "2026-09-12T15:10:00Z"
}'
echo -e "\n"

echo "Fin de la prueba de integración."
