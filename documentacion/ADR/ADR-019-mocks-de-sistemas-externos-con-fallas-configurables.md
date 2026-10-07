# ADR-019: Los sistemas externos se simulan con mocks en contenedor y fallas configurables

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-19

## Contexto

SGO-Logic se integra con proveedores contractuales que no pueden modificarse ni reemplazarse (PayKlik, LogiExpress, RES-04, RES-05), con el servicio de certificación de la SAT, con un proveedor externo de mensajería (EV-20), con el ERP-Core y con las 32 instancias de POS, que no se reemplazan (RES-03, EV-16). Ninguno de ellos está disponible en el entorno de ejecución del MVP, que debe levantarse en una máquina limpia con un solo comando (ADR-013).

Además, los escenarios de calidad exigen provocar fallas de esos sistemas a voluntad y de forma repetible: EAC-01 pide demostrar que no se pierde ninguna orden cuando PayKlik cae durante el pico de Black Friday, y la resiliencia de Despacho y de Facturación depende de que LogiExpress y la SAT dejen de responder (EV-18, EV-19). Una falla de un tercero no se puede programar para una prueba de carga.

## Decisión

Cada sistema externo se representa con un mock que corre como contenedor propio del Docker Compose del MVP, expone el contrato conocido del sistema real y ofrece una API de administración para provocar fallas. Los mocks son:

| Mock | Puerto en el host | Tecnología | Fallas que simula |
|---|---|---|---|
| `payklik-mock` | 8084 | .NET | Modos `NORMAL`, `LENTO` (latencia configurable), `CAIDO` (respuesta 503), `RECHAZO` (respuesta 200 con cargo rechazado por fondos insuficientes) e `INTERMITENTE` (probabilidad de falla configurable). Idempotencia por `Idempotency-Key` en cargos y reembolsos. |
| `sat-mock` | 8086 | .NET | Modos `NORMAL`, `LENTO`, `CAIDO` (503) y `RECHAZO`, más una probabilidad de falla 503 aplicable en cualquier modo. Idempotencia en la certificación. |
| `logiexpress-mock` | 8098 | .NET | Modos `NORMAL`, `LENTO` y `CAIDO`, más un timeout determinístico: una de cada 50 solicitudes de guía no responde. Rastreo y anulación de guías, y cambio manual del estado de rastreo. |
| `mock-mensajeria` | 8089 | .NET | Falla forzada (respuesta 500) en el envío de SMS, correo y notificaciones push, activable por configuración o en caliente. |
| `mock-erp` | 4000 | Node | Recepción de lotes de ventas en CSV, XML y JSON con cuadre de totales (rechazo 422), duplicados (409) y secuencia retrocedida (409); exportación de snapshots de historial crediticio por lote con cortes, duplicados y datos inválidos controlables. Caída provocada: 503, 500 o timeout de 8 segundos con respuesta 504. |
| `mock-pos` | 4001 | Node | 32 tiendas (`T01` a `T32`) con idempotencia por tienda y `eventoId`. Caída por tienda o masiva: 503, 500 o timeout. |

Las reglas comunes son cuatro:

1. **Determinismo cuando la prueba lo necesita.** El timeout de LogiExpress se decide con un contador y no con azar, de modo que una prueba pueda afirmar cuál solicitud falla.
2. **Sin datos sensibles.** Ningún mock recibe ni devuelve números de tarjeta ni códigos de seguridad; solo tokens opacos (RES-08, ADR-010).
3. **Ubicación.** Los mocks .NET viven en la solución (`SgoLogic.Mocks.PayKlik`, `SgoLogic.Mocks.SAT`, `SgoLogic.Mocks.LogiExpress`, `SgoLogic.MockMensajeria`). Los dos mocks Node viven en la carpeta `Mocks/` del repositorio, con su propio archivo de composición y documentación, y el compose principal los construye desde allí.
4. **Alcance.** Node se usa únicamente para estos dos mocks: ningún componente del producto depende de él. Los mocks no forman parte de la arquitectura de producción; allí se sustituyen por la dirección del sistema real.

## Alternativas consideradas

Se evaluaron cinco opciones: (1) mocks propios en contenedor con API de administración de fallas (elegida); (2) usar los servicios reales o sus ambientes de prueba, descartada porque la falla de un tercero no se puede provocar ni repetir a voluntad, y EAC-01/EAC-06 requieren escenarios controlados; (3) dobles en memoria dentro de las pruebas unitarias, que se mantienen para ese nivel de prueba pero no sirven para pruebas de integración ni para demostrar el sistema completo; (4) una herramienta genérica de simulación, descartada por incorporar otra tecnología para reproducir contratos pequeños; y (5) reescribir en .NET los mocks Node, descartada porque no forman parte del producto. La ampliación de `mock-erp` con fixtures de historial es una extensión de la opción elegida, no una nueva fuente productiva.

## Consecuencias

### Positivas
- Las pruebas de resiliencia son repetibles: la caída de PayKlik, el timeout de LogiExpress o la falla de la SAT se provocan con una llamada al mock y se pueden agendar dentro de una prueba de carga.
- El MVP se levanta en una máquina limpia sin credenciales ni acceso a los proveedores reales (ADR-013).
- El contrato de cada sistema queda ejecutable como documentación, y el resto del código se escribe contra ese contrato sin cambios cuando se apunte al sistema real.

### Negativas
- Los mocks reproducen el contrato conocido y los modos de falla declarados, no toda la variabilidad de los sistemas reales. Los umbrales de reintento y de disyuntor, y los tiempos máximos de espera, deben validarse contra los sistemas reales antes de producción, como ya anticipa ADR-009.
- El entorno del MVP incluye un runtime adicional (Node) que el equipo no domina por completo (RES-12, EV-46). El riesgo queda acotado porque el código de los mocks es pequeño y no forma parte del producto.
- Los mocks guardan su estado en memoria: reiniciar el contenedor lo reinicia. Los mocks de PayKlik, SAT y LogiExpress no usan las bibliotecas comunes y no exponen métricas; su efecto se observa a través de las métricas de los servicios que los consumen.
- Los mocks de LogiExpress no propagan por sí solos los cambios de estado de entrega a Despacho: quien ejecuta la demostración debe informarlos al webhook de Despacho, como haría el transportista real.
- `mock-erp` también conserva sus fixtures de historial en memoria y los entrega solo para pruebas del flujo batch; un reinicio restaura el conjunto inicial. Los snapshots no representan una base histórica real ni deben ser consultados desde Checkout.

## Preocupaciones y evidencia que atiende

PRE-02, PRE-04, PRE-16, PRE-17. Evidencia: EV-11, EV-16, EV-17, EV-18, EV-19, EV-20, EV-46.
