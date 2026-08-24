# ADR-009: Circuit breaker y reintentos para PayKlik y LogiExpress

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

PayKlik tiene latencia variable (de 800 ms hasta 5 s en picos) y estuvo indisponible 45 minutos durante el último Black Friday, con pérdidas estimadas en USD 150,000 (EV-11, EV-17). LogiExpress falla por timeout en aproximadamente 1 de cada 50 solicitudes (EV-18). Ambos son proveedores contractuales que no pueden reemplazarse dentro del horizonte del proyecto (RES-04, RES-05), y ninguna falla suya puede bloquear el flujo principal de la orden ni hacer que se pierda.

## Decisión

Los adaptadores hacia PayKlik y LogiExpress aplican reintentos con backoff y dead-letter exchange sobre RabbitMQ; en el caso de LogiExpress, específicamente, se agrega una capa anticorrupción con circuit breaker (ver `RES-05` en `DDA_07_Drivers_de_Restriccion_RES.md`, y `CU-03`/`CU-11` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Una falla o lentitud de cualquiera de los dos proveedores no bloquea la orden ni se traduce en pérdida de la compra para el cliente.
- Cada reintento queda registrado y es auditable, cumpliendo la exigencia de que ningún intento fallido quede sin respaldo (EV-28).

### Negativas
- El cliente puede percibir estados intermedios más largos de lo habitual ("Pago Pendiente", "Guía Pendiente") durante un incidente prolongado del proveedor.
- El circuit breaker requiere calibrar umbrales de apertura y cierre contra el patrón real de fallas de cada proveedor. [PENDIENTE-ANGEL: los umbrales específicos de apertura/cierre del circuit breaker (tasa de error, ventana de observación, tiempo de reintento) no están definidos en el repositorio].

## Preocupaciones y evidencia que atiende

PRE-01, PRE-02. Evidencia: EV-11, EV-17, EV-18, EV-28.
