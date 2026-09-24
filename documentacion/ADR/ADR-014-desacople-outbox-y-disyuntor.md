# ADR-014: El desacople exigido por el enunciado se cubre con Outbox y disyuntor, sin mecanismo nuevo

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-11

## Contexto

El enunciado de la Fase 2 exige desacople entre los componentes del sistema, sin nombrar un mecanismo concreto para lograrlo. El diseño ya tomó dos decisiones que, juntas, cubren esa exigencia: el patrón Outbox con publicador por polling (ADR-003) para la comunicación entre contextos de escritura, y el disyuntor con backoff exponencial (ADR-009) para las llamadas hacia proveedores externos. Faltaba dejar explícito que esas dos decisiones, y no una tercera por inventar, son la respuesta al requisito de desacople.

## Decisión

El desacople se cubre con los dos mecanismos ya decididos: el patrón Outbox con publicador por polling (ADR-003), para que ningún componente dependa de la disponibilidad del broker en el momento de la escritura, y el disyuntor con backoff exponencial (ADR-009), para que la caída de un proveedor externo no se propague al flujo de negocio. No se introduce un tercer mecanismo: ambos ya estaban decididos por otras razones y, combinados, satisfacen la exigencia de desacople sin agregar una pieza nueva al diseño.

## Alternativas consideradas

Se evaluaron dos opciones: (1) declarar que Outbox (ADR-003) y disyuntor con backoff (ADR-009) ya satisfacen el requisito de desacople; y (2) diseñar un mecanismo de desacople adicional específico para cumplir el enunciado, descartada porque duplicaría el trabajo que ADR-003 y ADR-009 ya hacen y contradice la justificación original de ambas decisiones, que fue precisamente evitar que un componente dependa síncronamente de otro. Se eligió declarar cubierto el requisito con lo ya decidido porque introducir un mecanismo nuevo sin una falla que lo justifique va contra el criterio de no diseñar para requisitos hipotéticos.

## Consecuencias

### Positivas
- Ningún contexto del backbone invoca directamente a otro por HTTP síncrono dentro del backbone: toda comunicación entre contextos de escritura pasa por eventos vía Outbox, y toda comunicación hacia proveedores externos pasa por el disyuntor. No hay una ruta síncrona oculta que reintroduzca el acoplamiento que ADR-003 y ADR-009 ya evitaban.
- El requisito de desacople del enunciado queda satisfecho sin negociar una decisión de arquitectura nueva ni reabrir alternativas que ya se descartaron en ADR-003 y ADR-009.

### Negativas
- Latencia añadida por el ciclo de polling del Outbox (ADR-003), que se suma a la de cualquier operación que dependa de un evento publicado por otro contexto.
- Consistencia eventual entre contextos, no transaccional: un consumidor puede ver el estado de otro contexto con retraso. Se acepta explícitamente, en línea con lo ya asumido desde ADR-002 y ADR-003.

## Preocupaciones y evidencia que atiende

PRE-01, PRE-02, PRE-11. Evidencia: EV-11, EV-16, EV-17, EV-18, EV-28.
