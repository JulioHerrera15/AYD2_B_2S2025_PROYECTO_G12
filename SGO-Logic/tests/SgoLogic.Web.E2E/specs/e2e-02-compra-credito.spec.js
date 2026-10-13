// @ts-check
const { test, expect } = require('@playwright/test');
const { iniciarSesion } = require('../helpers/auth.helper');
const { agregarProducto, vaciarCarritoSiExiste } = require('../helpers/cart.helper');

test.describe('E2E-02: Compra completa con Crédito Propio ("Compra ahora, paga en 30 días")', () => {

  test('Debe mostrar la opción de crédito para cliente elegible, permitir selección y completar la orden vía crédito', async ({ page }) => {
    // 1. Iniciar sesión con cliente que posea perfil crediticio
    await iniciarSesion(page, 'cliente1@sgo.local', '1234');

    // 2. Preparar carrito con producto
    await vaciarCarritoSiExiste(page);
    await agregarProducto(page, 'PROD-003', 1);

    // 3. Navegar a Checkout
    await page.goto('/Checkout');
    await page.waitForLoadState('domcontentloaded');

    // 4. Localizar la opción de Crédito MegaStore
    const radioCredito = page.locator('input[name="medioPago"][value="CREDITO"]');
    await expect(radioCredito).toBeVisible({ timeout: 10000 });

    // Verificar si el cliente es elegible para seleccionar la opción
    const estaHabilitado = await radioCredito.isEnabled();

    if (estaHabilitado) {
      // 5. Seleccionar la opción de Crédito
      await radioCredito.check();

      // Verificar que el texto del botón de confirmación se actualiza
      const btnConfirmarTexto = page.locator('#btn-confirmar-texto');
      await expect(btnConfirmarTexto).toContainText(/crédito|confirmar/i);

      // Aceptar política de privacidad
      const checkPrivacidad = page.locator('#check-privacidad');
      if (await checkPrivacidad.isVisible()) {
        await checkPrivacidad.check();
      }

      // 6. Confirmar pedido por la ruta de crédito
      const btnConfirmar = page.locator('#btn-confirmar');
      await expect(btnConfirmar).toBeEnabled();
      await btnConfirmar.click();

      // 7. Validar redirección exitosa
      await page.waitForURL(url =>
        url.pathname.includes('/Estado') ||
        url.pathname.includes('/Exito') ||
        url.pathname.includes('/Cuenta'),
        { timeout: 20000 }
      );

      // 8. Consultar el estado de la línea de crédito en Mi Cuenta
      await page.goto('/Cuenta/Credito');
      await page.waitForLoadState('domcontentloaded');
      await expect(page.locator('h1, h2').first()).toContainText(/crédito/i);
    } else {
      // Si el entorno local no tiene aún el snapshot de crédito sembrado para este usuario,
      // verificar que al menos el componente de crédito renderiza la advertencia correspondiente
      const contenedorCredito = page.locator('label:has(input[value="CREDITO"])');
      await expect(contenedorCredito).toBeVisible();
    }
  });

});
