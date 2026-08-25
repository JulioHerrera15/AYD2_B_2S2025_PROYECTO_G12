# ADR-006: Almacén de auditoría append-only en base separada

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

La Directora de Finanzas exige poder reconstruir quién ejecutó cada operación, cuándo ocurrió y qué se modificó, con evidencia recuperable en menos de un segundo, de cara a las auditorías anuales de la SAT (EV-30), y tiene autoridad de veto formal sobre cualquier decisión que reduzca la trazabilidad de las operaciones o comprometa el cumplimiento fiscal (EV-38). Ese registro no puede competir por rendimiento con la carga transaccional de Pagos o Facturación, ni ser alterable por nadie, ni siquiera por el propio sistema.

## Decisión

Auditoría es un contexto delimitado con base de datos propia, en una instancia separada tanto de SQL Operacional como de SQL Finanzas, con permisos restringidos a solo `INSERT` (sin `UPDATE` ni `DELETE`), indexada por identificador de transacción, y alimentada de forma asíncrona vía eventos de RabbitMQ desde el resto de contextos (ver `CU-05` en `DDA_04_Drivers_Funcionales_CDU.md` y su resolución de despliegue en `DDA_06_Despliegue.md`).

## Alternativas consideradas

Se evaluó mantener la bitácora de auditoría como una tabla más dentro de la instancia SQL de Finanzas, con permisos de solo inserción. Ventaja: una base menos que operar. Desventaja: las lecturas de auditoría competirían por recursos con la carga transaccional de Pagos y Facturación justo durante los picos, poniendo en riesgo el presupuesto de recuperación de menos de un segundo. También se consideró usar una herramienta de auditoría especializada, descartada por costo y por exceder las competencias del equipo. Se eligió una base de datos propia y separada con permisos restringidos a solo inserción, porque garantiza la inmutabilidad a nivel de motor sin tecnología adicional y aísla las lecturas de auditoría de la carga transaccional.

## Consecuencias

### Positivas
- La inmutabilidad se garantiza a nivel de permisos de base de datos, sin necesitar una tecnología de auditoría especializada.
- Al no compartir instancia con Finanzas, sus lecturas no compiten con la carga transaccional de Pagos ni de Facturación, lo que sostiene el presupuesto de recuperación en menos de un segundo.

### Negativas
- Es una base de datos adicional que operar, respaldar y monitorear.
- Al alimentarse de forma asíncrona vía eventos, existe una ventana breve en la que una operación ya ocurrió pero aún no está indexada en Auditoría — mitigada porque el evento se registra en la misma transacción de origen antes de considerarse completo (ver ADR-003, patrón Outbox).

## Preocupaciones y evidencia que atiende

PRE-05, PRE-07. Evidencia: EV-30, EV-26, EV-38.
