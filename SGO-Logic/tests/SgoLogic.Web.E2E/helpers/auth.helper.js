/**
 * Helper de autenticación para pruebas E2E en SGO-Logic.
 */
async function iniciarSesion(page, email = 'cliente1@sgo.local', password = '1234') {
  await page.goto('/Auth/Login');
  await page.waitForLoadState('domcontentloaded');

  // Llenar formulario de credenciales. AuthController.Login espera "contrasena",
  // no "password": es el nombre real del input en Views/Auth/Login.cshtml.
  await page.locator('input[name="email"], input#email').fill(email);
  await page.locator('input[name="contrasena"], input#contrasena').fill(password);

  // Enviar formulario
  await page.locator('button[type="submit"]').click();

  // Login correcto responde 302; uno fallido se queda en la misma vista con el error.
  await page.waitForURL(url => !url.pathname.includes('/Auth/Login'), { timeout: 15000 });
}

async function cerrarSesion(page) {
  try {
    await page.goto('/Auth/Logout');
    await page.waitForLoadState('domcontentloaded');
  } catch (err) {
    // Si la ruta POST requiere token antiforgery, limpia las cookies de sesión
    await page.context().clearCookies();
  }
}

module.exports = {
  iniciarSesion,
  cerrarSesion
};
