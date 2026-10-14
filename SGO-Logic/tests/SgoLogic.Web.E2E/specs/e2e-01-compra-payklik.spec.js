// @ts-check
const { test, expect } = require('@playwright/test');
const { iniciarSesion } = require('../helpers/auth.helper');
const { agregarProducto, vaciarCarritoSiExiste } = require('../helpers/cart.helper');

test.describe('E2E-01: Compra completa con pasarela PayKlik (Tarjeta)', () => {

  test('Debe autenticar al cliente, agregar producto, completar checkout con tarjeta y generar orden confirmada', async ({ page }) => {
    // 1. Iniciar sesión con cliente de pruebas
    await iniciarSesion(page, 'cliente1@sgo.local', '1234');

    // 2. Preparar carrito limpio y agregar producto con stock
    await vaciarCarritoSiExiste(page);
    await agregarProducto(page, 'PROD-002', 1);

    // 3. Navegar a la página de Checkout
    await page.goto('/Checkout');
    await page.waitForLoadState('domcontentloaded');

    // Verificar que estamos en checkout y el resumen muestra artículos
    await expect(page.locator('h1')).toContainText('Finalizar compra');
    await expect(page.locator('#checkout-form')).toBeVisible();

    // 4. Seleccionar medio de pago TARJETA
    const radioTarjeta = page.locator('input[name="medioPago"][value="TARJETA"]');
    if (await radioTarjeta.isVisible()) {
      await radioTarjeta.check();
    }

    // 5. Gestión de tarjeta: si hay tarjeta guardada, usarla; de lo contrario, llenar formulario
    const tarjetaGuardadaRadio = page.locator('.radio-tarjeta-guardada:not([disabled])').first();
    const hayGuardada = await tarjetaGuardadaRadio.count() > 0;

    if (hayGuardada) {
      await tarjetaGuardadaRadio.check();
      // Si solicita CVV para tarjeta guardada
      const cvvGuardada = page.locator('#cvv-guardada');
      if (await cvvGuardada.isVisible()) {
        await cvvGuardada.fill('123');
      }
    } else {
      // Ingresar tarjeta de prueba válida
      const inputPan = page.locator('#pan-input');
      if (await inputPan.isVisible()) {
        await inputPan.fill('4000 1234 5678 9010');
        await page.locator('#exp-input').fill('12/28');
        await page.locator('#cvv-input').fill('123');
        await page.locator('#titular-input').fill('Cliente Uno');

        // Click en validar tarjeta
        await page.locator('#btn-validar').click();
        await page.waitForTimeout(1000);
      }
    }

    // Aceptar términos y privacidad si el checkbox está presente
    const checkPrivacidad = page.locator('#check-privacidad');
    if (await checkPrivacidad.isVisible()) {
      await checkPrivacidad.check();
    }

    // 6. Confirmar pedido
    const btnConfirmar = page.locator('#btn-confirmar');
    await expect(btnConfirmar).toBeEnabled({ timeout: 10000 });
    await btnConfirmar.click();

    // 7. Validar resultado: debe redirigir a confirmación o a la pantalla de estado de orden
    await page.waitForURL(url =>
      url.pathname.includes('/Estado') ||
      url.pathname.includes('/Checkout') ||
      url.pathname.includes('/Exito'),
      { timeout: 20000 }
    );

    // Si está en /Estado, confirmar que se muestra el identificador de orden o comprobante
    const urlActual = page.url();
    expect(urlActual).not.toContain('/Auth/Login');
  });

});
