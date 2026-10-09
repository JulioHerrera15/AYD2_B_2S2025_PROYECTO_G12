/**
 * Helper de operaciones de carrito para pruebas E2E en SGO-Logic.
 */
async function agregarProducto(page, sku = 'PROD-002', cantidad = 1) {
  // Ir al catálogo
  await page.goto('/Catalogo');
  await page.waitForLoadState('domcontentloaded');

  // Buscar el botón o formulario de agregar del SKU especificado
  const cardProducto = page.locator(`[data-sku="${sku}"], .card-producto:has-text("${sku}")`).first();
  if (await cardProducto.count() > 0) {
    const btnAgregar = cardProducto.locator('button:has-text("Agregar"), button:has-text("Añadir")').first();
    await btnAgregar.click();
  } else {
    // Si no está por data-sku, agrega directamente vía post /Carrito/Agregar
    await page.evaluate(async ({ sku, cantidad }) => {
      const formData = new FormData();
      formData.append('sku', sku);
      formData.append('descripcion', `Producto ${sku}`);
      formData.append('cantidad', cantidad.toString());
      formData.append('precioUnitario', '299.99');
      await fetch('/Carrito/Agregar', {
        method: 'POST',
        body: formData
      });
    }, { sku, cantidad });
  }

  // Esperar notificación o actualización de carrito
  await page.waitForTimeout(500);
}

async function vaciarCarritoSiExiste(page) {
  await page.goto('/Carrito');
  await page.waitForLoadState('domcontentloaded');

  const btnEliminar = page.locator('form[action*="/Carrito/Eliminar"] button, button:has-text("Eliminar")');
  const count = await btnEliminar.count();
  for (let i = 0; i < count; i++) {
    try {
      await btnEliminar.first().click();
      await page.waitForLoadState('domcontentloaded');
    } catch (e) {
      break;
    }
  }
}

module.exports = {
  agregarProducto,
  vaciarCarritoSiExiste
};
