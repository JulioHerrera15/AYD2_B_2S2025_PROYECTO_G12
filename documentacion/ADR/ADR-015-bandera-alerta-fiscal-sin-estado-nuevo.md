# ADR-015: Bandera de alerta fiscal con reintento manual, sin estado nuevo, cuando se agotan los reintentos de certificación

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-11

## Contexto

ADR-011 estableció la certificación fiscal asíncrona con el estado intermedio `CONFIRMADA_PENDIENTE_FISCAL`, y dijo que al agotar los reintentos ante la SAT se genera una alerta interna para revisión manual (CU-04). Lo que ADR-011 no definió es qué pasa con la orden misma cuando eso ocurre: sin una decisión explícita, la orden se quedaba varada en `CONFIRMADA_PENDIENTE_FISCAL` sin ninguna ruta de salida, ni para el cliente ni para quien tuviera que resolver el incidente.

## Decisión

No se agrega un estado nuevo a la máquina de estados de la orden. La orden se marca con una bandera de alerta fiscal, visible y consultable junto con su motivo y el momento en que se levantó, y se mantiene en `CONFIRMADA_PENDIENTE_FISCAL`. Se habilita un reintento manual que republica la solicitud de certificación hacia Facturación; si ese reintento certifica con éxito, la bandera se limpia y la orden sigue su curso normal hacia `CONFIRMADA`.

## Alternativas consideradas

Se evaluaron cuatro opciones: (1) bandera de alerta fiscal sobre el estado existente, con reintento manual; (2) agregar un estado nuevo de error fiscal a la máquina de estados, descartada porque el contrato de estados de `ordenes-eventos-v1` está congelado y lo consumen Consulta de Estado, Auditoría y la integración con el POS — agregar un estado ahí obliga a modificar esos cuatro contextos, no solo Órdenes; (3) cancelar automáticamente la orden al agotar los reintentos, descartada porque la venta comercial ya ocurrió (la orden ya pasó por el cobro aplicado antes de llegar aquí) y cancelarla por un problema exclusivamente fiscal contradice la obligación de que la orden nunca desaparezca; y (4) reintentar indefinidamente sin alerta, descartada porque oculta el problema en vez de escalarlo, que es exactamente lo que ADR-011 quería evitar al pedir la alerta interna. Se eligió la bandera sobre el estado existente porque resuelve el vacío sin tocar un contrato ya congelado y sin comprometer ninguna de las dos obligaciones en conflicto, la fiscal y la comercial.

## Consecuencias

### Positivas
- La obligación fiscal sigue siendo exigible y visible: la bandera y su motivo quedan persistidos en la orden, no se pierden ni se ocultan en un log.
- La orden permanece consultable en todo momento, sin desaparecer ni bloquear al cliente por un problema que es responsabilidad de la SAT o de la integración con ella.
- El contrato de estados de `ordenes-eventos-v1` no se toca: Consulta de Estado, Auditoría y la integración con el POS siguen funcionando sin cambios.

### Negativas
- Requiere intervención humana explícita para salir de la alerta: alguien debe accionar el reintento manual, que es justo lo que ADR-011 pedía al hablar de revisión manual, pero es un paso operativo que no se resuelve solo.
- Una orden con la bandera levantada puede permanecer en `CONFIRMADA_PENDIENTE_FISCAL` indefinidamente si nadie atiende la alerta; no hay un mecanismo de escalamiento automático más allá de la alerta inicial.

## Preocupaciones y evidencia que atiende

PRE-06, PRE-07. Evidencia: EV-19, EV-29, EV-30.
