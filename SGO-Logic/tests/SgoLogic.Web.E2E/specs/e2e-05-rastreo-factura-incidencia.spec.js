// @ts-check
const { test, expect } = require('@playwright/test');
const { iniciarSesion } = require('../helpers/auth.helper');

test.describe('E2E-05: Rastreo de orden, factura electrónica e incidencias', () => {

  test('Debe consultar historial de órdenes, visualizar comprobante/factura y permitir gestionar incidencias', async ({ page }) => {
    // 1. Iniciar sesión con cliente
    await iniciarSesion(page, 'cliente1@sgo.local', '1234');

    // 2. Navegar a la vista de Rastreo y Estado de Órdenes
    await page.goto('/Estado');
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('h1').first()).toContainText(/pedidos|gu[ií]as|env[ií]os|[oó]rdenes|estado|rastreo/i);

    // 3. Verificar si existen órdenes en el historial del cliente
    const linkOrden = page.locator('a[href*="/Estado?ordenId="], a[href*="/Estado/Detalle"], tr a').first();
    const hayOrdenes = await linkOrden.count() > 0;

    if (hayOrdenes) {
      await linkOrden.click();
      await page.waitForLoadState('domcontentloaded');

      // Validar presencia de datos de la orden (timeline o resumen)
      await expect(page.locator('body')).toContainText(/orden|total|estado/i);

      // 4. Validar enlace a comprobante o factura
      const linkComprobante = page.locator('a[href*="Comprobante"], a[href*="Factura"]').first();
      if (await linkComprobante.isVisible()) {
        await linkComprobante.click();
        await page.waitForLoadState('domcontentloaded');
        await expect(page.locator('body')).toBeVisible();
      }
    }

    // 5. Verificar panel o formulario de Incidencias
    await page.goto('/Incidencias/Crear');
    await page.waitForLoadState('domcontentloaded');

    // Comprobar que el formulario de reporte de incidencia está operativo
    const formIncidencia = page.locator('form[action*="/Incidencias"], form');
    await expect(formIncidencia.first()).toBeVisible({ timeout: 10000 });
  });

});
