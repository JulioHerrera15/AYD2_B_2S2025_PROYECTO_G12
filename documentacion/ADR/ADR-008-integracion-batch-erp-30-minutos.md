# ADR-008: Integración batch CSV/XML cada 30 minutos con el ERP

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

El ERP-Core se integra por archivos CSV o XML procesables en intervalos de 30 minutos (EV-15); no existe una vía síncrona ni en tiempo real y esto no es negociable porque el ERP no se modifica (RES-01, ADR-007). El mismo mecanismo batch debe permitir procesar el snapshot de historial comercial que necesita Crédito, además de los resúmenes de ventas. Hoy Finanzas recibe archivos de hojas de cálculo de distintos canales y tiendas para conciliar ventas manualmente, lo que puede retrasar el cierre, los reportes gerenciales y la actualización de perfiles (EV-10).

## Decisión

Un worker del contexto de Integración ERP consolida los eventos de venta confirmados y genera el lote CSV/XML cada 30 minutos, transfiriéndolo al ERP dentro de su ventana admitida, con conciliación de diferencias (ver `RES-02` y `CU-13` en el DDA). El mismo worker obtiene el lote de snapshots de historial comercial, lo valida mediante la ACL, lo transforma al contrato `HistorialClienteErpActualizado` y lo publica mediante Outbox para Crédito (CU-16, ADR-023). Además del control por lote (número de secuencia, fecha de corte y totales de control), un proceso de reconciliación diario compara ventas, historiales recibidos y perfiles aplicados contra los lotes aceptados y genera un reporte de diferencias para Finanzas.

## Alternativas consideradas

Se evaluaron tres opciones: (1) worker batch que genera ventas y procesa snapshots de historial cada 30 minutos; (2) integración en tiempo real vía API, descartada porque el ERP AS/400 no expone ninguna interfaz síncrona y no puede modificarse (EV-14, EV-15); y (3) exportación manual de archivos como se hace hoy, descartada por ser el problema mismo que el proyecto busca eliminar (EV-10). Se elige el lote automático porque es el único mecanismo compatible con el ERP y permite automatizar tanto la conciliación como la alimentación del perfil de Crédito. La forma ejecutable de demostrar este intercambio se registra en ADR-023 y ADR-019 mediante `mock-erp` ampliado.

## Consecuencias

### Positivas
- Respeta el único mecanismo disponible del ERP sin requerir su modificación.
- Automatiza la consolidación que hoy se realiza manualmente con hojas de cálculo, reduciendo el riesgo de retraso en el cierre mensual.
- Permite que Crédito responda localmente sin depender del AS/400 durante checkout.

### Negativas
- La consistencia contable entre SGO-Logic y el ERP es eventual, con una ventana de hasta 30 minutos: cualquier reporte gerencial que dependa del ERP en tiempo real no reflejará las ventas de los últimos 30 minutos.
- La frescura del historial crediticio también es eventual y una decisión puede usar el último corte válido.
- El reporte de reconciliación diario añade un proceso adicional que operar y revisar, a cambio de dar a Finanzas visibilidad verificable de que ninguna venta quedó fuera del ERP.

## Preocupaciones y evidencia que atiende

PRE-11, PRE-12. Evidencia: EV-10, EV-15.
