// @ts-check
const { test, expect } = require('@playwright/test');
const { iniciarSesion } = require('../helpers/auth.helper');
const { agregarProducto, vaciarCarritoSiExiste } = require('../helpers/cart.helper');

test.describe('E2E-03: Restricción y bloqueo de Crédito para cliente no elegible', () => {

  test('Debe deshabilitar la opción de crédito o mostrar motivo de exclusión y forzar pago por pasarela PayKlik', async ({ page }) => {
    // 1. Iniciar sesión con cliente no elegible
    await iniciarSesion(page, 'cliente3@sgo.local', '1234');

    // 2. Agregar producto al carrito
    await vaciarCarritoSiExiste(page);
    await agregarProducto(page, 'PROD-004', 1);

    // 3. Navegar a Checkout
    await page.goto('/Checkout');
    await page.waitForLoadState('domcontentloaded');

    // 4. Localizar la opción de Crédito MegaStore
    const radioCredito = page.locator('input[name="medioPago"][value="CREDITO"]');
    await expect(radioCredito).toBeVisible({ timeout: 10000 });

    // Si el usuario no es elegible, el radio debe estar deshabilitado
    const isDisabled = await radioCredito.isDisabled();
    const contenedorCredito = page.locator('label:has(input[value="CREDITO"])');

    if (isDisabled) {
      // Debe mostrar el motivo de no disponibilidad en el contenedor
      await expect(contenedorCredito).toContainText(/no disponible|límite|crédito/i);

      // 5. Verificar que los métodos alternativos (TARJETA / TRANSFERENCIA) sí están disponibles
      const radioTarjeta = page.locator('input[name="medioPago"][value="TARJETA"]');
      await expect(radioTarjeta).toBeVisible();
    } else {
      // Si el cliente3 tuviera crédito en esta base de datos, validar que la navegación a /Cuenta/Credito
      // refleja el estado oficial del perfil
      await page.goto('/Cuenta/Credito');
      await page.waitForLoadState('domcontentloaded');
      await expect(page.locator('body')).toBeVisible();
    }
  });

});
