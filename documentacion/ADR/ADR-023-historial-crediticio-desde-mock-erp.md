# ADR-023: Historial crediticio por lotes desde el mock ERP y perfil precalculado en Crédito

**Estado:** Aceptada.
**Responsable:** Equipo SGO-Logic.
**Fecha:** 2026-10-06

## Contexto

El producto incorpora el crédito "Compra ahora, paga en 30 días". La elegibilidad depende de compras, puntualidad de pago, devoluciones e incidentes del cliente. El contrato de Crédito ya define el evento `HistorialClienteErpActualizado` y el consumidor que actualiza el perfil crediticio, pero Integración ERP todavía no produce ese evento y `mock-erp` solo simula la recepción de lotes de ventas.

El ERP-Core es un sistema AS/400 que no se modifica y cuya integración se mantiene por lotes CSV/XML con una ventana de hasta 30 minutos (ADR-007 y ADR-008). El checkout necesita una decisión en milisegundos y no puede depender de una consulta al ERP. El mock debe permitir demostrar el recorrido completo en una máquina limpia, con datos reproducibles y fallas controlables, sin confundir sus fixtures con datos reales del ERP.

## Decisión

Se amplía `mock-erp` para simular la exportación de un historial comercial por cliente mediante snapshots agrupados en lotes. El mock conserva su responsabilidad de representar el contrato del ERP y expone una operación de prueba para consultar o entregar un lote de historial; no se convierte en una base de dominio ni se consulta desde el checkout.

El flujo adoptado es:

1. `mock-erp` mantiene fixtures de clientes y genera un snapshot de historial con código ERP, email normalizado, antigüedad, compras y monto de los últimos 12 meses, pagos puntuales/tardíos, máximo atraso, devoluciones, incidentes, moneda, fecha de corte y lote de origen.
2. El worker de Integración ERP obtiene el snapshot en su proceso batch, valida el formato legado en la capa anticorrupción y lo transforma a `HistorialClienteErpActualizadoData`.
3. Integración ERP publica `HistorialClienteErpActualizado` por Outbox, con `source=integracion-erp`, `aggregateId` igual al email normalizado, `fechaCorte` y `loteOrigen`. Un mismo lote o un corte anterior no vuelve a publicar datos efectivos.
4. Crédito consume el evento en su cola propia y, en una sola transacción, guarda el último snapshot, recalcula `PerfilCrediticio`, actualiza la línea y publica `PerfilCrediticioActualizado` si cambió el resultado.
5. Checkout consulta únicamente la oferta local de Crédito. Si esa lectura tarda más de su límite o Crédito no responde, oculta la opción y conserva PayKlik como alternativa.
6. Órdenes mantiene la bifurcación existente: después de reservar inventario, `CREDITO` solicita evaluación de Crédito; PayKlik continúa por la ruta de cobro. Ambas rutas terminan en orden confirmada o rechazada y generan la notificación correspondiente.

El formato externo del ERP queda encapsulado en Integración ERP. El contrato interno canónico es `HistorialClienteErpActualizado` de `documentacion/docs/contratos/credito-eventos-v1.md`; no se expone el modelo legado a Crédito.

El mock debe soportar, como mínimo, los siguientes escenarios reproducibles: historial válido elegible, historial sin historial suficiente, pagos tardíos, devoluciones, incidente grave, lote duplicado, lote con corte anterior, lote inválido y caída o timeout durante la obtención. La administración de fallas queda restringida al entorno de pruebas.

## Alternativas consideradas

1. **Extender `mock-erp` con snapshots de historial y entrega batch mediante Integración ERP (elegida).** Permite demostrar el requisito completo, respeta la fuente autoritativa del ERP, mantiene el checkout desacoplado y es coherente con ADR-007, ADR-008, ADR-018 y ADR-019.
2. **Derivar todo el historial desde eventos internos de SGO-Logic.** Se descarta como fuente principal: no contiene la historia previa del cliente, pagos externos ni incidentes que solo existan en el ERP. Puede complementar los datos propios de Crédito, como pagos, vencimientos y devoluciones posteriores.
3. **Consultar el ERP en vivo durante checkout.** Se descarta porque contradice el batch existente, introduce latencia y acopla la decisión comercial a la disponibilidad del AS/400.
4. **Usar un endpoint interno de Crédito o siembra fija como fuente de demostración.** Se descarta como flujo principal porque salta Integración ERP y no demuestra la ACL, el lote ni la publicación del evento. Se conserva solo como apoyo para pruebas unitarias o desarrollo aislado.
5. **Agregar un proveedor externo de buró o score.** Se descarta por costo, alcance y falta de evidencia en el enunciado; la elegibilidad debe basarse en el historial comercial del ERP.

## Consecuencias

### Positivas

- El recorrido `ERP -> Integración ERP -> evento -> Crédito -> checkout` queda ejecutable y demostrable.
- Checkout no espera al ERP ni recalcula el perfil; la consulta de elegibilidad es local y rápida.
- La decisión no modifica ERP-Core, POS, Órdenes ni los contratos actuales de PayKlik.
- Los escenarios de aprobación, rechazo, datos atrasados y fallas de integración pueden repetirse de forma determinista.
- La ACL mantiene separado el modelo legado del dominio de Crédito y permite reemplazar el mock por el ERP real sin modificar Crédito.
- Outbox, deduplicación por evento y `fechaCorte` protegen contra duplicados y entregas fuera de orden.

### Negativas

- La elegibilidad refleja el último corte aplicado y puede tener una antigüedad de hasta la ventana batch; no es una consulta contable en tiempo real.
- El mock requiere mantener fixtures, validaciones y documentación de un contrato adicional.
- La prueba completa requiere levantar RabbitMQ, Integración ERP, Crédito y el mock, además de sus bases.
- El snapshot actual de Crédito conserva la versión vigente; si Finanzas necesita reconstruir cada cambio histórico, debe agregarse una tabla de versiones o apoyarse en Auditoría y Outbox.

## Reglas de datos y seguridad

- El email se normaliza a minúsculas antes de publicar y consumir el evento.
- El mock no contiene PAN, CVV ni datos de tarjeta.
- Los endpoints de administración y fixtures del mock no se publican como interfaz productiva.
- El endpoint temporal `/interno/credito/historiales` no sustituye el flujo batch y debe estar deshabilitado en la demostración del recorrido completo.
- Los eventos incluyen `eventId`, `transactionId`, `causationId`, `sequence`, `fechaCorte` y `loteOrigen` para deduplicación y auditoría.

## Preocupaciones y evidencia que atiende

PRE-01, PRE-02, PRE-04, PRE-05, PRE-11, PRE-12, PRE-14, PRE-16, PRE-17, PRE-20 y las nuevas PRE-21 y PRE-22 del DDA. Evidencia esperada: prueba de integración del lote de historial, prueba de consumo idempotente en Crédito, prueba de elegibilidad local y recorrido de checkout con crédito, rechazo y timeout.
