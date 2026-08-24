# ADR-011: Certificación fiscal asíncrona ante la SAT (estado Pendiente Fiscal)

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

Toda orden requiere una factura XML certificada por la SAT, con CUI y serie autorizada, antes de considerarse finalizada (EV-19), pero el servicio de la SAT tiene tiempos de respuesta variables e impredecibles. Bloquear la confirmación de la orden hasta obtener la certificación contradice la expectativa de una experiencia de compra fluida y equivalente entre canales (PRE-13, PRE-14).

## Decisión

La certificación fiscal se ejecuta de forma asíncrona: la orden pasa a "Confirmada - Pendiente Fiscal" en cuanto se acepta el cobro, mientras el documento se encola vía el canal MOM de RabbitMQ dedicado a la SAT y se reintenta con backoff exponencial hasta certificarse, momento en el que la orden pasa a "Confirmada Fiscalmente" (ver `RES-07` en `DDA_07_Drivers_de_Restriccion_RES.md` y `CU-04` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

Se evaluaron cinco opciones: (1) certificación asíncrona con estado intermedio "Pendiente Fiscal"; (2) certificación síncrona bloqueante, descartada porque la latencia impredecible de la SAT destruiría la experiencia de compra y la resiliencia; (3) un híbrido con intento síncrono de timeout corto y respaldo asíncrono, descartado por añadir complejidad sin beneficio claro sobre la opción asíncrona; (4) certificación en lotes al final del día, descartada por posible incumplimiento de la normativa de facturación en línea; y (5) delegar la certificación al ERP, descartada porque el ERP no se modifica y no puede asumir esa responsabilidad. Se eligió la certificación asíncrona porque permite confirmar la compra al cliente en milisegundos mientras el documento se encola y se reintenta hasta certificarse, cumpliendo la obligación fiscal de forma diferida pero garantizada.

## Consecuencias

### Positivas
- El cliente recibe confirmación de su compra sin esperar a la respuesta de la SAT.
- La obligación fiscal (RES-07) se sigue cumpliendo siempre, solo que de forma diferida y visible mediante un estado intermedio explícito.

### Negativas
- Existe una ventana en la que la orden está confirmada para el cliente pero aún no tiene respaldo fiscal completo; esto exige tener definido con precisión qué significa "finalizar la orden" frente a la SAT — vacío de información `VAC-03` (`DDA_00_Catalogo_Evidencia.md`, sección 10), hoy cubierto solo por un supuesto adoptado por el equipo (el cierre fiscal), pendiente de confirmar con el cliente.
- Si se agota el máximo de reintentos, se genera una alerta interna para revisión manual (`CU-04`), lo que introduce un paso operativo humano no completamente automatizado.

## Preocupaciones y evidencia que atiende

PRE-06, PRE-07. Evidencia: EV-19, EV-29, EV-30.
