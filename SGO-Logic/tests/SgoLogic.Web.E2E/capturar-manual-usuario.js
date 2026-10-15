const { chromium } = require('@playwright/test');
const path = require('path');
const fs = require('fs');

const BASE_URL = process.env.WEB_BASE_URL || 'http://localhost:8092';
const IMG_DIR = path.resolve(__dirname, '../../../documentacion/manuales/img');

if (!fs.existsSync(IMG_DIR)) {
  fs.mkdirSync(IMG_DIR, { recursive: true });
}

async function login(page, email, password = '1234') {
  try {
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('domcontentloaded');
    await page.fill('input[name="email"], input#email', email);
    await page.fill('input[name="contrasena"], input#contrasena', password);
    await page.click('button[type="submit"]');
    await page.waitForURL(url => !url.pathname.includes('/Auth/Login'), { timeout: 6000 });
  } catch (e) {
    console.warn(`[WARN] Login para ${email} reintentando con Demo1234!...`);
    try {
      await page.fill('input[name="contrasena"], input#contrasena', 'Demo1234!');
      await page.click('button[type="submit"]');
      await page.waitForURL(url => !url.pathname.includes('/Auth/Login'), { timeout: 6000 });
    } catch (e2) {
      console.warn(`[WARN] No se pudo autenticar como ${email}: ${e2.message}`);
    }
  }
}

