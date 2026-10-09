# Guía de Pruebas End-to-End (E2E) para la Interfaz Web con Playwright

**Responsable:** Jonathan (202300353)  
**Proyecto:** Sistema de Gestión de Órdenes y Logística Omnicanal (SGO-Logic) — Fase 3  
**Ubicación de la suite:** `SGO-Logic/tests/SgoLogic.Web.E2E/`

---

## 1. Propósito y Alcance

El enunciado de la Fase 3 exige un total de 25 pruebas formales distribuidas en 5 tipos (Unitarias, Integración, Carga, Estrés y End-to-End), con un mínimo de 5 pruebas por tipo.

Esta suite implementa las **5 pruebas End-to-End (E2E)** completas automatizadas para el frontend web de MegaStore ([SgoLogic.Web](file:///Users/jona/Desktop/USAC/4to_Anio/8vo%20Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Web)). Las pruebas se ejecutan mediante [Playwright](https://playwright.dev/) simulando las interacciones reales de un usuario en un navegador web (Chromium) de punta a punta, validando la interacción con el API Gateway, la base de datos, Redis, RabbitMQ y los mocks de servicios externos.

---

## 2. Los 5 Escenarios E2E Automatizados

| ID | Archivo de Especificación | Escenario | Flujo Validado |
| :--- | :--- | :--- | :--- |
| **E2E-01** | `specs/e2e-01-compra-payklik.spec.js` | **Compra completa con PayKlik (Tarjeta)** | Login del cliente (`cliente1@sgo.local`) &rarr; Selección de producto en catálogo &rarr; Carrito &rarr; Checkout &rarr; Validación de tarjeta de crédito &rarr; Aceptación de términos &rarr; Confirmación de orden y comprobante emitido. |
| **E2E-02** | `specs/e2e-02-compra-credito.spec.js` | **Compra con Crédito Propio ("Compra ahora, paga en 30 días")** | Login de cliente con línea de crédito aprobada &rarr; Checkout &rarr; Verificación de opción activa "Crédito MegaStore" con monto disponible y fecha límite de pago &rarr; Confirmación sin pasarela bancaria externa &rarr; Verificación de movimiento en `/Cuenta/Credito`. |
| **E2E-03** | `specs/e2e-03-credito-no-elegible.spec.js` | **Restricción de Crédito para cliente no elegible** | Login de cliente sin historial o con morosidad (`cliente3@sgo.local`) &rarr; Checkout &rarr; Validación de que el selector de crédito aparece deshabilitado (`disabled`) con aviso explicativo &rarr; Confirmación de que el sistema obliga a utilizar pasarela tradicional (PayKlik). |
| **E2E-04** | `specs/e2e-04-reserva-stock-carrito.spec.js` | **Reserva de stock en carrito y control de inventario** | Agregado de productos &rarr; Inspección de reserva temporal en Redis &rarr; Incremento de cantidades &rarr; Intento de agregado de producto sin stock (`PROD-005` con 0 unidades) &rarr; Verificación de bloqueo preventivo de sobreventa. |
| **E2E-05** | `specs/e2e-05-rastreo-factura-incidencia.spec.js` | **Rastreo de orden, factura electrónica e incidencias** | Login &rarr; Navegación a `/Estado` &rarr; Consulta de historial de órdenes &rarr; Inspección del detalle y línea de tiempo (timeline) &rarr; Visualización de factura electrónica/comprobante SAT &rarr; Acceso y validación del formulario de incidencias (`/Incidencias/Crear`). |

---

## 3. Prerrequisitos de Ejecución

1. **Stack de servicios arriba:**
   Desde la raíz de `SGO-Logic/`:
   ```bash
   cd SGO-Logic
   docker compose up -d
   ```
   Asegurar que la tienda web responda en `http://localhost:8092`.

2. **Entorno Node.js:**
   Node.js v18+ y npm instalados en la máquina anfitriona o en el runner de CI/CD.

---

## 4. Comandos de Ejecución

### Opción A: Script directo (Recomendado)
```bash
cd SGO-Logic/tests/SgoLogic.Web.E2E
./ejecutar-e2e.sh
```

### Opción B: Mediante npm
```bash
cd SGO-Logic/tests/SgoLogic.Web.E2E

# Instalar dependencias
npm install

# Instalar navegadores si es primera vez
npx playwright install chromium

# Ejecutar todas las pruebas en modo headless
npm test

# Ejecutar viendo el navegador en tiempo real
npm run test:headed

# Ejecutar en modo interactivo con UI de Playwright
npm run test:ui
```

### Opción C: Ver reporte detallado con capturas
```bash
npm run report
```
Playwright genera automáticamente un reporte HTML interactivo en `playwright-report/` con trazas y capturas de pantalla de cualquier fallo detectado.
