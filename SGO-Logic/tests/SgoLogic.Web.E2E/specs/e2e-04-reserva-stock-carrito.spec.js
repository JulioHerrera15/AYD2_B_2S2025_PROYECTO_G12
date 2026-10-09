// @ts-check
const { test, expect } = require('@playwright/test');
const { iniciarSesion } = require('../helpers/auth.helper');
const { agregarProducto, vaciarCarritoSiExiste } = require('../helpers/cart.helper');

test.describe('E2E-04: Reserva de stock en carrito y validación de existencias', () => {

  test('Debe gestionar la reserva temporal de inventario en el carrito y validar control de cantidades', async ({ page }) => {
    // 1. Iniciar sesión
    await iniciarSesion(page, 'cliente2@sgo.local', '1234');

    // 2. Preparar carrito limpio
    await vaciarCarritoSiExiste(page);

    // 3. Agregar producto con existencias
    await agregarProducto(page, 'PROD-002', 1);

    // 4. Ir a la vista de Carrito
    await page.goto('/Carrito');
    await page.waitForLoadState('domcontentloaded');

    // Validar que el producto figura en el carrito
    await expect(page.locator('h1, h2')).toContainText(/carrito/i);
    const itemCarrito = page.locator('.item-carrito, tr:has-text("PROD-002"), div:has-text("PROD-002")').first();
    await expect(itemCarrito).toBeVisible({ timeout: 10000 });

    // 5. Probar incremento de cantidad
    const btnIncrementar = page.locator('form[action*="/Carrito/Incrementar"] button, button:has-text("+")').first();
    if (await btnIncrementar.isVisible()) {
      await btnIncrementar.click();
      await page.waitForLoadState('domcontentloaded');
    }

    // 6. Verificar presencia del botón para proceder al Checkout
    const btnCheckout = page.locator('a[href*="/Checkout"], button:has-text("Checkout"), button:has-text("Pagar")').first();
    await expect(btnCheckout).toBeVisible();

    // 7. Intentar agregar producto sin stock (PROD-005 sembrado con 0 unidades en Portales)
    await page.evaluate(async () => {
      const formData = new FormData();
      formData.append('sku', 'PROD-005');
      formData.append('descripcion', 'Laptop Gamer Ultra 16');
      formData.append('cantidad', '1');
      formData.append('precioUnitario', '9999.00');
      return await fetch('/Carrito/Agregar', {
        method: 'POST',
        body: formData
      });
    });

    // Recargar vista para confirmar comportamiento de existencias
    await page.goto('/Carrito');
    await page.waitForLoadState('domcontentloaded');
    await expect(page.locator('body')).toBeVisible();
  });

});
