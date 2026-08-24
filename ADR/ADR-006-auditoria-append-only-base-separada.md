# ADR-006: Almacén de auditoría append-only en base separada

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

La Directora de Finanzas exige poder reconstruir quién ejecutó cada operación, cuándo ocurrió y qué se modificó, con evidencia recuperable en menos de un segundo, de cara a las auditorías anuales de la SAT (EV-30), y tiene autoridad de veto formal sobre cualquier decisión que reduzca la trazabilidad de las operaciones o comprometa el cumplimiento fiscal (EV-38). Ese registro no puede competir por rendimiento con la carga transaccional de Pagos o Facturación, ni ser alterable por nadie, ni siquiera por el propio sistema.

## Decisión

Auditoría es un contexto delimitado con base de datos propia, en una instancia separada tanto de SQL Operacional como de SQL Finanzas, con permisos restringidos a solo `INSERT` (sin `UPDATE` ni `DELETE`), indexada por identificador de transacción, y alimentada de forma asíncrona vía eventos de RabbitMQ desde el resto de contextos (ver `CU-05` en `DDA_04_Drivers_Funcionales_CDU.md` y su resolución de despliegue en `DDA_06_Despliegue.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- La inmutabilidad se garantiza a nivel de permisos de base de datos, sin necesitar una tecnología de auditoría especializada.
- Al no compartir instancia con Finanzas, sus lecturas no compiten con la carga transaccional de Pagos ni de Facturación, lo que sostiene el presupuesto de recuperación en menos de un segundo.

### Negativas
- Es una base de datos adicional que operar, respaldar y monitorear.
- Al alimentarse de forma asíncrona vía eventos, existe una ventana breve en la que una operación ya ocurrió pero aún no está indexada en Auditoría — mitigada porque el evento se registra en la misma transacción de origen antes de considerarse completo (ver ADR-003, patrón Outbox).

## Preocupaciones y evidencia que atiende

PRE-05, PRE-07. Evidencia: EV-30, EV-26, EV-38.
