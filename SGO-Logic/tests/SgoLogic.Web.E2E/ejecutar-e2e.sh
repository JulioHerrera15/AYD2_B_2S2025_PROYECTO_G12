#!/usr/bin/env bash
set -e

# ==============================================================================
# Script de Ejecución de Pruebas E2E (Playwright) — SGO-Logic (Jonathan)
# ==============================================================================

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$DIR"

echo "======================================================================"
echo " Ejecutando Suite de Pruebas E2E (Playwright) - MegaStore Web"
echo "======================================================================"

if [ ! -d "node_modules" ]; then
  echo "Instalando dependencias de prueba (Playwright)..."
  npm install
fi

# Asegurar navegador Chromium instalado
echo "Verificando binarios de navegador..."
npx playwright install chromium 2>/dev/null || true

# Ejecutar pruebas
echo "Ejecutando las 5 pruebas E2E..."
npx playwright test "$@"

echo "======================================================================"
echo " Ejecución de pruebas E2E completada con éxito."
echo "======================================================================"
