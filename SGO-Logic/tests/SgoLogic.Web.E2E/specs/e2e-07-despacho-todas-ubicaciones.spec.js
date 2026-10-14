// @ts-check
const { test, expect } = require('@playwright/test');
const { iniciarSesion } = require('../helpers/auth.helper');

test.describe('E2E-07: Panel de despacho con todas las ubicaciones', () => {

  test('Con sede "Todas" debe listar despachos de ubicaciones distintas a CD-01 y Tienda Central', async ({ page }) => {
    // 1. Iniciar sesión con el administrador (personal autorizado para despacho)
    await iniciarSesion(page, 'admin@sgo.local', '1234');

    // 2. Abrir el panel de despacho con la sede "Todas"
    await page.goto('/Despacho?ubicacionId=TODAS');
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('h1').first()).toContainText(/despacho/i);
    await expect(page.locator('[data-testid="despacho-error"]')).toHaveCount(0);

    // 3. El seed tiene despachos con origen CD-CENTRAL y TIENDA-02-MIRAFLORES. Antes
    //    del arreglo la Web solo consultaba CD-01 y TIENDA-01-CENTRAL y no aparecian.
    const filas = page.locator('table tbody tr');
    await expect(filas.first()).toBeVisible();

    const destinos = await filas.locator('td:nth-child(3)').allInnerTexts();
    const fueraDeLasFijas = destinos.filter(d => !/CD-01\b|Tienda Central|TIENDA-01-CENTRAL/i.test(d));
    expect(fueraDeLasFijas.length).toBeGreaterThan(0);

    // 4. El selector de sede se arma con los orígenes reales, no con valores fijos
    await expect(page.locator('a[data-sede="TODAS"]')).toBeVisible();
    await expect(page.locator('a[data-sede="CD-CENTRAL"]')).toBeVisible();
  });

});
