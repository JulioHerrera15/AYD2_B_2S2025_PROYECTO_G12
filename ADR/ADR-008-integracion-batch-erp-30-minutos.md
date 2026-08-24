# ADR-008: Integración batch CSV/XML cada 30 minutos con el ERP

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

El único mecanismo que el ERP-Core admite para recibir resúmenes de ventas es un archivo CSV o XML, procesable en intervalos de 30 minutos (EV-15); no existe una vía síncrona ni en tiempo real, y esto no es negociable porque el ERP no se modifica (RES-01, ADR-007). Hoy Finanzas recibe archivos de hojas de cálculo de distintos canales y tiendas para conciliar ventas manualmente, lo que puede retrasar el cierre y los reportes gerenciales (EV-10).

## Decisión

Un worker del contexto de Integración ERP consolida los eventos de venta confirmados y genera el lote CSV/XML cada 30 minutos, transfiriéndolo al ERP dentro de su ventana admitida, con conciliación de diferencias (ver `RES-02` en `DDA_07_Drivers_de_Restriccion_RES.md` y `CU-13` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Respeta el único mecanismo disponible del ERP sin requerir su modificación.
- Automatiza la consolidación que hoy se realiza manualmente con hojas de cálculo, reduciendo el riesgo de retraso en el cierre mensual.

### Negativas
- La consistencia contable entre SGO-Logic y el ERP es eventual, con una ventana de hasta 30 minutos: cualquier reporte gerencial que dependa del ERP en tiempo real no reflejará las ventas de los últimos 30 minutos.
- Requiere un mecanismo de conciliación de diferencias cuyo detalle exacto de detección y resolución no está completamente especificado en el repositorio (ver marcador dejado en la ficha `CU-13` de `DDA_04_Drivers_Funcionales_CDU.md`).

## Preocupaciones y evidencia que atiende

PRE-11, PRE-12. Evidencia: EV-10, EV-15.