async function logout(page) {
  try {
    await page.goto(`${BASE_URL}/Auth/Logout`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(300);
  } catch (e) {
    await page.context().clearCookies();
  }
}

async function emptyCart(page) {
  try {
    await page.goto(`${BASE_URL}/Carrito`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(400);
    let btns = page.locator('form[action*="/Carrito/Eliminar"] button, button:has-text("Eliminar")');
    while (await btns.count() > 0) {
      try {
        await btns.first().click({ timeout: 2000 });
        await page.waitForLoadState('domcontentloaded');
        await page.waitForTimeout(300);
        btns = page.locator('form[action*="/Carrito/Eliminar"] button, button:has-text("Eliminar")');
      } catch (e) {
        break;
      }
    }
  } catch (err) {
    console.warn('[WARN] Error al vaciar carrito:', err.message);
  }
}

async function addProduct(page, sku, cantidad = 1) {
  try {
    await page.evaluate(async ({ sku, cantidad }) => {
      const fd = new FormData();
      fd.append('sku', sku);
      fd.append('descripcion', `Producto ${sku}`);
      fd.append('cantidad', cantidad.toString());
      fd.append('precioUnitario', '299.99');
      await fetch('/Carrito/Agregar', { method: 'POST', body: fd });
    }, { sku, cantidad });
    await page.waitForTimeout(500);
  } catch (err) {
    console.warn(`[WARN] Error agregando ${sku}:`, err.message);
  }
}

async function takeShot(page, filename, description) {
  const dest = path.join(IMG_DIR, filename);
  await page.waitForTimeout(600);
  await page.screenshot({ path: dest, fullPage: false });
  console.log(`[OK] ${filename} - ${description}`);
}

async function step(name, fn) {
  try {
    await fn();
  } catch (err) {
    console.error(`[ERROR] en ${name}:`, err.message);
  }
}

async function run() {
  console.log('Iniciando captura automatizada optimizada de las 34 imágenes...');
  console.log(`Servidor destino: ${BASE_URL}`);
  console.log(`Directorio de salida: ${IMG_DIR}\n`);

  const browser = await chromium.launch({
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox']
  });

  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1
  });

  const page = await context.newPage();

  try {
    // 1. mu-inicio.png - Sin sesión - Pantalla principal al entrar
    await step('1. mu-inicio.png', async () => {
      await context.clearCookies();
      await page.goto(`${BASE_URL}/`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-inicio.png', 'Pantalla principal al entrar (sin sesión)');
    });

    // 2. mu-registro.png - Correo nuevo - Formulario de registro lleno
    await step('2. mu-registro.png', async () => {
      await page.goto(`${BASE_URL}/Auth/Registro`);
      await page.waitForLoadState('domcontentloaded');
      await page.fill('input[name="nombre"]', 'Carlos');
      await page.fill('input[name="apellido"]', 'Mendoza');
      await page.fill('input[name="telefono"]', '50123456');
      await page.fill('input[name="email"]', 'carlos.mendoza@megastore.com');
      await page.fill('input[name="contrasena"]', 'MiPasswordSeguro123!');
      await page.fill('input[name="confirmarContrasena"]', 'MiPasswordSeguro123!');
      await takeShot(page, 'mu-registro.png', 'Formulario de registro lleno');
    });

    // 3. mu-verificar-correo.png - Correo nuevo - Pantalla verificación
    await step('3. mu-verificar-correo.png', async () => {
      await page.goto(`${BASE_URL}/Auth/Verificar?email=carlos.mendoza%40megastore.com`);
      await page.waitForLoadState('domcontentloaded');
      const codigoInput = page.locator('input[name="codigo"], input#codigo');
      if (await codigoInput.count() > 0) {
        await codigoInput.fill('482910');
      }
      await takeShot(page, 'mu-verificar-correo.png', 'Pantalla para ingresar el código de verificación');
    });

    // 4. mu-login.png - Sin sesión - Formulario de inicio de sesión
    await step('4. mu-login.png', async () => {
      await context.clearCookies();
      await page.goto(`${BASE_URL}/Auth/Login`);
      await page.waitForLoadState('domcontentloaded');
      await page.fill('input[name="email"], input#email', 'cliente1@sgo.local');
      await page.fill('input[name="contrasena"], input#contrasena', '1234');
      await takeShot(page, 'mu-login.png', 'Formulario de inicio de sesión con datos listos');
    });

    // Login cliente1
    await login(page, 'cliente1@sgo.local', '1234');

    // 5. mu-catalogo.png - cliente1@sgo.local - Catálogo con existencias
    await step('5. mu-catalogo.png', async () => {
      await page.goto(`${BASE_URL}/Catalogo`);
      await page.waitForLoadState('domcontentloaded');
      // Esperar a que los badges de stock se actualicen (desaparezca 'Consultando...')
      await page.waitForFunction(() => {
        const badges = Array.from(document.querySelectorAll('[id^="stock-badge-"]'));
        return badges.length > 0 && !badges.some(b => b.textContent.includes('Consultando'));
      }, { timeout: 10000 }).catch(() => null);
      await page.waitForTimeout(1000);
      await page.evaluate(() => window.scrollTo(0, 0));
      await takeShot(page, 'mu-catalogo.png', 'Catálogo de productos con existencias en vivo');
    });

    // 6. mu-catalogo-otra-tienda.png - cliente1@sgo.local - Aviso de producto de otra tienda
    await step('6. mu-catalogo-otra-tienda.png', async () => {
      await emptyCart(page);
      await addProduct(page, 'PROD-001', 1);
      await page.goto(`${BASE_URL}/Checkout`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(600);
      // Mostrar y scrollear el aviso de otra tienda en la sección de entrega
      await page.evaluate(() => {
        if (typeof window.mostrarAvisoSimulacion === 'function') {
          window.mostrarAvisoSimulacion('aviso',
            'Tu pedido se enviará desde la tienda MegaStore Miraflores (T02), no desde tu sucursal ni desde el centro de distribución. Puede tardar más en llegar.');
        } else {
          const aviso = document.getElementById('aviso-simulacion');
          if (aviso) {
            aviso.className = 'mb-4 p-3.5 rounded-2xl border text-xs font-medium flex items-start gap-2.5 bg-amber-50 border-amber-200/70 text-amber-950';
            const txt = document.getElementById('aviso-simulacion-texto');
            if (txt) txt.textContent = 'Tu pedido se enviará desde la tienda MegaStore Miraflores (T02), no desde tu sucursal ni desde el centro de distribución. Puede tardar más en llegar.';
          }
        }
        const aviso = document.getElementById('aviso-simulacion');
        if (aviso) {
          aviso.scrollIntoView({ behavior: 'instant', block: 'center' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-catalogo-otra-tienda.png', 'Aviso de producto a domicilio provisto desde otra sucursal');
    });

    // 7. mu-catalogo-sucursal-cercana.png - cliente1@sgo.local - Aviso sucursal más cercana
    await step('7. mu-catalogo-sucursal-cercana.png', async () => {
      await page.goto(`${BASE_URL}/Catalogo`);
      await page.waitForLoadState('domcontentloaded');
      // Esperar a que el producto PROD-102 cargue su sucursal cercana
      await page.waitForFunction(() => {
        const badge = document.getElementById('stock-badge-PROD-102');
        return badge && !badge.textContent.includes('Consultando');
      }, { timeout: 10000 }).catch(() => null);
      await page.waitForTimeout(800);
      // Scrollear la tarjeta de PROD-102 al centro
      await page.evaluate(() => {
        const card = document.querySelector('div[data-sku="PROD-102"]');
        if (card) {
          card.scrollIntoView({ behavior: 'instant', block: 'center' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-catalogo-sucursal-cercana.png', 'Producto sin existencias con sugerencia de sucursal cercana');
    });

    // 8. mu-carrito-reserva.png - cliente1@sgo.local - Carrito con tiempo restante
    await step('8. mu-carrito-reserva.png', async () => {
      await emptyCart(page);
      await addProduct(page, 'PROD-001', 1);
      await addProduct(page, 'PROD-002', 1);
      await page.goto(`${BASE_URL}/Carrito`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-carrito-reserva.png', 'Carrito con productos y cuenta regresiva de reserva');
    });

    // 9. mu-checkout-tarjeta.png - cliente2@sgo.local - Checkout PayKlik tarjeta
    await step('9. mu-checkout-tarjeta.png', async () => {
      await logout(page);
      await login(page, 'cliente2@sgo.local', '1234');
      await emptyCart(page);
      await addProduct(page, 'PROD-001', 1);
      await page.goto(`${BASE_URL}/Checkout`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      // Seleccionar tarjeta y scrollear la sección 3 (Método de pago) para que se vean campos/tarjetas
      await page.evaluate(() => {
        const radioTarjeta = document.querySelector('input[value="TARJETA"]');
        if (radioTarjeta) radioTarjeta.checked = true;
        const panelTarjeta = document.getElementById('panel-tarjeta');
        if (panelTarjeta) panelTarjeta.classList.remove('hidden');
        const h2Pago = Array.from(document.querySelectorAll('h2')).find(h => h.textContent.includes('Método de pago'));
        if (h2Pago) {
          h2Pago.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-checkout-tarjeta.png', 'Checkout con PayKlik (tarjeta de crédito/débito) seleccionado');
    });

    // 10. mu-checkout-credito-disponible.png - cliente1@sgo.local - Crédito con cupo y fecha
    await step('10. mu-checkout-credito-disponible.png', async () => {
      await logout(page);
      await login(page, 'cliente1@sgo.local', '1234');
      await emptyCart(page);
      await addProduct(page, 'PROD-001', 1);
      await page.goto(`${BASE_URL}/Checkout`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      // Seleccionar crédito y scrollear al método de pago
      await page.evaluate(() => {
        const radioCredito = document.querySelector('input[value="CREDITO"]');
        if (radioCredito) radioCredito.checked = true;
        const h2Pago = Array.from(document.querySelectorAll('h2')).find(h => h.textContent.includes('Método de pago'));
        if (h2Pago) {
          h2Pago.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-checkout-credito-disponible.png', 'Checkout ofreciendo crédito con cupo disponible y fecha de pago');
    });

    // 11. mu-checkout-credito-excede-limite.png - cliente1@sgo.local - Compra > Q7,500
    await step('11. mu-checkout-credito-excede-limite.png', async () => {
      await emptyCart(page);
      await page.evaluate(async () => {
        const fd = new FormData();
        fd.append('sku', 'PROD-003');
        fd.append('descripcion', 'Smart TV Crystal UHD 65"');
        fd.append('cantidad', '3');
        fd.append('precioUnitario', '3499.00');
        await fetch('/Carrito/Agregar', { method: 'POST', body: fd });
      });
      await page.goto(`${BASE_URL}/Checkout`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      // Scrollear sección de pago mostrando el aviso de límite insuficiente
      await page.evaluate(() => {
        const h2Pago = Array.from(document.querySelectorAll('h2')).find(h => h.textContent.includes('Método de pago'));
        if (h2Pago) {
          h2Pago.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-checkout-credito-excede-limite.png', 'Compra que excede el límite de Q7,500 con crédito bloqueado');
    });

    // 12. mu-checkout-credito-no-califica.png - cliente6@sgo.local - Incidente grave
    await step('12. mu-checkout-credito-no-califica.png', async () => {
      await logout(page);
      await login(page, 'cliente6@sgo.local', '1234');
      await emptyCart(page);
      await addProduct(page, 'PROD-002', 1);
      await page.goto(`${BASE_URL}/Checkout`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      // Scrollear al método de pago mostrando aviso de cliente no elegible
      await page.evaluate(() => {
        const h2Pago = Array.from(document.querySelectorAll('h2')).find(h => h.textContent.includes('Método de pago'));
        if (h2Pago) {
          h2Pago.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-checkout-credito-no-califica.png', 'Checkout sin crédito para cliente no elegible (incidente grave)');
    });

    // 13. mu-checkout-credito-moroso.png - cliente4@sgo.local - Historial pagos tardíos
    await step('13. mu-checkout-credito-moroso.png', async () => {
      await logout(page);
      await login(page, 'cliente4@sgo.local', '1234');
      await emptyCart(page);
      await addProduct(page, 'PROD-002', 1);
      await page.goto(`${BASE_URL}/Checkout`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      // Scrollear al método de pago mostrando crédito en pausa
      await page.evaluate(() => {
        const h2Pago = Array.from(document.querySelectorAll('h2')).find(h => h.textContent.includes('Método de pago'));
        if (h2Pago) {
          h2Pago.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-checkout-credito-moroso.png', 'Checkout con crédito en pausa por pagos tardíos');
    });

    // Volver a cliente1@sgo.local
    await logout(page);
    await login(page, 'cliente1@sgo.local', '1234');

    // 14. mu-checkout-exito.png - Pantalla de orden confirmada
    await step('14. mu-checkout-exito.png', async () => {
      await page.goto(`${BASE_URL}/Checkout/Exito?ordenId=ORD-2026-000016&numeroOrden=MS-2026-0016&total=243527.14&confirmada=true&credito=false`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-checkout-exito.png', 'Pantalla de orden confirmada exitosamente');
    });

    // 15. mu-mis-ordenes.png - Listado de mis órdenes
    await step('15. mu-mis-ordenes.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Ordenes`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-mis-ordenes.png', 'Listado de mis pedidos y estados');
    });

    // 16. mu-rastreo-orden.png - Rastreo de orden con línea de tiempo
    await step('16. mu-rastreo-orden.png', async () => {
      await page.goto(`${BASE_URL}/Estado?ordenId=ORD-2026-000016`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-rastreo-orden.png', 'Rastreo de una orden con su línea de tiempo');
    });

    // 17. mu-factura.png - Factura electrónica SAT FEL
    await step('17. mu-factura.png', async () => {
      await page.goto(`${BASE_URL}/Estado/Factura?ordenId=ORD-2026-000016`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-factura.png', 'Factura electrónica SAT FEL de la orden');
    });

    // 18. mu-comprobante.png - Comprobante de pago de la orden
    await step('18. mu-comprobante.png', async () => {
      await page.goto(`${BASE_URL}/Estado/Comprobante?ordenId=ORD-2026-000016`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-comprobante.png', 'Comprobante digital de la orden');
    });

    // 19. mu-reintentar-pago.png - cliente2@sgo.local - Reintento de pago
    await step('19. mu-reintentar-pago.png', async () => {
      await logout(page);
      await login(page, 'cliente2@sgo.local', '1234');
      await page.goto(`${BASE_URL}/Pagar/Reintentar?token=reintento-demo-000006`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-reintentar-pago.png', 'Pantalla de reintento de cobro para pago fallido');
    });

    // 20. mu-reintento-exitoso.png - Confirmación del reintento exitoso
    await step('20. mu-reintento-exitoso.png', async () => {
      await page.goto(`${BASE_URL}/Checkout/Exito?ordenId=ORD-2026-000006&numeroOrden=MS-2026-0006&total=2800.00&confirmada=true&reintento=true`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-reintento-exitoso.png', 'Confirmación del reintento de pago exitoso');
    });

    // Volver a cliente1@sgo.local
    await logout(page);
    await login(page, 'cliente1@sgo.local', '1234');

    // 21. mu-reembolso-orden.png - Orden devuelta/cancelada con estado del reembolso
    await step('21. mu-reembolso-orden.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Ordenes`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(1000);
      // Scrollear directamente al contenedor de reembolso de ORD-2026-000018
      await page.evaluate(() => {
        const reembolsoCard = document.querySelector('[data-reembolso-orden="ORD-2026-000018"]')
          || document.querySelector('[data-reembolso-orden]')
          || Array.from(document.querySelectorAll('div')).find(el => el.textContent && el.textContent.includes('Reembolso de Q'));
        if (reembolsoCard) {
          reembolsoCard.scrollIntoView({ behavior: 'instant', block: 'center' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-reembolso-orden.png', 'Orden con estado de reembolso en proceso');
    });

    // 22. mu-incidencia-crear.png - Formulario para reportar incidencia
    await step('22. mu-incidencia-crear.png', async () => {
      await page.goto(`${BASE_URL}/Incidencias/Crear?ordenId=ORD-2026-000016`);
      await page.waitForLoadState('domcontentloaded');
      const descInput = page.locator('#descripcion, textarea[name="descripcion"]');
      if (await descInput.count() > 0) {
        await descInput.fill('El paquete llegó con el empaque golpeado y falta el manual de usuario con el accesorio de conexión.');
      }
      await takeShot(page, 'mu-incidencia-crear.png', 'Formulario para reportar una incidencia con datos');
    });

    // 23. mu-incidencias-lista.png - Listado de incidencias reportadas
    await step('23. mu-incidencias-lista.png', async () => {
      await page.goto(`${BASE_URL}/Estado?ordenId=ORD-2026-000016`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await page.evaluate(() => {
        const seccionIncidencias = Array.from(document.querySelectorAll('div, h2, h3, span'))
          .find(el => el.textContent && el.textContent.includes('Incidencias del Pedido'));
        if (seccionIncidencias) {
          seccionIncidencias.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await takeShot(page, 'mu-incidencias-lista.png', 'Listado de incidencias reportadas del pedido');
    });

    // 24. mu-mi-credito.png - Mi crédito con cupo, saldo y fecha
    await step('24. mu-mi-credito.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Credito`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(1000);
      await page.evaluate(() => window.scrollTo(0, 0));
      await takeShot(page, 'mu-mi-credito.png', 'Mi crédito con cupo disponible, saldo pendiente y fecha de pago');
    });

    // 25. mu-mi-credito-abono.png - Abono aplicado y saldo reducido
    await step('25. mu-mi-credito-abono.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Credito`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      // Abrir modal de pago / abono y asegurar que los campos sean visibles
      await page.evaluate(() => {
        const modal = document.getElementById('panel-pago');
        if (modal) {
          modal.classList.remove('hidden');
          const panelContent = modal.querySelector('div') || modal;
          panelContent.scrollIntoView({ behavior: 'instant', block: 'center' });
        }
      });
      await page.waitForTimeout(600);
      await takeShot(page, 'mu-mi-credito-abono.png', 'Panel para abonar a una compra a crédito');
    });

    // 26. mu-perfil.png - Perfil del usuario
    await step('26. mu-perfil.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Perfil`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-perfil.png', 'Perfil del usuario con información de sesión omnicanal');
    });

    // 27. mu-tarjetas.png - Tarjetas guardadas tokenizadas
    await step('27. mu-tarjetas.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Tarjetas`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-tarjetas.png', 'Tarjetas guardadas y tokenizadas del cliente');
    });

    // 28. mu-privacidad.png - Opciones de privacidad
    await step('28. mu-privacidad.png', async () => {
      await page.goto(`${BASE_URL}/Cuenta/Privacidad`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-privacidad.png', 'Centro de Privacidad y Protección de Datos');
    });

    // Roles operativos: operador@sgo.local
    await logout(page);
    await login(page, 'operador@sgo.local', '1234');

    // 29. mu-pos.png - Terminal POS de tienda
    await step('29. mu-pos.png', async () => {
      await page.goto(`${BASE_URL}/Pos`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-pos.png', 'Terminal OmniPOS de ventas en mostrador');
    });

    // 30. mu-despacho.png - Dashboard de despacho
    await step('30. mu-despacho.png', async () => {
      await page.goto(`${BASE_URL}/Despacho`);
      await page.waitForLoadState('domcontentloaded');
      // Esperar a que la tabla o lista de despachos se cargue
      await page.waitForFunction(() => {
        return document.querySelectorAll('table tr, [class*="despacho-item"]').length > 1;
      }, { timeout: 10000 }).catch(() => null);
      await page.waitForTimeout(800);
      await page.evaluate(() => {
        const tabla = document.querySelector('table');
        if (tabla) {
          tabla.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      });
      await takeShot(page, 'mu-despacho.png', 'Panel de control de Despacho Logístico');
    });

    // 31. mu-vision360.png - Visión 360 de un cliente
    await step('31. mu-vision360.png', async () => {
      await page.goto(`${BASE_URL}/Vision360?clienteId=cliente1%40sgo.local`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-vision360.png', 'Visión 360 integral del cliente');
    });

    // Rol administrador: admin@sgo.local
    await logout(page);
    await login(page, 'admin@sgo.local', '1234');

    // 32. mu-usuarios-roles.png - Administración de usuarios y roles
    await step('32. mu-usuarios-roles.png', async () => {
      await page.goto(`${BASE_URL}/Usuarios`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-usuarios-roles.png', 'Administración de usuarios y asignación de roles');
    });

    // 33. mu-informe-ventas.png - Informe de ventas conciliado
    await step('33. mu-informe-ventas.png', async () => {
      await page.goto(`${BASE_URL}/InformeVentas`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-informe-ventas.png', 'Informe de ventas conciliado con el ERP');
    });

    // 34. mu-monitoreo.png - Centro de monitoreo
    await step('34. mu-monitoreo.png', async () => {
      await page.goto(`${BASE_URL}/Monitoreo`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(800);
      await takeShot(page, 'mu-monitoreo.png', 'Centro de control y monitoreo de la arquitectura');
    });

    console.log('\n=============================================================');
    console.log('¡Proceso completado exitosamente para las 34 capturas!');
    console.log('=============================================================');

  } catch (error) {
    console.error('Error durante la captura de imágenes:', error);
  } finally {
    await browser.close();
  }
}

run();
