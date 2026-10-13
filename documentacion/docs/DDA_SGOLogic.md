# Documento de Definición de Arquitectura (DDA)
# SGO-Logic — Sistema de Gestión de Órdenes y Logística Omnicanal

**Cliente:** MegaStore
**Versión:** 2.5
**Naturaleza del documento:** Definición de arquitectura de la Fase 1. Comprende la delimitación del problema, la identificación de partes interesadas y drivers, las decisiones de arquitectura y las vistas que las sustentan. La versión 2.0 agrega la sección 14, Cambios de la Fase 2, que documenta cómo se materializó esa arquitectura en el MVP; las secciones 1 a 13 conservan el contenido de la Fase 1 y solo incorporan notas que remiten a los ADR posteriores.

---

## Índice

1. Resumen ejecutivo
2. Catálogo de evidencia y convención de identificadores
3. Partes interesadas y preocupaciones arquitectónicas
4. Caso de negocio: casos de uso de negocio y primera descomposición
5. Características del sistema priorizadas
6. Drivers de requisitos funcionales y casos de uso expandidos
7. Drivers de calidad: escenarios de atributos de calidad
8. Drivers de restricción
9. Restricciones de infraestructura
10. Decisiones de arquitectura (ADR)
11. Vistas arquitectónicas
    - 11.1 Diagrama de contexto
    - 11.2 Diagrama de bloques
    - 11.3 Diagrama de componentes
    - 11.4 Diagramas de despliegue (lógico y físico)
    - 11.5 Diagramas de casos de uso expandidos
12. Matrices de trazabilidad
13. Vacíos de información y supuestos
14. Cambios de la Fase 2

---

# 1. Resumen ejecutivo

MegaStore es una cadena regional de tiendas por departamentos con presencia en Guatemala, El Salvador y Costa Rica: 32 tiendas físicas, 6,500 empleados, un canal de comercio electrónico que representa el 25% de las ventas y crece cerca del 15% anual, y una facturación anual aproximada de USD 450 millones. Su operación actual se apoya en aplicaciones y procedimientos que evolucionaron de forma independiente, sin una visión unificada: cada tienda conserva información local de ventas e inventario, el comercio electrónico opera con datos separados, las devoluciones pueden tardar hasta siete días hábiles por falta de una vista consolidada y Finanzas concilia ventas de forma manual con hojas de cálculo.

SGO-Logic es la iniciativa que resuelve esa fragmentación. Su objetivo es permitir que un cliente inicie, continúe y complete una compra desde cualquier canal —tienda física, web, aplicación móvil o centro de atención— con una experiencia consistente, sobre una única visión de inventario, cobro, facturación fiscal y despacho.

Este documento define la arquitectura de SGO-Logic. Establece el alcance del sistema, identifica a las partes interesadas y sus preocupaciones, deriva los drivers funcionales, de calidad y de restricción, y documenta las decisiones de arquitectura con sus alternativas y consecuencias. Cada decisión se sustenta en evidencia del negocio y se refleja en al menos una vista arquitectónica.

La arquitectura adopta un estilo de microservicios organizados por contexto delimitado, con comunicación orientada a eventos sobre un bus de mensajería, orquestación del ciclo de vida de la orden mediante una saga centralizada, y separación entre el modelo de escritura y el modelo de lectura para sostener los tiempos de respuesta exigidos. El despliegue inicial es on-premises, empaquetado en contenedores, con una ruta de migración a la nube preparada. Las decisiones priorizan el valor para el negocio, la resiliencia ante fallas de sistemas externos, la trazabilidad fiscal y la operación dentro de los límites de presupuesto y de las capacidades del equipo.

---

# 2. Catálogo de evidencia y convención de identificadores

Esta sección extrae y numera la evidencia del caso de negocio, y fija la convención de identificadores que da coherencia y trazabilidad al resto del documento. Cada afirmación de la arquitectura apunta a por lo menos una evidencia de este catálogo.

## 2.1 Convención de identificadores

| Prefijo | Artefacto |
|---|---|
| `EV-##` | Evidencia del caso de negocio |
| `STK-##` | Parte interesada (stakeholder) |
| `PRE-##` | Preocupación arquitectónica |
| `CAR-##` | Característica priorizada del sistema |
| `CUN-##` | Caso de uso de negocio |
| `CU-##` | Caso de uso expandido del sistema |
| `DRF-##` | Driver de requisito funcional |
| `EAC-##` | Escenario de atributo de calidad |
| `RES-##` | Driver de restricción |
| `ADR-###` | Registro de decisión de arquitectura |
| `CON-##` | Conflicto entre preocupaciones |
| `VAC-##` | Vacío de información |
| `AN-##` / `TN-##` | Actor / trabajador de negocio |
| `AS-##` | Actor del sistema |

## 2.2 Contexto organizacional

| ID | Evidencia | Implicación arquitectónica |
|---|---|---|
| EV-01 | Cadena con 25 años de operación en Guatemala, El Salvador y Costa Rica: 32 tiendas, 6,500 empleados, USD 450M de facturación anual. | Sistema multi-país y multi-tienda; zonas horarias, monedas y normativa fiscal por país. |
| EV-02 | El comercio electrónico es el 25% de las ventas y crece cerca del 15% anual. | El canal digital es material; la arquitectura se dimensiona para crecimiento sostenido. |
| EV-03 | Comercializa línea blanca, electrónica, ropa y calzado, con promociones estacionales, combos y financiamiento a 3, 6 y 12 meses. | El catálogo y el motor de precios tienen complejidad propia. |
| EV-04 | La Dirección busca que un cliente inicie, continúe y complete una compra desde cualquier canal con experiencia consistente. | Objetivo estratégico raíz: identidad de cliente unificada y estado de compra compartido. |
| EV-05 | Opera dos centros de distribución sin una visión centralizada de existencias, ubicaciones, reservas, entradas y salidas. | Justifica un contexto de inventario unificado. |
| EV-06 | No existe una herramienta integrada para que el centro de atención consulte el historial completo de compras, devoluciones e incidencias. | Justifica un modelo de lectura consolidado. |

## 2.3 Situación actual y costos del problema

| ID | Evidencia | Implicación arquitectónica |
|---|---|---|
| EV-07 | Las aplicaciones evolucionaron por separado; cada tienda y el comercio electrónico operan con información aislada. | Diagnóstico de silos; sustenta un sistema de registro único. |
| EV-08 | Ante una compra en línea, el personal de logística llama por teléfono a cada tienda para confirmar existencia. | Proceso manual a eliminar; justifica el inventario en tiempo real. |
| EV-09 | Las devoluciones y cambios tardan hasta siete días hábiles por falta de una vista consolidada de compra, pago, despacho y entrega. | Línea base de postventa; refuerza el modelo de lectura consolidado. |
| EV-10 | Finanzas concilia ventas manualmente con hojas de cálculo de distintos canales y tiendas. | Justifica la sincronización automatizada con el ERP. |
| EV-11 | En el último Black Friday la pasarela de pagos estuvo indisponible 45 minutos, con pérdidas estimadas en USD 150,000. | Evidencia cuantificada del riesgo de dependencia externa. |
| EV-12 | Cada envío incorrecto cuesta cerca de USD 25 en logística inversa; el objetivo es reducir estos errores en 70%. | Meta medible ligada a la confiabilidad del inventario. |

## 2.4 Sistemas existentes y restricciones de integración

| ID | Evidencia | Implicación arquitectónica |
|---|---|---|
| EV-13 | El ERP es la plataforma contable-financiera, sobre AS/400 de los años noventa. | Sistema legacy con semántica propia; requiere capa anticorrupción. |
| EV-14 | La Junta Directiva determinó que el ERP no se reemplaza ni modifica en este horizonte. | Restricción dura no negociable. |
| EV-15 | El ERP solo recibe resúmenes de ventas por archivo CSV o XML, procesables cada 30 minutos. | Integración batch; consistencia contable eventual con ventana de 30 minutos. |
| EV-16 | Las 32 instancias de POS no se reemplazan y deben recibir eventos con manejo de errores y reintentos. | El POS es consumidor de eventos; exige entrega confiable e idempotente. |
| EV-17 | PayKlik ofrece REST con latencia de 800 ms hasta 5 s en picos, con contrato vigente por tres años. | Dependencia externa lenta y variable; no puede invocarse de forma bloqueante. |
| EV-18 | LogiExpress ofrece REST con cerca de 1 de cada 50 solicitudes en timeout, con contrato vigente por dos años. | Tasa de fallo del 2%; obliga a reintentos con respaldo. |
| EV-19 | La emisión de factura por XML a la SAT es obligatoria antes de finalizar la orden, con tiempos de respuesta variables. | Obligación fiscal frente a un servicio de latencia impredecible. |
| EV-20 | Un proveedor externo presta SMS, correo y notificaciones push, con disponibilidad fuera de control. | Las notificaciones deben ser estrictamente asíncronas. |

## 2.5 Operación omnicanal esperada

| ID | Evidencia | Implicación arquitectónica |
|---|---|---|
| EV-21 | Una compra puede iniciar en un canal y finalizar en otro, reconociendo al cliente durante todo el proceso. | Identidad unificada y estado de compra fuera de la sesión del canal. |
| EV-22 | Antes de confirmar una venta debe verificarse la disponibilidad para no comprometer una unidad a más de un cliente. | Único punto que exige consistencia fuerte; define el mecanismo de reserva. |
| EV-23 | Cuando una compra no concluye, el inventario reservado debe liberarse en un tiempo acorde con la operación. | Reservas con vencimiento automático. |
| EV-24 | Se aceptan tarjetas, transferencias bancarias y billeteras digitales. | Múltiples medios de pago con flujos distintos. |
| EV-25 | Por cumplimiento, no se almacenan números completos de tarjeta ni códigos de seguridad. | Tokenización; descarta un procesador de tarjeta propio. |
| EV-26 | Cada intento de cobro debe quedar relacionado con la orden, el canal, la fecha y su resultado. | Registro de intentos, no solo del resultado final. |
| EV-27 | Para el próximo Black Friday se espera una demanda de hasta ocho veces la de un día normal. | Factor de escalamiento declarado. |
| EV-28 | Se busca no perder la orden si el proveedor de pago falla, procesar sin depender totalmente de él y auditar cada intento fallido. | Exige desacoplar la captura del cobro; rutas alternativas, no procesador propio. |
| EV-29 | Tras confirmar la venta se genera la documentación fiscal y se mantiene la relación entre venta, devoluciones y reembolsos. | Trazabilidad documental encadenada. |
| EV-30 | Por auditorías de la SAT, Finanzas debe reconstruir quién ejecutó cada operación, cuándo y qué cambió, recuperable en menos de un segundo. | Registro append-only indexado. |
| EV-31 | El despacho puede salir de un centro de distribución, una tienda o recogida, y el método puede cambiar tras confirmar. | La orden es un agregado de larga vida y mutable. |
| EV-32 | Logística necesita saber qué preparar, desde qué ubicación despachar y a quién entregar, con inventario en tiempo real. | Vista operativa dedicada, alimentada por eventos. |
| EV-33 | Consultar el estado de una orden desde cualquier dispositivo no puede superar los cinco segundos. | Presupuesto de latencia de consulta explícito. |
| EV-34 | El cliente debe recibir notificaciones, pero una falla de comunicación no puede detener la operación principal. | El desacople de notificaciones está autorizado. |
| EV-35 | Habrá un carrito unificado entre canales, con métodos de pago según el canal. | El carrito es estado compartido, no de sesión. |

## 2.6 Partes interesadas, restricciones y proceso

| ID | Evidencia | Implicación arquitectónica |
|---|---|---|
| EV-36 | La Dirección de Comercialización exige la misma experiencia en todos los canales, sin silos. | Confirma el objetivo omnicanal. |
| EV-37 | El Gerente de TI operará la solución; rechaza modificar ERP-Core o POS, exige mantenibilidad interna y monitoreo de sistemas externos. | Restringe el espacio de alternativas; poder de rechazo técnico. |
| EV-38 | La Directora de Finanzas tiene veto sobre decisiones que reduzcan la trazabilidad o comprometan el cumplimiento fiscal. | Cualquier optimización que sacrifique auditoría es inviable. |
| EV-39 | El Jefe de Logística prioriza reducir errores de despacho y la visibilidad oportuna de órdenes. | Conecta con las metas de logística. |
| EV-40 | La Junta ratifica la permanencia del ERP-Core y del POS. | Origen de restricciones de integración. |
| EV-41 | La inversión inicial aprobada es de USD 150,000 para todo el proyecto. | Límite de inversión, fondo compartido. |
| EV-42 | El costo operativo mensual no debe superar USD 1,200 sin aprobación adicional. | Techo operativo estrecho. |
| EV-43 | El Black Friday de noviembre es fecha fija; se espera una ruta viable de pagos y facturación en el primer mes. | Restricción temporal y criterio de priorización. |
| EV-44 | La solución cumple protección de datos y facturación electrónica de la SAT en los tres países. | Cumplimiento regulatorio multi-país. |
| EV-45 | Los contratos vigentes con PayKlik y LogiExpress deben respetarse. | Impide reemplazar proveedores en el horizonte. |
| EV-46 | El equipo domina Java, .NET y Docker básico; toda desviación debe cuantificar adopción y capacitación. | Restricción de capacidad organizacional. |
| EV-47 | Se busca minimizar el consumo energético y mantener la facturación totalmente electrónica. | Penaliza el sobredimensionamiento permanente. |
| EV-48 | El despliegue inicial es on-premises, reutilizando la inversión existente. | Fija la topología inicial. |
| EV-49 | La arquitectura debe permitir una migración planificada a la nube sin rediseño. | Requisito de portabilidad diferida. |
| EV-50 | Se emplea UML 2.5.1 cuando corresponde, declarando explícitamente cualquier notación complementaria. | Convención de notación. |
| EV-51 | La priorización de trabajo antepone los componentes base de la arquitectura a los cosméticos. | El backlog se ordena por backbone. |
| EV-53 | Las decisiones se registran mediante ADR como registro vivo, y los diagramas son vistas del documento. | Relación entre artefactos. |
| EV-54 | La fase incorpora crédito comercial de "Compra ahora, paga en 30 días" para clientes elegibles. | Se requiere un contexto de Crédito con línea, cuentas por cobrar, elegibilidad y cobranza propia. |
| EV-55 | La elegibilidad depende del historial real de compras, pagos, devoluciones e incidentes que reside en el ERP. | El historial debe importarse y transformarse sin consultar el ERP durante checkout. |
| EV-56 | El checkout debe decidir rápidamente si muestra crédito; si la evaluación no responde a tiempo, debe ofrecer PayKlik. | Crédito mantiene un perfil precalculado local y una ruta de fallback. |
| EV-57 | El ERP legado no se modifica y su integración se realiza por lotes; el MVP necesita datos reproducibles para demostrar el flujo. | `mock-erp` se amplía con snapshots de historial por lote, sin convertirlo en una base de dominio. |
| EV-58 | Una compra a crédito debe terminar en orden confirmada o rechazada, con el cliente notificado igual que en la ruta de PayKlik. | Órdenes conserva la saga centralizada y agrega la bifurcación de evaluación de Crédito. |

---

# 3. Partes interesadas y preocupaciones arquitectónicas

## 3.1 Partes interesadas

**Gobierno y patrocinio**

| ID | Parte interesada | Interés principal | Poder |
|---|---|---|---|
| STK-01 | Junta Directiva | Preservar la inversión en sistemas existentes y limitar el riesgo de transición. | Decisorio; su determinación sobre ERP y POS es no negociable. |
| STK-02 | Dirección / Gerencia General | Habilitar la operación omnicanal dentro del presupuesto y del plazo comercial. | Decisorio; aprueba inversión y fija el plazo. |

**Ejecutivos con interés directo**

| ID | Parte interesada | Interés principal | Poder |
|---|---|---|---|
| STK-03 | Carlos Méndez — Gerente de TI | Operar sin interrupciones en picos, sin tocar el legacy, con equipo interno capaz de mantener. | Alto; rechaza propuestas que modifiquen ERP o POS. |
| STK-04 | Ana Lucía Fuentes — Directora de Finanzas | Trazabilidad completa y cumplimiento fiscal ante auditorías de la SAT. | Alto, con veto formal. |
| STK-05 | Roberto Quezada — Jefe de Logística | Reducir errores de despacho y obtener visibilidad oportuna de órdenes. | Medio; define metas operativas medibles. |
| STK-06 | Dirección de Comercialización | Experiencia de compra idéntica en todos los canales. | Medio-alto; voz del objetivo omnicanal. |
| STK-07 | Jefatura de Operaciones | Evitar la repetición de la pérdida por indisponibilidad en alta demanda. | Medio; aporta la evidencia del incidente y la proyección de demanda. |

**Operativos internos**

| ID | Parte interesada | Interés principal |
|---|---|---|
| STK-08 | Equipo interno de desarrollo y mantenimiento | Que la solución sea construible y mantenible con Java, .NET y Docker básico. |
| STK-09 | Personal de tienda | Vender sin depender de llamadas telefónicas, sin cambiar su herramienta. |
| STK-10 | Agentes del centro de atención | Consultar en una sola vista compras, pagos, despachos, devoluciones e incidencias. |
| STK-11 | Personal de logística y centros de distribución | Saber qué preparar, desde dónde despachar y a quién entregar, con inventario confiable. |
| STK-12 | Personal de Finanzas y contabilidad | Conciliar ventas sin hojas de cálculo manuales y cerrar el mes sin retrasos. |

**Externos**

| ID | Parte interesada | Interés principal | Poder |
|---|---|---|---|
| STK-13 | Cliente final | Comprar desde cualquier canal con experiencia consistente y seguimiento del estado. | Determinante en valor. |
| STK-14 | SAT | Recibir documentación fiscal válida y poder auditar las operaciones. | Alto, no negociable. |
| STK-15 | PayKlik | Mantener el contrato y su interfaz REST. | Alta dependencia técnica por su latencia variable. |
| STK-16 | LogiExpress | Mantener el contrato y su integración REST. | Alta dependencia técnica por su tasa de timeout. |
| STK-17 | Proveedor de notificaciones | Prestar el servicio bajo sus propias condiciones. | Baja; disponibilidad fuera de control. |
| STK-18 | Reguladores de protección de datos | Cumplimiento de la normativa en los tres países. | Alto, no negociable. |
| STK-19 | Finanzas / Crédito | Determinar elegibilidad con historial comercial verificable, administrar cuentas por cobrar y bloquear mora. | Alto. |
| STK-20 | Equipo de Integración ERP | Traducir datos del AS/400 y entregar lotes confiables sin contaminar los dominios nuevos. | Medio-alto. |

## 3.2 Preocupaciones arquitectónicas

**Continuidad y resiliencia**

- **PRE-01** — El sistema sostiene picos de hasta ocho veces la carga normal sin interrupción de la venta. (STK-03, STK-07, STK-02)
- **PRE-02** — La falla de un sistema externo no provoca la pérdida de una orden ya iniciada. (STK-03, STK-07, STK-02)
- **PRE-03** — Las fallas de canales secundarios, en particular las notificaciones, permanecen aisladas del flujo principal. (STK-03, STK-13)
- **PRE-04** — El comportamiento y la disponibilidad de las dependencias externas son observables de forma continua. (STK-03)

**Trazabilidad y cumplimiento**

- **PRE-05** — Es posible reconstruir quién ejecutó cada operación, cuándo y qué se modificó. (STK-04, STK-14)
- **PRE-06** — La documentación fiscal es válida, oportuna y mantiene la relación entre venta, devoluciones y reembolsos. (STK-04, STK-14, STK-12)
- **PRE-07** — Ninguna transacción queda sin respaldo recuperable. (STK-04, STK-14)
- **PRE-08** — El tratamiento de datos personales y de medios de pago cumple la normativa, sin almacenar datos sensibles de tarjeta. (STK-18, STK-04, STK-13)

**Integridad y consistencia del negocio**

- **PRE-09** — Una misma unidad de inventario no se compromete a más de un cliente. (STK-05, STK-06, STK-13)
- **PRE-10** — El inventario reservado por compras no concluidas se libera automáticamente. (STK-05, STK-06)
- **PRE-11** — La información propagada hacia los sistemas legacy no se pierde ni se duplica. (STK-03, STK-12, STK-04)
- **PRE-12** — La consistencia contable con el ERP está sujeta a una ventana de hasta 30 minutos y es conciliable. (STK-04, STK-12)

**Experiencia y valor para el negocio**

- **PRE-13** — La experiencia de compra es equivalente en todos los canales, permitiendo iniciar en uno y concluir en otro. (STK-06, STK-13, STK-02)
- **PRE-14** — Las consultas de estado e historial responden dentro de límites de tiempo aceptables. (STK-10, STK-13, STK-04)
- **PRE-15** — La operación cuenta con visibilidad oportuna de órdenes e inventario para reducir errores de despacho. (STK-05, STK-11)

**Viabilidad técnica y económica**

- **PRE-16** — Los sistemas ERP-Core y POS no se modifican ni se reemplazan. (STK-01, STK-03)
- **PRE-17** — La arquitectura es mantenible por el equipo interno con sus competencias actuales. (STK-03, STK-08)
- **PRE-18** — La inversión inicial y el costo operativo mensual se mantienen dentro de los límites aprobados. (STK-02, STK-01)
- **PRE-19** — La solución se despliega on-premises y permite una migración planificada a la nube sin rediseño. (STK-01, STK-03, STK-02)
- **PRE-20** — Existe una ruta viable de pagos y facturación para el primer mes, antes del Black Friday. (STK-02, STK-07, STK-03)
- **PRE-21** — La elegibilidad de crédito usa un historial comercial verificable y actualizado por lotes, sin depender del ERP en la ruta crítica. (STK-04, STK-19, STK-20)
- **PRE-22** — Una decisión de crédito rápida, sus compensaciones y su resultado son trazables y no cambian el final común de la orden. (STK-03, STK-04, STK-13, STK-19)

## 3.3 Conflictos entre preocupaciones

| ID | Conflicto | Preocupaciones enfrentadas | Resolución adoptada |
|---|---|---|---|
| CON-01 | Trazabilidad exhaustiva frente a rendimiento en picos | PRE-05, PRE-07 vs. PRE-01, PRE-14 | Registro de auditoría desacoplado de la ruta crítica, en base separada (ADR-006). |
| CON-02 | Obligación fiscal previa al cierre frente a experiencia | PRE-06 vs. PRE-13, PRE-14 | Certificación fiscal asíncrona con estado intermedio (ADR-011). |
| CON-03 | Independencia del proveedor de pagos frente a no almacenar datos de tarjeta | PRE-02 vs. PRE-08 | Rutas de pago alternativas y tokenización delegada (ADR-010). |
| CON-04 | Resiliencia y elasticidad frente al techo presupuestario | PRE-01, PRE-19 vs. PRE-18 | Stack open source on-premises con escalado bajo demanda (ADR-001, RES-04). |
| CON-05 | No modificar el legacy frente a consistencia contable oportuna | PRE-16, PRE-12 vs. PRE-07 | Consistencia eventual con ventana de 30 minutos y reconciliación (ADR-008). |
| CON-06 | Mantenibilidad con competencias actuales frente a sofisticación técnica | PRE-17 vs. PRE-01, PRE-02 | Número acotado de tecnologías, con capacitación presupuestada (RES-06). |
| CON-07 | Frescura del historial frente a disponibilidad y latencia del ERP | PRE-21, PRE-22 vs. PRE-01, PRE-14, PRE-16 | Snapshot batch en Integración ERP, perfil precalculado en Crédito y fallback a PayKlik si la consulta local no responde (ADR-023). |

---

# 4. Caso de negocio: casos de uso de negocio y primera descomposición

## 4.1 Notación

Se emplea UML 2.5.1 con extensiones de modelado de negocio: actor de negocio (externo), trabajador de negocio (rol interno) y caso de uso de negocio (proceso que produce valor observable). Los casos describen qué hace el negocio, con independencia de su grado de automatización.

## 4.2 Actores de negocio y trabajadores de negocio

| ID | Actor de negocio | Papel |
|---|---|---|
| AN-01 | Cliente | Inicia, continúa y concluye compras; solicita devoluciones y consulta estados. |
| AN-02 | SAT | Recibe y certifica documentación fiscal; audita operaciones. |
| AN-03 | PayKlik | Procesa los cobros con tarjeta. |
| AN-04 | LogiExpress | Genera guías y comunica estados de entrega. |
| AN-05 | Proveedor de notificaciones | Entrega mensajes por SMS, correo y push. |

| ID | Trabajador de negocio | Papel |
|---|---|---|
| TN-01 | Vendedor de tienda | Registra ventas presenciales y atiende recogidas. |
| TN-02 | Agente del centro de atención | Atiende compras y postventa; consulta historial. |
| TN-03 | Operador de logística y CD | Prepara, despacha y da seguimiento a pedidos. |
| TN-04 | Analista de Finanzas | Concilia ventas y participa en el cierre contable. |
| TN-05 | Responsable de comercialización | Define catálogo, precios, combos y promociones. |

## 4.3 Casos de uso de negocio de alto nivel

| ID | Caso de uso de negocio | Valor producido |
|---|---|---|
| CUN-01 | Gestionar venta omnicanal | Una orden confirmada, iniciada en cualquier canal y concluida en cualquier otro, con inventario comprometido de forma confiable. |
| CUN-02 | Administrar inventario unificado | Una única visión de existencias, ubicaciones y reservas de tiendas y centros de distribución. |
| CUN-03 | Gestionar cobro | Un cobro aplicado y auditado, o una orden preservada cuando el cobro no se completa. |
| CUN-04 | Emitir documentación fiscal | Documentos fiscales válidos, certificados y relacionados con la venta original. |
| CUN-05 | Ejecutar despacho y entrega | El producto entregado desde el origen correcto, con seguimiento hasta la recepción. |
| CUN-06 | Atender postventa | Devoluciones, cambios e incidencias resueltos con una vista consolidada. |
| CUN-07 | Conciliar y cerrar contablemente | Ventas conciliadas, sincronizadas con el ERP y con evidencia recuperable. |
| CUN-08 | Administrar catálogo, precios y promociones | Catálogo consistente en todos los canales. Gestionado por el Catálogo Comercial, dependencia externa al sistema. |
| CUN-09 | Comunicar al cliente el estado de su orden | El cliente informado sobre confirmación, preparación, tránsito, entrega o incidencias. |
| CUN-10 | Administrar crédito comercial | Clientes elegibles pueden comprar a 30 días; Finanzas mantiene la línea y cobra las cuentas por cobrar. |

El diagrama de casos de uso de negocio de alto nivel se presenta en la sección 11.

## 4.4 Primera descomposición

**CUN-01 Gestionar venta omnicanal:** reconocer al cliente en cualquier canal (CUN-01.1); mantener el carrito unificado (CUN-01.2); verificar disponibilidad y reservar inventario (CUN-01.3); confirmar la orden (CUN-01.4); modificar una orden confirmada (CUN-01.5); liberar reservas de compras no concluidas (CUN-01.6).

**CUN-02 Administrar inventario unificado:** consolidar existencias de tiendas y centros (CUN-02.1); registrar entradas y salidas (CUN-02.2); gestionar reservas y su vencimiento (CUN-02.3); consultar disponibilidad en tiempo real (CUN-02.4).

**CUN-03 Gestionar cobro:** ofrecer medios de pago según el canal (CUN-03.1); procesar un intento de cobro (CUN-03.2); registrar y auditar cada intento (CUN-03.3); preservar la orden ante indisponibilidad del proveedor (CUN-03.4); procesar reembolsos (CUN-03.5).

**CUN-04 Emitir documentación fiscal:** generar el documento fiscal (CUN-04.1); certificarlo ante la SAT (CUN-04.2); emitir documentos por devolución o reembolso (CUN-04.3); conservar y relacionar la evidencia fiscal (CUN-04.4).

**CUN-05 Ejecutar despacho y entrega:** determinar el origen del despacho (CUN-05.1); preparar el pedido (CUN-05.2); coordinar la guía con el proveedor (CUN-05.3); atender la recogida en tienda (CUN-05.4); dar seguimiento al estado de entrega (CUN-05.5).

**CUN-06 Atender postventa:** consultar el historial consolidado (CUN-06.1); registrar una devolución o cambio (CUN-06.2); reingresar el producto al inventario (CUN-06.3); gestionar incidencias (CUN-06.4).

**CUN-07 Conciliar y cerrar contablemente:** consolidar resúmenes por canal y tienda (CUN-07.1); transferirlos al ERP (CUN-07.2); conciliar diferencias (CUN-07.3); reconstruir evidencia para auditoría (CUN-07.4).

**CUN-09 Comunicar al cliente:** emitir notificaciones del ciclo de vida (CUN-09.1); atender la consulta de estado bajo demanda (CUN-09.2).

**CUN-10 Administrar crédito comercial:** importar el historial comercial (CUN-10.1); precalcular el perfil y la línea (CUN-10.2); ofrecer u ocultar crédito en checkout (CUN-10.3); evaluar una orden y emitir una cuenta por cobrar (CUN-10.4); consultar saldo y vencimiento (CUN-10.5); registrar abonos, vencimientos y bloqueos por mora (CUN-10.6).

## 4.5 Delimitación del sistema

| Caso de negocio | Alcance en SGO-Logic |
|---|---|
| CUN-01 Gestionar venta omnicanal | Completo. SGO-Logic es el sistema de registro de la orden. |
| CUN-02 Administrar inventario unificado | Completo. |
| CUN-03 Gestionar cobro | Orquestación y registro propios; la ejecución del cobro con tarjeta permanece en PayKlik. |
| CUN-04 Emitir documentación fiscal | Generación y certificación propias; el registro contable permanece en el ERP. |
| CUN-05 Ejecutar despacho y entrega | Decisión y seguimiento propios; el transporte y las guías permanecen en LogiExpress. |
| CUN-06 Atender postventa | Completo. |
| CUN-07 Conciliar y cerrar contablemente | Consolidación, transferencia y conciliación; el cierre contable permanece en el ERP. |
| CUN-08 Administrar catálogo | Fuera del alcance. El Catálogo Comercial es una dependencia externa que el sistema consulta para resolver precios. |
| CUN-09 Comunicar al cliente | Orquestación propia; la entrega efectiva depende del proveedor de notificaciones. |
| CUN-10 Administrar crédito comercial | Perfil, línea, evaluación, cuentas por cobrar, abonos y bloqueos son propios de SGO-Logic. El historial base permanece autoritativo en el ERP y se recibe por Integración ERP. |

---

# 5. Características del sistema priorizadas

## 5.1 Criterios de priorización

Cada característica se evalúa en cuatro criterios con escala Alto (3), Medio (2), Bajo (1): valor para el negocio, riesgo de no atenderla, urgencia frente al Black Friday e impacto sobre la arquitectura. Toda característica ligada a pago y facturación hereda urgencia alta por el compromiso de contar con una ruta protegida en el primer mes (EV-43). Ante puntaje igual, precede la característica de la que dependen otras.

## 5.2 Catálogo y priorización

| ID | Característica | Decisión que la soporta | Puntaje | Nivel |
|---|---|---|---|---|
| CAR-03 | Orquestación del ciclo de vida de la orden | ADR-002 | 12 | Backbone |
| CAR-02 | Inventario único con reserva y expiración | ADR-005 | 12 | Backbone |
| CAR-04 | Cobro resiliente y auditable | ADR-009, ADR-010 | 12 | Backbone |
| CAR-06 | Registro inmutable de auditoría | ADR-006 | 12 | Backbone |
| CAR-05 | Facturación electrónica asíncrona | ADR-011 | 11 | Backbone |
| CAR-01 | Identidad de cliente y carrito unificado | ADR-001, ADR-004 | 11 | Backbone |
| CAR-10 | Propagación confiable de eventos a POS | ADR-003, ADR-012 | 10 | Backbone |
| CAR-07 | Consulta de estado y vista 360 | ADR-004 | 9 | Alto |
| CAR-08 | Origen de despacho y seguimiento | ADR-012 | 9 | Alto |
| CAR-12 | Observabilidad de sistemas externos | Prometheus + Grafana | 9 | Alto |
| CAR-09 | Sincronización batch con el ERP | ADR-008 | 8 | Medio |
| CAR-13 | Postventa | ADR-002 | 7 | Medio |
| CAR-11 | Catálogo, precios y promociones (externo) | Dependencia externa | 7 | Medio |
| CAR-14 | Notificaciones al cliente | Coreografía por eventos | 5 | Bajo |
| CAR-15 | Crédito comercial con perfil precalculado | ADR-023 | 11 | Alto |

## 5.3 Secuencia del backbone

**Ola 1 — Núcleo transaccional:** CAR-06, CAR-02, CAR-03, CAR-04, CAR-05. Esta ola constituye la ruta protegida de pagos y facturación exigida para el primer mes.

**Ola 2 — Habilitadores omnicanal:** CAR-01, CAR-10, CAR-07.

**Ola 3 — Operación extendida y visibilidad:** CAR-08, CAR-12, CAR-09.

**Ola 4 — Crédito y complementarias:** CAR-15, CAR-13, CAR-11, CAR-14.

## 5.4 Justificación de prioridades no evidentes

El registro de auditoría (CAR-06) encabeza el backbone porque el veto de Finanzas y las auditorías fiscales hacen inaceptable una operación sin registro desde la primera transacción; la bitácora debe existir antes que las operaciones que registra. La facturación (CAR-05) conserva urgencia alta pese a su certificación asíncrona, porque el desacople es una decisión de diseño y la prioridad es un compromiso comercial. El carrito (CAR-01) no encabeza la lista pese a ser la cara visible del omnicanal, porque el riesgo de no tenerlo es de experiencia, mientras que las características que lo preceden evitan pérdidas cuantificadas. Las notificaciones (CAR-14) cierran la secuencia porque su falla no debe detener la operación.

---

# 6. Drivers de requisitos funcionales y casos de uso expandidos

## 6.1 Contextos delimitados

Cada contexto es dueño exclusivo de sus datos; ningún contexto accede a la base de otro. Cuando un contexto necesita información o una acción de otro, la solicita mediante un evento o una llamada explícita.

| Contexto | Responsabilidad | Es dueño de |
|---|---|---|
| Borde / API Gateway | Punto único de entrada, autenticación y ruteo. | Enrutamiento y validación de acceso. |
| Auth | Identidad y sesión. | Credenciales, tokens, roles. |
| Órdenes | Ciclo de vida de la orden; orquestador de la saga. | Orden, estados y transiciones. |
| Inventario | Existencias unificadas, reservas y expiración. | Existencias por ubicación, reservas. |
| Pagos | Intentos de cobro y su resultado. | Intentos de pago, estado de pago. |
| Facturación | Generación y certificación fiscal. | Documentos fiscales y su estado. |
| Auditoría | Registro inmutable de operaciones. | Bitácora append-only. |
| Consulta de Estado | Modelo de lectura consolidado. | Vista materializada de órdenes. |
| Despacho | Origen del despacho y seguimiento. | Instrucción de despacho, estado de entrega. |
| Integración ERP | Consolidación y envío batch al AS/400. | Lotes de resúmenes de venta. |
| Crédito | Historial comercial precalculado, elegibilidad, línea y cuentas por cobrar a 30 días. | Perfil crediticio, historial importado, línea, cargos, abonos y estados de mora. |
| Integración POS | Envío confiable de eventos a las 32 instancias de POS. | Cola de eventos hacia POS. |
| Notificaciones | Envío de avisos al cliente. | Solicitudes de notificación. |

El **Catálogo Comercial** (productos, precios, promociones, combos y financiamiento) es una dependencia externa. El sistema lo consulta para resolver precios y no administra su contenido.

## 6.2 Casos de uso expandidos

| ID | Caso de uso | Contexto | Actor primario |
|---|---|---|---|
| CU-01 | Reservar inventario para una orden | Inventario | Órdenes (sistema) |
| CU-02 | Confirmar orden | Órdenes | Cliente |
| CU-03 | Procesar cobro con preservación de la orden | Pagos | Órdenes (sistema) |
| CU-04 | Certificar documento fiscal ante la SAT | Facturación | Órdenes (sistema) |
| CU-05 | Registrar evento de auditoría | Auditoría | Cualquier contexto (sistema) |
| CU-06 | Liberar reserva vencida | Inventario | Temporizador (sistema) |
| CU-07 | Reconocer cliente y mantener carrito | Órdenes / Auth | Cliente |
| CU-08 | Publicar eventos pendientes (Outbox) | Contextos de escritura | Publicador (sistema) |
| CU-09 | Propagar evento de venta al POS | Integración POS | Órdenes (sistema) |
| CU-10 | Consultar estado de una orden | Consulta de Estado | Cliente / Agente |
| CU-11 | Determinar origen de despacho y solicitar guía | Despacho | Órdenes (sistema) |
| CU-12 | Actualizar estado de entrega | Despacho | LogiExpress |
| CU-13 | Consolidar y transferir ventas al ERP | Integración ERP | Temporizador (sistema) |
| CU-14 | Registrar devolución o cambio | Órdenes | Agente |
| CU-15 | Notificar cambio de estado de orden | Notificaciones | Órdenes (sistema) |
| CU-16 | Importar historial comercial del ERP | Integración ERP | Temporizador / Operador de Finanzas |
| CU-17 | Determinar elegibilidad y ofrecer crédito | Crédito / Web | Cliente |
| CU-18 | Evaluar crédito dentro de la saga | Crédito | Órdenes (sistema) |
| CU-19 | Consultar y pagar cuenta a 30 días | Crédito / Pagos | Cliente |

## 6.3 Descripción textual de los casos del núcleo

### CU-01 Reservar inventario para una orden

- **Objetivo:** comprometer las unidades solicitadas garantizando que una misma unidad no se asigne a dos clientes.
- **Precondición:** existe una orden en construcción con productos y una ubicación candidata.
- **Flujo principal:** el sistema recibe la solicitud con productos, cantidades y la versión conocida del registro de existencias; verifica que la existencia cubre la cantidad; aplica la reserva incrementando la versión del registro; crea una reserva con marca de expiración; devuelve la reserva confirmada.
- **Flujo alternativo — conflicto de versión:** si la versión ya cambió, el sistema rechaza la operación e indica que debe reintentarse con la existencia actualizada.
- **Flujo alternativo — existencia insuficiente:** el sistema responde sin reservar e indica la cantidad disponible.
- **Poscondición:** las unidades quedan comprometidas con una expiración definida, o no se compromete nada y se informa el motivo.
- **Interacciones:** recibe la solicitud de Órdenes; emite un evento de reserva que consumen Auditoría y Consulta de Estado.

### CU-02 Confirmar orden

- **Objetivo:** cerrar la intención de compra y coordinar cobro, facturación y despacho con compensación ante fallas.
- **Precondición:** el cliente tiene un carrito con productos y un medio de pago seleccionado.
- **Flujo principal:** Órdenes crea la orden y registra el evento en Auditoría; solicita la reserva (CU-01); con la reserva confirmada solicita el cobro (CU-03); con el cobro aceptado pasa la orden a "Confirmada - Pendiente Fiscal" y solicita la certificación (CU-04) de forma asíncrona; solicita el despacho (CU-11); cada transición queda registrada y publicada.
- **Flujo alternativo — falla la reserva:** la orden no avanza; permite reintento o cambio de ubicación, sin haber cobrado.
- **Flujo alternativo — falla el cobro:** la saga libera la reserva y deja la orden en "Pago Pendiente". La orden no se pierde.
- **Flujo alternativo — falla la certificación:** la orden ya está confirmada para el cliente; la certificación se reintenta en segundo plano.
- **Poscondición:** la orden queda confirmada y en curso, o preservada en un estado intermedio con su reserva liberada según corresponda.
- **Interacciones:** orquesta Inventario, Pagos, Facturación y Despacho mediante solicitudes explícitas; registra en Auditoría; publica eventos para Consulta de Estado, Integración POS y Notificaciones.

### CU-03 Procesar cobro con preservación de la orden

- **Objetivo:** ejecutar el cobro dejando constancia de cada intento y sin perder la orden si el proveedor falla.
- **Precondición:** existe una orden con monto y medio de pago. No se almacenan datos completos de tarjeta.
- **Flujo principal:** Pagos registra un intento con orden, canal, fecha y estado; invoca a la pasarela; ante respuesta positiva marca el intento como exitoso y responde a Órdenes; el intento queda registrado en Auditoría.
- **Flujo alternativo — el proveedor falla:** el intento se marca como fallido; la orden pasa a "Pago Pendiente" y se genera un enlace de reintento que se envía al cliente por correo.
- **Flujo alternativo — reintento del cliente:** genera un nuevo intento ligado a la misma orden, conservando el historial.
- **Poscondición:** el cobro queda aplicado y registrado, o la orden queda en "Pago Pendiente" con todos los intentos auditados.
- **Interacciones:** responde a Órdenes; solicita el correo de reintento a Notificaciones; registra en Auditoría.

### CU-04 Certificar documento fiscal ante la SAT

- **Objetivo:** generar el documento fiscal y obtener su certificación sin bloquear la confirmación de la orden.
- **Precondición:** existe una orden confirmada con los datos fiscales necesarios.
- **Flujo principal:** Facturación genera el documento conservando identificador tributario y serie; lo encola hacia la SAT por el canal de mensajería dedicado; al recibir la certificación, marca el documento como certificado y notifica a Órdenes que la orden puede cerrarse fiscalmente; registra el resultado.
- **Flujo alternativo — la SAT tarda o no responde:** el documento permanece en cola con reintentos; la orden se mantiene en "Confirmada - Pendiente Fiscal" sin que el cliente perciba espera.
- **Flujo alternativo — devolución o reembolso:** genera el documento de reversión conservando la relación con la venta original.
- **Poscondición:** el documento queda certificado y ligado a la orden, o en reintento controlado.
- **Interacciones:** responde a Órdenes; se comunica con la SAT por el canal dedicado; registra en Auditoría.

### CU-05 Registrar evento de auditoría

- **Objetivo:** conservar de forma inmutable quién ejecutó cada operación, cuándo y qué cambió, recuperable en menos de un segundo.
- **Flujo principal:** Auditoría recibe el evento con actor, momento, operación y datos del cambio; lo inserta en su base propia, que solo admite inserción; lo indexa por identificador de transacción.
- **Flujo alternativo — consulta de evidencia:** ante una solicitud, recupera por identificador de transacción todos los registros asociados y los devuelve en menos de un segundo.
- **Poscondición:** el evento queda registrado de forma permanente e inalterable.
- **Interacciones:** recibe eventos de todos los contextos de escritura; no escribe en ninguna otra base.

### CU-06 Liberar reserva vencida

- **Objetivo:** devolver al inventario disponible las unidades reservadas por compras que no concluyeron.
- **Flujo principal:** al vencer el tiempo de vida de la reserva, esta se marca como expirada; las unidades vuelven a contarse como disponibles; se registra el evento y se publica.
- **Flujo alternativo — la orden se concretó antes de vencer:** la reserva se consumió y no expira.
- **Poscondición:** el inventario refleja la disponibilidad real sin retener unidades de compras abandonadas.
- **Interacciones:** notifica a Órdenes que la reserva expiró; registra en Auditoría.

### CU-16 Importar historial comercial del ERP

- **Objetivo:** alimentar el perfil de Crédito con el historial comercial autoritativo del ERP sin consultar el AS/400 durante una compra.
- **Precondición:** existe un lote de snapshots disponible en el mock ERP o en el canal batch equivalente del ERP real.
- **Flujo principal:** el temporizador activa Integración ERP; el worker obtiene el lote; la capa anticorrupción valida y traduce cada registro; normaliza el email y conserva código ERP, fecha de corte y lote de origen; persiste la operación y publica `HistorialClienteErpActualizado` mediante Outbox; Crédito consume el evento, guarda el snapshot, recalcula el perfil y publica `PerfilCrediticioActualizado` si cambió el resultado.
- **Flujo alternativo — lote duplicado o corte anterior:** Integración ERP o Crédito ignora el registro sin retroceder el perfil vigente y deja evidencia de la deduplicación.
- **Flujo alternativo — formato inválido o ERP no disponible:** el lote queda pendiente o en dead letter según el contrato de mensajería; no se modifica el perfil anterior ni se bloquea el checkout.
- **Poscondición:** Crédito tiene el último historial válido aplicado o conserva el perfil anterior con una alerta operativa.
- **Interacciones:** mock ERP o ERP real, Integración ERP, Outbox, Crédito y Auditoría.

### CU-17 Determinar elegibilidad y ofrecer crédito

- **Objetivo:** mostrar crédito solo a clientes elegibles, sin recalcular ni llamar al ERP durante checkout.
- **Precondición:** el cliente está autenticado y Crédito tiene un perfil precalculado, o puede determinar que no existe historial.
- **Flujo principal:** Checkout solicita la elegibilidad con el email del claim; Crédito lee el perfil local, valida el monto contra el disponible y devuelve si se ofrece crédito, el límite disponible, el vencimiento estimado y el mensaje para el cliente; Web muestra la opción junto con PayKlik.
- **Flujo alternativo — sin historial, perfil no elegible o cupo insuficiente:** Crédito devuelve una respuesta negativa; Web oculta la opción y mantiene PayKlik.
- **Flujo alternativo — timeout o indisponibilidad de Crédito:** Web aplica el límite de consulta y muestra únicamente PayKlik; la orden no queda bloqueada.
- **Poscondición:** el cliente ve una oferta consistente con el último perfil aplicado, o una ruta de pago alternativa.
- **Interacciones:** Cliente, Web, API Gateway y Crédito. No hay llamada a ERP.

### CU-18 Evaluar crédito dentro de la saga

- **Objetivo:** aprobar o rechazar una orden a crédito después de reservar inventario y emitir una cuenta por cobrar si procede.
- **Precondición:** Órdenes tiene una reserva confirmada, el cliente está autenticado y seleccionó `CREDITO`.
- **Flujo principal:** Órdenes publica `EvaluacionCreditoSolicitada`; Crédito verifica elegibilidad y cupo con bloqueo optimista; si aprueba, aparta el monto de la línea, crea el cargo con vencimiento a 30 días y publica `CreditoAprobado`; Órdenes pasa a `CONFIRMADA_PENDIENTE_FISCAL`, continúa la saga y notifica al cliente.
- **Flujo alternativo — rechazo:** Crédito publica `CreditoRechazado`; Órdenes rechaza la orden, solicita liberar la reserva y notifica el motivo en lenguaje de cliente.
- **Flujo alternativo — timeout:** el worker de Órdenes rechaza la evaluación, libera la reserva y ofrece reintentar con PayKlik; una aprobación tardía se ignora o anula el cargo según la regla de idempotencia.
- **Flujo alternativo — conflicto de cupo:** Crédito relee la línea y reintenta; si el cupo ya no alcanza, rechaza sin crear una cuenta por cobrar.
- **Poscondición:** orden confirmada con cargo pendiente, o orden rechazada con reserva liberada; ambos resultados son auditables y notificables.
- **Interacciones:** Órdenes, Crédito, Inventario, Facturación, Notificaciones y Auditoría.

### CU-19 Consultar y pagar cuenta a 30 días

- **Objetivo:** permitir que el cliente consulte su deuda, vencimiento y disponible, y realice abonos sin almacenar datos sensibles de tarjeta.
- **Precondición:** existe una cuenta por cobrar activa asociada al cliente.
- **Flujo principal:** Cliente abre Mi crédito; Crédito devuelve saldo, vencimiento, línea disponible y cuentas; el cliente solicita un abono con un token de medio de pago; Crédito registra el abono y publica `CobroAbonoCreditoSolicitado`; Pagos responde por evento; Crédito aplica el abono, libera cupo y marca la cuenta pagada cuando corresponde.
- **Flujo alternativo — vencimiento:** un worker marca la cuenta vencida y bloquea la línea hasta ponerse al día.
- **Flujo alternativo — devolución:** Crédito reduce la deuda y libera cupo; si existe excedente ya abonado, solicita el reembolso a Pagos.
- **Poscondición:** saldo y línea reflejan el resultado final o el abono queda pendiente/rechazado para reintento.
- **Interacciones:** Cliente, Crédito, Pagos, PayKlik y Notificaciones.

Los casos CU-07 a CU-19 se documentan con la misma estructura y se representan en los diagramas de la sección 11. Los casos CU-16 a CU-19 son la extensión de Fase 3 para Crédito; sus diagramas UML quedan como pendiente de elaboración si se exige una vista por caso.

---

# 7. Drivers de calidad: escenarios de atributos de calidad

Cada escenario se expresa en formato de seis partes: fuente del estímulo, estímulo, ambiente, artefacto, respuesta y medida de respuesta.

| ID | Atributo | Enunciado | Prioridad |
|---|---|---|---|
| EAC-01 | Disponibilidad | El sistema no pierde la orden si PayKlik falla durante el pico de Black Friday; cada intento fallido queda auditado. | Alta |
| EAC-02 | Rendimiento (consulta) | Consultar el estado de una orden responde en menos de 5 segundos. | Alta |
| EAC-03 | Rendimiento (auditoría) | Recuperar la evidencia completa de una transacción toma menos de 1 segundo. | Alta |
| EAC-04 | Escalabilidad | El sistema escala ante el crecimiento sostenido del canal digital y ante picos estacionales, sin rediseño. | Alta |
| EAC-05 | Consistencia de inventario | Ninguna unidad se compromete a más de un cliente, incluso con reservas concurrentes. | Alta |
| EAC-06 | Rendimiento y resiliencia de crédito | La elegibilidad se consulta desde el perfil local sin llamar al ERP; una falla o timeout de Crédito no bloquea el checkout. | Alta |

**EAC-01 — Disponibilidad ante falla del proveedor de pago.** Fuente: PayKlik. Estímulo: indisponibilidad o falla al procesar un cobro. Ambiente: producción durante Black Friday con demanda de hasta ocho veces la normal. Artefacto: servicio de Pagos y su ruta de reintento. Respuesta: la orden no se pierde; el intento se registra antes de invocar a PayKlik; si falla, el evento se reintenta de forma asíncrona sin bloquear el flujo; cada intento se audita. Medida: cero órdenes perdidas por indisponibilidad del proveedor; 100% de intentos fallidos auditados; disponibilidad del servicio de Pagos igual o superior a 99.9% durante el evento.

**EAC-02 — Rendimiento en consulta de estado.** Fuente: cliente o personal de atención desde cualquier dispositivo. Estímulo: solicitud de consulta del estado de una orden. Ambiente: operación normal y en pico. Artefacto: servicio de Consulta de Estado. Respuesta: retorna el estado desde su modelo de lectura, sin orquestar llamadas en tiempo real a sistemas externos. Medida: tiempo de respuesta inferior a 5 segundos para el 100% de las consultas, incluso bajo carga pico.

**EAC-03 — Rendimiento en recuperación de evidencia.** Fuente: Finanzas o auditoría de la SAT. Estímulo: solicitud de reconstrucción de una transacción. Ambiente: cualquier momento posterior a la venta. Artefacto: servicio de Auditoría. Respuesta: recupera la evidencia desde su propia base, sin competir con la carga transaccional. Medida: recuperación en menos de 1 segundo; cero transacciones sin respaldo.

**EAC-04 — Escalabilidad.** Fuente: volumen de usuarios y transacciones del canal digital. Estímulo: crecimiento sostenido cercano al 15% anual y pico estacional de hasta ocho veces. Ambiente: producción en condiciones normales con tendencia de crecimiento y en la ventana de alta demanda. Artefacto: servicios del backbone en el clúster de contenedores. Respuesta: los servicios escalan horizontalmente de forma independiente según su propio patrón de carga, sin rediseño ni migración forzada. Medida: sostiene una demanda de ocho veces sin degradar EAC-01, EAC-02 ni EAC-03; la capacidad base se incrementa año a año dentro del techo operativo sin cambios arquitectónicos.

**EAC-05 — Consistencia de inventario.** Fuente: múltiples clientes sobre el mismo producto. Estímulo: dos o más reservas concurrentes sobre la misma unidad. Ambiente: operación normal y en pico. Artefacto: servicio de Inventario con bloqueo optimista y expiración. Respuesta: detecta el conflicto de versión al escribir y rechaza la segunda reserva; libera automáticamente las reservas no concluidas al expirar. Medida: cero unidades comprometidas a más de un cliente; reservas no concluidas liberadas dentro del tiempo de expiración definido.

**EAC-06 — Decisión rápida y resiliente de crédito.** Fuente: cliente en checkout. Estímulo: consulta de elegibilidad para un monto de compra. Ambiente: operación normal, con el ERP fuera de línea o con un perfil cuya antigüedad alcanza la ventana batch. Artefacto: endpoint de elegibilidad de Crédito y perfil precalculado. Respuesta: devuelve la oferta leyendo solo la base local; si no responde dentro del límite configurado, Web oculta crédito y conserva PayKlik. Medida: la consulta local responde en milisegundos en condiciones normales, nunca llama al ERP, y ningún timeout de Crédito impide crear una orden por PayKlik.

---

# 8. Drivers de restricción

A diferencia de un escenario de calidad, una restricción elimina alternativas de diseño sin negociación posible.

| ID | Restricción | Categoría | Decisión que la satisface |
|---|---|---|---|
| RES-01 | No modificar ni reemplazar el ERP-Core (AS/400). | Técnica / gobierno | Capa anticorrupción y worker batch (ADR-007, ADR-008). |
| RES-02 | Integración con el ERP solo por archivo CSV/XML cada 30 minutos. | Técnica | Worker de consolidación (ADR-008). |
| RES-03 | No reemplazar los POS; solo recepción de eventos con reintentos. | Técnica / gobierno | Propagación por eventos idempotentes (ADR-012). |
| RES-04 | PayKlik se integra tal cual, respetando el contrato. | Contractual | Resiliencia del lado de SGO-Logic (ADR-009). |
| RES-05 | LogiExpress se integra tal cual, respetando el contrato. | Contractual | Capa anticorrupción y circuit breaker (ADR-009). |
| RES-06 | La falla del proveedor de notificaciones no bloquea el flujo. | Técnica | Notificaciones desacopladas por eventos. |
| RES-07 | Facturación fiscal por XML a la SAT antes de finalizar la orden. | Regulatoria | Canal dedicado y estado Pendiente Fiscal (ADR-011). |
| RES-08 | No se almacenan datos completos de tarjeta ni códigos de seguridad. | Regulatoria / seguridad | Tokenización delegada (ADR-010). |
| RES-09 | Cumplimiento de protección de datos y normativa fiscal en tres países. | Regulatoria | Tratamiento de datos por país. |
| RES-10 | Inversión inicial total de USD 150,000 para todo el proyecto. | Económica | Presupuesto declarado (sección 9). |
| RES-11 | Costo operativo mensual igual o inferior a USD 1,200. | Económica | Stack open source on-premises (sección 9). |
| RES-12 | El equipo domina Java, .NET y Docker básico. | Organizacional | Capacitación presupuestada; número acotado de tecnologías. |
| RES-13 | Minimizar el consumo energético; facturación totalmente electrónica. | Sostenibilidad | Escalado bajo demanda en vez de capacidad fija. |
| RES-14 | Despliegue inicial on-premises. | Infraestructura | Kubernetes y almacenes de datos on-premises. |
| RES-15 | Migración a la nube posible sin rediseño. | Infraestructura | Empaquetado en contenedores portable. |
| RES-16 | El perfil de Crédito debe actualizarse desde el historial comercial del ERP, no desde una consulta síncrona en checkout. | Funcional / técnica | Snapshot batch, ACL y evento `HistorialClienteErpActualizado` (ADR-023). |
| RES-17 | La alternativa demostrable debe ejecutarse sin acceso al AS/400 real y permitir fallas reproducibles. | Operativa / pruebas | `mock-erp` ampliado en contenedor con fixtures y administración de lotes (ADR-019, ADR-023). |

**Nota sobre RES-11.** El monto operativo mensual exacto depende del dimensionamiento del hardware para el pico de Black Friday. Si el escalamiento superara USD 1,200 mensuales de forma sostenida, requiere aprobación adicional.

---

# 9. Restricciones de infraestructura

## 9.1 Presupuesto

La inversión inicial de USD 150,000 cubre análisis, desarrollo e infraestructura de puesta en marcha; es un fondo compartido para todo el proyecto, no un techo por componente. El costo operativo mensual no debe superar USD 1,200 sin aprobación adicional; cubre la infraestructura propia de SGO-Logic y no incluye las tarifas contractuales de PayKlik ni de LogiExpress.

Las tecnologías seleccionadas —Kubernetes, RabbitMQ, Redis y los motores SQL— son software libre sin costo de licenciamiento, por lo que el gasto operativo se concentra en cómputo y almacenamiento, cubiertos por el hardware on-premises existente. El único costo adicional identificado es la capacitación en Kubernetes (USD 1,500 a 4,000), cargada a la inversión inicial y no al techo operativo.

| Concepto | Tipo | Monto |
|---|---|---|
| Capacitación en Kubernetes | Inversión inicial | USD 1,500 – 4,000 |
| Infraestructura de cómputo y almacenamiento | Operativo mensual | Dentro del techo de USD 1,200, reutilizando hardware existente |
| Licenciamiento de software | — | USD 0 (tecnologías open source) |

## 9.2 Despliegue on-premises y migración a la nube

El despliegue inicial es on-premises sobre la infraestructura existente. Todos los servicios corren empaquetados como contenedores en Kubernetes, lo que garantiza la portabilidad: el mismo empaquetado puede moverse a un clúster administrado en la nube sin rediseño. Redis dispone de una ruta de migración definida hacia un servicio administrado equivalente; las instancias SQL seguirían el mismo principio, manteniendo los límites de esquema por servicio; RabbitMQ es portable sin cambios en los servicios que lo consumen.

Presupuesto y despliegue on-premises se refuerzan mutuamente: el techo mensual es alcanzable porque el despliegue inicial reutiliza infraestructura pagada y tecnologías sin licencia, en vez de contratar servicios administrados por demanda.

**Nota de la Fase 2.** Kubernetes on-premises se mantiene como plataforma de producción. El entorno de ejecución del MVP es Docker Compose (ADR-013); su topología y el mapa de servicios se describen en las secciones 14.1 y 14.2.

---

# 10. Decisiones de arquitectura (ADR)

Cada decisión se documenta con su contexto, la decisión adoptada, las alternativas consideradas y sus consecuencias.

## ADR-001 — Estilo de microservicios por contexto delimitado

**Estado:** Aceptada.

**Contexto.** El sistema integra identidad, carrito, inventario, cobro, facturación, despacho, auditoría, notificaciones e integraciones con sistemas legacy y externos, cada uno con reglas, ritmo de cambio y escalado distintos. La operación actual está fragmentada en silos (EV-07). El equipo domina Java, .NET y Docker básico (EV-46), opera dentro de un techo de USD 1,200 mensuales (EV-42) y debe sostener picos de hasta ocho veces la demanda (EV-27) sin que la falla de una parte arrastre al resto.

**Decisión.** Se adopta un estilo de microservicios por contexto delimitado: Auth, Órdenes, Inventario, Pagos, Facturación, Despacho, Auditoría, Consulta de Estado, Integración ERP, Integración POS y Notificaciones. Cada contexto es dueño exclusivo de sus datos; la comunicación ocurre mediante eventos o llamadas explícitas, nunca por acceso compartido a esquema.

**Alternativas consideradas.** Se evaluó un monolito modular en un solo contenedor, con separación lógica por módulos pero una sola base de datos y un solo despliegue: de menor complejidad operativa y alineado con el nivel de Docker del equipo, pero incapaz de escalar de forma independiente los componentes del backbone durante el pico, y con un fallo de módulo que compromete todo el proceso. Se descartó porque el aislamiento de fallas (PRE-01, PRE-02) y el escalado selectivo pesaron más que el ahorro de complejidad. También se consideró un monolito con extracción posterior a microservicios, descartado por implicar rehacer el trabajo en desarrollo.

**Consecuencias.** Positivas: cada servicio escala de forma independiente; el aislamiento de fallas evita el efecto dominó; se reparte la responsabilidad entre integrantes sin choques; resuelve el diagnóstico de silos. Negativas: mayor complejidad operativa —múltiples servicios, bases y colas—, mitigada con capacitación presupuestada; la consistencia entre contextos pasa a ser eventual y exige coordinación explícita (ADR-002); aumenta la superficie a observar (CAR-12).

**Nota de la Fase 2.** Kubernetes on-premises sigue siendo la plataforma de producción. El entorno de ejecución del MVP es Docker Compose, según ADR-013 (sección 14.1).

## ADR-002 — Saga orquestada centralizada para el ciclo de vida de la orden

**Estado:** Aceptada.

**Contexto.** Confirmar una orden coordina reserva, cobro, certificación fiscal y despacho, pasos de contextos distintos que pueden fallar de forma independiente. Se exige no perder la orden ante fallas del proveedor de pago y auditar cada intento (EV-28); el método de entrega puede cambiar tras confirmar (EV-31), por lo que la orden es un agregado de larga vida y mutable.

**Decisión.** Órdenes actúa como orquestador centralizado de una saga (máquina de estados) que coordina las llamadas a Inventario, Pagos, Facturación y Despacho, decide las compensaciones ante fallas y es el único contexto que conoce la secuencia completa; el resto solo conoce su propio paso.

**Alternativas consideradas.** Cinco enfoques: (1) saga orquestada centralizada con máquina de estados interna; (2) coreografía pura por eventos, descartada porque impide el rastreo centralizado que exige Finanzas; (3) orquestación basada en base de datos, que aprovecha la SQL existente pero exige lógica de control propia; (4) orquestador con herramienta dedicada, descartado por exceder las competencias del equipo; (5) saga híbrida. Se eligió la saga orquestada centralizada por dar control total del estado, facilitar compensaciones y dejar registro exacto de cada paso, sin herramientas fuera del alcance del equipo. Se incorpora el matiz de la opción 5: las notificaciones se resuelven por coreografía, al ser un servicio secundario cuya falla no debe frenar la operación.

**Consecuencias.** Positivas: concentra la lógica de compensación y el estado de la orden; permite exponer estados intermedios explícitos en vez de bloquear; da un punto único de instrumentación para auditar. Negativas: Órdenes se vuelve un punto de alto acoplamiento lógico, crítico para todo el flujo; la máquina de estados exige prueba exhaustiva conforme crecen los flujos alternativos.

**Nota de la Fase 2.** El mecanismo con el que Órdenes se entera del resultado de cada paso y avanza la saga sin transiciones manuales se precisa en ADR-016 (sección 14.5).

## ADR-003 — Patrón Outbox con polling publisher para publicación confiable de eventos

**Estado:** Aceptada.

**Contexto.** Cada contexto de escritura publica eventos hacia el bus para que otros los consuman, sin perderlos si el bus está temporalmente inaccesible ni duplicarlos. El POS debe recibir eventos con manejo de errores y reintentos (EV-16) y no puede perderse una orden por una falla de publicación (EV-28).

**Decisión.** Cada contexto de escritura persiste el evento pendiente en una tabla outbox dentro de su propia base, en la misma transacción que el cambio de datos. Un publicador dedicado consulta periódicamente esas tablas y publica los eventos en el bus, marcándolos como publicados solo tras la confirmación del broker.

**Alternativas consideradas.** Cinco opciones: (1) Outbox con polling publisher; (2) Outbox con Change Data Capture, más eficiente pero de alta complejidad fuera del alcance del equipo; (3) envío directo sin Outbox, descartado por garantizar pérdida de eventos; (4) two-phase commit, descartado por su impacto en el rendimiento durante el pico; (5) Outbox con notificación nativa del motor, más eficiente pero que acopla el código a un motor específico. Se eligió el polling publisher por ser el más sencillo sobre la base SQL existente, alineado con la experiencia del equipo y sin costo adicional. Se asume la necesidad de optimizar el intervalo de consulta.

**Consecuencias.** Positivas: ningún evento se pierde aunque el bus no esté disponible en el instante de la escritura; evita el problema de escritura dual. Negativas: introduce latencia entre el cambio y la publicación, según el intervalo de polling; el publicador es un componente adicional que operar; exige disciplina para no publicar por otra vía.

**Nota de la Fase 2.** El contrato común de mensajería sobre el que opera el Outbox (envelope, topología del broker, reintentos y bibliotecas compartidas) se registra en ADR-018. El intervalo de polling del publicador es de 2 segundos por defecto (sección 14.6).

## ADR-004 — CQRS ligero con Redis como modelo de lectura para consulta de estado

**Estado:** Aceptada.

**Contexto.** La consulta de estado de una orden abarca varios dominios y no puede superar los cinco segundos (EV-33). No puede depender de orquestar en tiempo real llamadas a los contextos de escritura ni a sistemas externos, especialmente durante picos (EV-27).

**Decisión.** Se separa un contexto de Consulta de Estado, distinto de Órdenes, que mantiene un modelo de lectura materializado en Redis, actualizado de forma asíncrona al consumir los eventos que publican los contextos de escritura.

**Alternativas consideradas.** Cinco opciones: (1) Redis materializado; (2) base SQL de solo lectura, con mayor latencia en picos; (3) NoSQL documental, ideal para la vista consolidada pero que introduce otro motor; (4) consultas directas por fan-out, descartadas por incumplir los cinco segundos si una dependencia está lenta; (5) motor de búsqueda, descartado por su alto consumo de recursos frente al techo mensual. Se eligió Redis materializado por reutilizar un motor ya adoptado, responder en el orden de milisegundos y mantener el costo al mínimo. Se asume la necesidad de una estrategia de recarga ante la caída de un nodo.

**Consecuencias.** Positivas: la consulta no depende de la disponibilidad de los contextos de escritura ni de los externos; escala de forma independiente. Negativas: el modelo es eventualmente consistente, con una ventana breve entre el cambio real y su reflejo; introduce un componente de datos adicional.

**Nota de la Fase 2.** La decisión de este ADR se mantiene. En el MVP, Consulta de Estado conserva la vista de cada orden en Redis y, además, en PostgreSQL como respaldo durable y fuente del historial; ver sección 14.6.

## ADR-005 — Bloqueo optimista con columna de versión y expiración por TTL para reserva de inventario

**Estado:** Aceptada.

**Contexto.** Antes de confirmar una venta debe verificarse la disponibilidad para no comprometer una unidad a más de un cliente (EV-22), único punto que exige consistencia fuerte. Las reservas no concluidas se liberan en un tiempo acorde con la operación (EV-23). El mecanismo se sostiene bajo picos de hasta ocho veces la demanda (EV-27).

**Decisión.** La reserva usa bloqueo optimista mediante una columna de versión sobre el registro de existencias: el conflicto se detecta al escribir, sin bloquear el registro por adelantado. Cada reserva se crea con una marca de expiración gestionada junto a Redis, y un proceso automático libera las reservas vencidas.

**Alternativas consideradas.** Cinco opciones: (1) bloqueo pesimista con barredor programado, de consistencia absoluta pero con riesgo de contención en el pico; (2) bloqueo optimista con versión más expiración; (3) inventario exclusivamente en memoria, descartado por el riesgo de pérdida de datos exactos; (4) saga con compensación sin bloqueo previo, descartada porque permite sobreventas; (5) procedimientos almacenados de limpieza, descartados por dificultar la portabilidad. Se eligió el bloqueo optimista con versión y expiración para privilegiar el rendimiento de escritura durante los picos, aceptando manejar reintentos por conflicto. El bloqueo pesimista se descartó como opción principal por el riesgo de contención bajo el pico, aunque ofrece una garantía de consistencia superior.

**Consecuencias.** Positivas: no requiere bloqueos de larga duración, lo que favorece el throughput en picos; la expiración automática evita reservas huérfanas. Negativas: bajo alta contención puede generar reintentos frecuentes por conflicto, que Órdenes maneja explícitamente; la expiración depende de la disponibilidad de Redis o del temporizador.

## ADR-006 — Almacén de auditoría append-only en base separada

**Estado:** Aceptada.

**Contexto.** Finanzas exige reconstruir quién ejecutó cada operación, cuándo y qué se modificó, con evidencia recuperable en menos de un segundo, ante las auditorías de la SAT (EV-30), y tiene veto sobre decisiones que reduzcan la trazabilidad (EV-38). Ese registro no puede competir por rendimiento con la carga transaccional ni ser alterable.

**Decisión.** Auditoría es un contexto con base de datos propia, en instancia separada, con permisos restringidos a solo inserción, indexada por identificador de transacción y alimentada de forma asíncrona vía eventos.

**Alternativas consideradas.** Se evaluó mantener la bitácora como una tabla dentro de la instancia de Finanzas con permisos de solo inserción: una base menos que operar, pero sus lecturas competirían por recursos con la carga transaccional durante los picos, arriesgando el presupuesto de recuperación. Se consideró también una herramienta de auditoría especializada, descartada por costo y por exceder las competencias del equipo. Se eligió una base propia y separada de solo inserción, que garantiza la inmutabilidad a nivel de motor sin tecnología adicional y aísla las lecturas de la carga transaccional.

**Consecuencias.** Positivas: la inmutabilidad se garantiza por permisos, sin tecnología especializada; al no compartir instancia, sus lecturas no compiten con la carga transaccional. Negativas: es una base adicional que operar; al alimentarse de forma asíncrona existe una ventana breve antes de la indexación, mitigada porque el evento se registra en la transacción de origen (ADR-003).

## ADR-007 — Capa anticorrupción para integrar el ERP AS/400 sin modificarlo

**Estado:** Aceptada.

**Contexto.** El ERP es la plataforma contable-financiera sobre AS/400 (EV-13), no se reemplaza ni modifica (EV-14, EV-40), y el Gerente de TI tiene poder de rechazo sobre cualquier propuesta que lo module (EV-37). Su modelo de datos y semántica no deben contaminar el dominio nuevo.

**Decisión.** Se introduce una capa anticorrupción dedicada a la integración con el ERP, que traduce los lotes de ventas y los snapshots de historial comercial entre el modelo legado y los contratos de SGO-Logic, sin que el ERP conozca ni participe del modelo de dominio nuevo.

**Alternativas consideradas.** Cuatro opciones: (1) capa anticorrupción dedicada; (2) que cada contexto genere directamente el formato del ERP, descartada porque esparce la lógica del legacy y acopla el sistema al AS/400; (3) modificar el ERP, descartada por la restricción de la Junta; (4) una base intermedia compartida, descartada por crear acoplamiento de datos y violar la propiedad exclusiva de datos por contexto. Se eligió la capa anticorrupción dedicada porque concentra la traducción hacia el legacy en un único punto y absorbe cambios futuros del ERP sin propagarlos.

**Consecuencias.** Positivas: el dominio nuevo queda protegido de la semántica del legacy; cualquier cambio futuro del ERP se absorbe en un solo punto. Negativas: es un componente adicional que mantener; el ERP sigue siendo una dependencia dura del cierre contable.

## ADR-008 — Integración batch CSV/XML cada 30 minutos con el ERP

**Estado:** Aceptada.

**Contexto.** El ERP se integra por archivos CSV o XML cada 30 minutos (EV-15), sin vía síncrona, y no se modifica (RES-01, ADR-007). El mismo mecanismo batch alimenta los resúmenes de ventas y el snapshot de historial comercial de Crédito. Hoy Finanzas concilia manualmente con hojas de cálculo (EV-10).

**Decisión.** Un worker del contexto de Integración ERP consolida los eventos de venta confirmados y procesa snapshots de historial cada 30 minutos, usando lotes CSV/XML y la ACL. Publica `HistorialClienteErpActualizado` por Outbox para Crédito. Además del control por lote —número de secuencia, fecha de corte y totales de control—, un proceso de reconciliación diario compara ventas, historiales y perfiles aplicados contra los lotes aceptados y genera un reporte de diferencias para Finanzas.

**Alternativas consideradas.** Tres opciones: (1) lotes CSV/XML cada 30 minutos por un worker dedicado para ventas e historial; (2) integración en tiempo real vía API, descartada porque el ERP no expone interfaz síncrona y no se modifica; (3) exportación manual como hoy, descartada por ser el problema que el proyecto elimina. Se eligió el lote automático por ser el único mecanismo compatible con el ERP y por automatizar la consolidación y la alimentación del perfil de Crédito.

**Consecuencias.** Positivas: respeta el único mecanismo disponible sin modificar el ERP; automatiza la consolidación manual y permite responder elegibilidad localmente. Negativas: la consistencia contable y la frescura del historial son eventuales, con ventana de hasta 30 minutos; el reporte de reconciliación diario añade un proceso adicional que operar y revisar.

## ADR-009 — Circuit breaker y reintentos para PayKlik y LogiExpress

**Estado:** Aceptada.

**Contexto.** PayKlik tiene latencia variable y estuvo indisponible 45 minutos en el último Black Friday, con pérdidas de USD 150,000 (EV-11, EV-17); LogiExpress falla por timeout cerca de una de cada 50 solicitudes (EV-18). Ambos son contractuales y no se reemplazan (RES-04, RES-05), y ninguna falla suya puede bloquear el flujo ni perder la orden.

**Decisión.** Los adaptadores hacia PayKlik y LogiExpress aplican reintentos con backoff y cola de mensajes fallidos sobre el bus; para LogiExpress se añade una capa anticorrupción con circuit breaker.

**Alternativas consideradas.** Cuatro opciones: (1) reintentos con backoff más circuit breaker y cola de fallidos; (2) reintentos simples sin circuit breaker, descartada porque seguir golpeando un proveedor caído agrava la saturación; (3) llamada síncrona directa sin protección, descartada porque una caída bloquearía el flujo, como ocurrió en el incidente de los USD 150,000; (4) reemplazar el proveedor, descartada porque los contratos son vigentes y no negociables. Se eligió el esquema de reintentos con backoff, cola de fallidos y circuit breaker por aislar al sistema de las fallas del proveedor sin bloquear la orden y dejar cada intento auditado.

**Consecuencias.** Positivas: una falla del proveedor no bloquea la orden ni pierde la compra; cada reintento queda auditado. Negativas: el cliente puede percibir estados intermedios más largos durante un incidente prolongado; el circuit breaker requiere calibrar sus umbrales de apertura y cierre —tasa de error, ventana de observación y tiempo de reintento— contra el patrón real de fallas de cada proveedor. Estos valores se definirán durante las pruebas de carga que simulan el pico de ocho veces, el mismo ejercicio en el que se fijan las réplicas mínima y máxima del servicio de Pagos, para no fijar umbrales sin datos de comportamiento real.

**Nota de la Fase 2.** Pagos implementa el disyuntor hacia PayKlik. En Despacho, la solicitud de guía a LogiExpress se ejecuta sin circuit breaker y degrada a GUIA_PENDIENTE; esa excepción, su justificación y el riesgo que deja se registran en ADR-017. Los mocks con los que se provocan las fallas de ambos proveedores se describen en ADR-019.

## ADR-010 — Tokenización y no almacenamiento de datos de tarjeta

**Estado:** Aceptada.

**Contexto.** Por cumplimiento, no se almacenan números completos de tarjeta ni códigos de seguridad (EV-25). A la vez, se busca no perder la orden ante fallas y auditar cada intento (EV-28), lo que no autoriza construir un procesador de tarjetas propio.

**Decisión.** SGO-Logic delega la tokenización a PayKlik; el contexto de Pagos almacena solo el resultado y los metadatos del intento —orden, canal, fecha y resultado—, nunca el número de tarjeta ni el código de seguridad.

**Alternativas consideradas.** Tres opciones: (1) delegar la tokenización al proveedor y almacenar solo token y metadatos; (2) almacenar los datos de tarjeta cifrados en una bóveda propia, descartada porque construir y certificar un almacén de datos de tarjeta propio excede el alcance, el presupuesto y las competencias, y choca con la prohibición corporativa; (3) no almacenar ninguna referencia de pago, descartada porque impediría reintentar un cobro o auditar los intentos. Se eligió delegar la tokenización y guardar solo el token y los metadatos, lo que elimina el alcance de cumplimiento de datos sensibles y permite auditar cada intento.

**Consecuencias.** Positivas: elimina el alcance de cumplimiento de un procesador propio; resuelve el conflicto entre independencia del proveedor y prohibición de almacenar datos sensibles (CON-03). Negativas: la resiliencia de pagos queda limitada a rutas alternativas dentro del mismo proveedor, no a alternar libremente entre procesadores; el nivel real de independencia frente a PayKlik queda acotado.

## ADR-011 — Certificación fiscal asíncrona ante la SAT

**Estado:** Aceptada.

**Contexto.** Toda orden requiere una factura certificada por la SAT antes de finalizar (EV-19), pero el servicio de la SAT tiene tiempos de respuesta variables. Bloquear la confirmación hasta obtener la certificación contradice la expectativa de una experiencia fluida (PRE-13, PRE-14).

**Decisión.** La certificación se ejecuta de forma asíncrona: la orden pasa a "Confirmada - Pendiente Fiscal" en cuanto se acepta el cobro, mientras el documento se encola por el canal dedicado y se reintenta con backoff hasta certificarse, momento en el que la orden pasa a confirmada fiscalmente.

**Alternativas consideradas.** Cinco opciones: (1) certificación asíncrona con estado intermedio; (2) certificación síncrona bloqueante, descartada porque la latencia impredecible de la SAT destruiría la experiencia y la resiliencia; (3) híbrido con intento síncrono de timeout corto y respaldo asíncrono, descartado por añadir complejidad sin beneficio claro; (4) certificación en lotes al final del día, descartada por posible incumplimiento de la facturación en línea; (5) delegar la certificación al ERP, descartada porque el ERP no se modifica. Se eligió la certificación asíncrona por permitir confirmar la compra en milisegundos mientras el documento se certifica, cumpliendo la obligación de forma diferida pero garantizada.

**Consecuencias.** Positivas: el cliente recibe confirmación sin esperar a la SAT; la obligación fiscal se cumple siempre, de forma diferida y visible por un estado intermedio. Negativas: existe una ventana en la que la orden está confirmada pero sin respaldo fiscal completo, lo que exige precisar qué significa finalizar la orden frente a la SAT (VAC-03), hoy cubierto por el supuesto del cierre fiscal; si se agota el máximo de reintentos, se genera una alerta para revisión manual.

**Nota de la Fase 2.** El comportamiento de la orden cuando se agotan los reintentos ante la SAT se registra en ADR-015: bandera de alerta fiscal con reintento manual, sin estado nuevo. El paso de CONFIRMADA_PENDIENTE_FISCAL a CONFIRMADA y a DESPACHO_SOLICITADO ocurre por eventos, sin intervención manual (ADR-016).

## ADR-012 — Propagación confiable de eventos al POS con reintentos e idempotencia

**Estado:** Aceptada.

**Contexto.** Las 32 instancias de POS no se reemplazan (EV-16, EV-40) y deben recibir eventos con manejo de errores y reintentos. El POS es consumidor de eventos, nunca fuente (RES-03), y puede estar intermitentemente inaccesible por tienda.

**Decisión.** El contexto de Integración POS consume los eventos de venta publicados por Órdenes y los envía a la instancia de POS correspondiente con un identificador único por evento que permite ignorar duplicados; ante falta de respuesta, el mensaje va a la cola de fallidos y se reintenta con backoff.

**Alternativas consideradas.** Tres opciones: (1) publicación de eventos con identificador único, cola de fallidos y reintentos; (2) llamada síncrona directa a cada POS, descartada porque una tienda sin conexión bloquearía o perdería el evento; (3) que el POS consulte periódicamente al sistema, descartada porque exigiría modificar el POS. Se eligió la propagación por eventos con idempotencia y reintentos por garantizar que ninguna venta se pierda ni se duplique aunque una tienda esté fuera de línea, sin exigirle al POS ninguna capacidad nueva.

**Consecuencias.** Positivas: ninguna venta se pierde ni se duplica en el POS aunque una tienda esté sin conexión; no exige que el POS exponga API ni consulte inventario central. Negativas: la reconciliación de una tienda fuera de línea depende de que el reintento la alcance, sin garantía de tiempo máximo declarada; el manejo de duplicados recae también del lado del POS.

## Decisiones de la Fase 2 (ADR-013 en adelante)

Las decisiones tomadas durante la Fase 2 se registran en archivos independientes en `documentacion/ADR/`, con el mismo formato que las anteriores. Los ADR-001 a ADR-012 no se modifican. El resumen de cada decisión se encuentra en la sección 14.5.

| ADR | Decisión | Relación con la Fase 1 | Archivo | Estado |
|---|---|---|---|---|
| ADR-013 | Docker Compose como entorno de ejecución del MVP | Precisa el entorno de ejecución y mantiene ADR-001 como plataforma de producción | `documentacion/ADR/ADR-013-docker-compose-entorno-ejecucion-mvp.md` | Aceptada |
| ADR-014 | El desacople exigido se cubre con Outbox y disyuntor | Confirma ADR-003 y ADR-009 como respuesta al requisito de desacople | `documentacion/ADR/ADR-014-desacople-outbox-y-disyuntor.md` | Aceptada |
| ADR-015 | Bandera de alerta fiscal con reintento manual, sin estado nuevo | Completa ADR-011 | `documentacion/ADR/ADR-015-bandera-alerta-fiscal-sin-estado-nuevo.md` | Aceptada |
| ADR-016 | La saga de Órdenes avanza por eventos de resultado, sin transiciones manuales | Concreta ADR-002 y ADR-003 | `documentacion/ADR/ADR-016-saga-dirigida-por-eventos-de-resultado.md` | Aceptada |
| ADR-017 | Despacho consulta a Inventario por HTTP síncrono y degrada la guía sin circuit breaker | Precisa la excepción a ADR-014 y el desvío de ADR-009 | `documentacion/ADR/ADR-017-despacho-consulta-inventario-http-y-guia-pendiente-sin-disyuntor.md` | Aceptada |
| ADR-018 | Contrato común de mensajería | Concreta ADR-003 | `documentacion/ADR/ADR-018-contrato-comun-de-mensajeria.md` | Aceptada |
| ADR-019 | Los sistemas externos se simulan con mocks en contenedor y fallas configurables | Sustenta ADR-013 y las pruebas de resiliencia de ADR-009 | `documentacion/ADR/ADR-019-mocks-de-sistemas-externos-con-fallas-configurables.md` | Aceptada |
| ADR-020 | La autenticación se valida en el borde, en el API Gateway, con el JWT emitido por Auth | Concreta la responsabilidad de autenticación del Borde de la sección 6.1 | `documentacion/ADR/ADR-020-autenticacion-en-el-borde.md` | Aceptada |
| ADR-021 | En el MVP la compensación de stock ocurre por expiración del TTL de la reserva | Acota la compensación de ADR-002 y se apoya en la expiración de ADR-005 | `documentacion/ADR/ADR-021-compensacion-de-stock-por-ttl-en-el-mvp.md` | Reemplazada por ADR-022 |
| ADR-022 | La compensación de stock es explícita: Inventario libera la reserva por evento y reingresa las devoluciones | Completa la compensación de ADR-002 y conserva la expiración de ADR-005 como red de seguridad | `documentacion/ADR/ADR-022-compensacion-explicita-de-stock.md` | Aceptada |
| ADR-023 | Historial crediticio por lotes desde el mock ERP y perfil precalculado en Crédito | Completa ADR-007, ADR-008 y ADR-019 para el producto "Compra ahora, paga en 30 días" | `documentacion/ADR/ADR-023-historial-crediticio-desde-mock-erp.md` | Aceptada |

---

# 11. Vistas arquitectónicas

Los diagramas constituyen vistas de este documento. Salvo indicación contraria, se emplea UML 2.5.1; las notaciones complementarias se declaran de forma explícita.

## 11.1 Diagrama de contexto

Muestra cómo SGO-Logic interactúa con sistemas internos, externos y partes interesadas.

![Diagrama de Contexto](../img/Diagrama%20de%20Contexto.png)

## 11.2 Diagrama de bloques

Vista de alto nivel de la arquitectura completa. **Notación:** no es UML; es una notación de bloques por capas de estilo C4, declarada como notación complementaria. El sistema se organiza en siete capas: canales, borde (API Gateway y Auth), aplicación (los contextos delimitados, con Órdenes como orquestador y Consulta de Estado separado para la lectura), mensajería (RabbitMQ en modos pub/sub, MOM y batch), datos (una base por contexto, con el almacén de auditoría de solo inserción como bloque propio), dependencias externas (incluido el Catálogo Comercial, fuera del alcance) y observabilidad (Prometheus y Grafana).

![Diagrama de Bloques](../img/Diagrama%20de%20bloques.png)

## 11.3 Diagrama de componentes

![Diagrama de Componentes](../img/Diagrama%20de%20Componentes.png)

**Fuente editable:** `documentacion/diagramas_CDU/Diagrama de Componentes.drawio` (draw.io). La imagen anterior es la exportación de ese archivo; tras modificar el `.drawio` hay que volver a exportar el PNG para que ambos coincidan.

Desde la Fase 3 el diagrama incluye los subsistemas `Crédito` y `Web y Canales`. `Crédito` se dibuja con sus tres consumidores de eventos (Órdenes, Pagos e Integración ERP), los dos workers de cobranza y reembolso, y su persistencia en `sgo_credito` sobre la instancia de finanzas. `Web y Canales` es la aplicación ASP.NET Core MVC que consume el `api-gateway` por HTTP y no publica ni consume eventos.

## 11.4 Diagramas de despliegue

Muestran los artefactos físicos del sistema y su integración con los componentes de software. La vista lógica presenta la distribución de servicios, mensajería y almacenes de datos; la vista física presenta el clúster de contenedores, los servidores de base de datos y el punto de entrada. Ambas reflejan los escenarios de calidad: escalado horizontal independiente por servicio (EAC-01, EAC-04), el modelo de lectura en Redis (EAC-02), la base de auditoría separada de solo inserción (EAC-03) y el bloqueo optimista con expiración en Inventario (EAC-05).

**Vista lógica**

![Diagrama de Despliegue Lógico](../img/Diagrama%20de%20Despliegue%20Lógico.png)

**Vista física**

![Diagrama de Despliegue Físico](../img/Diagrama%20de%20Despliegue%20Físico.png)

**Nota de la Fase 2.** Los diagramas de esta sección describen la plataforma de producción en Kubernetes. La topología del entorno de ejecución del MVP, en Docker Compose, se detalla en las secciones 14.1 y 14.2.

## 11.5 Diagramas de casos de uso de negocio y de casos de uso expandidos

El diagrama de casos de uso de negocio de alto nivel presenta los procesos de negocio y sus actores.

![Diagrama de Casos de Uso de Negocio de Alto Nivel](../img/Diagrama%20de%20Casos%20de%20Uso%20de%20Negocio%20de%20Alto%20Nivel.png)

Los casos de uso expandidos CU-01 a CU-19 se representan a continuación. CU-01 a CU-15 corresponden a los diagramas existentes; CU-16 a CU-19 son los casos de Crédito de la Fase 3 y cuentan con su fuente editable en `documentacion/diagramas_CDU/CU-16.drawio` a `CU-19.drawio`.

**CU-01**
![CU-01](../diagramas_CDU/CU-01.svg)

**CU-02**
![CU-02](../diagramas_CDU/CU-02.svg)

**CU-03**
![CU-03](../diagramas_CDU/CU-03.svg)

**CU-04**
![CU-04](../diagramas_CDU/CU-04.svg)

**CU-05**
![CU-05](../diagramas_CDU/CU-05.svg)

**CU-06**
![CU-06](../diagramas_CDU/CU-06.svg)

**CU-07**
![CU-07](../diagramas_CDU/CU-07.svg)

**CU-08**
![CU-08](../diagramas_CDU/CU-08.svg)

**CU-09**
![CU-09](../diagramas_CDU/CU-09.svg)

**CU-10**
![CU-10](../diagramas_CDU/CU-10.svg)

**CU-11**
![CU-11](../diagramas_CDU/CU-11.drawio.svg)

**CU-12**
![CU-12](../diagramas_CDU/CU-12.drawio.svg)

**CU-13**
![CU-13](../diagramas_CDU/CU-13.drawio.svg)

**CU-14**
![CU-14](../diagramas_CDU/CU-14.drawio.svg)

**CU-15**
![CU-15](../diagramas_CDU/CU-15.drawio.svg)

**CU-16**
![CU-16](../diagramas_CDU/CU-16.drawio.svg)

**CU-17**
![CU-17](../diagramas_CDU/CU-17.drawio.svg)

**CU-18**
![CU-18](../diagramas_CDU/CU-18.drawio.svg)

**CU-19**
![CU-19](../diagramas_CDU/CU-19.drawio.svg)

El siguiente diagrama presenta el flujo principal del sistema: el recorrido de punta a punta de una compra omnicanal, desde el reconocimiento del cliente hasta la confirmación de la orden.

![Flujo Principal del Sistema](../img/DDA_05%20-%20Flujo%20Principal%20del%20Sistema_%20Experiencia%20del%20Carrito%20Omnicanal%20(Preliminar%20v0.1).svg)

## 11.6 Prototipo de Interfaz de Usuario (Mockups)

Este apartado presenta las interfaces gráficas (mockups no funcionales) de la iniciativa **SGO-Logic** de MegaStore. El diseño adopta un enfoque minimalista y de alta fidelidad, asegurando que la experiencia de usuario y del personal interno sea consistente y moderna en todos los canales.

Estas vistas materializan los requerimientos funcionales documentados en la sección 6, demostrando visualmente el concepto de omnicanalidad (Entregable 4).

### Vista Cliente: Checkout Web Omnicanal
**Asociado a:** CU-01, CU-04 (Procesamiento de cobro y carrito unificado).  
**Propósito:** Demuestra la unificación del carrito y la selección del método de despacho (Recoger en tienda vs Envío a domicilio), indicando claramente que no se almacena información sensible de pago (EV-25).

**Versión Escritorio (Desktop)**
<p align="center">
  <img src="../img/mockups/checkout_desktop.png" width="90%">
</p>

**Versión Móvil**
<p align="center">
  <img src="../img/mockups/checkout_mobile.png" width="40%">
</p>

### Vista Tienda Física: Punto de Venta (POS)
**Asociado a:** CU-02, CU-03 (Recuperación de sesión).  
**Propósito:** Demuestra cómo un cajero en cualquiera de las 32 tiendas puede recuperar un carrito iniciado por el cliente en la nube (Redis) y finalizar el proceso físicamente.

**Pantalla de Cajero / Vendedor (iPad / Desktop)**
<p align="center">
  <img src="../img/mockups/POS_desktop.png" width="90%">
</p>

### Vista Postventa: Visión 360 del Cliente (Call Center)
**Asociado a:** CU-13, CAR-07.  
**Propósito:** Resuelve la necesidad de tener todo el historial (compras, despachos, devoluciones) en una sola vista con carga en menos de 5 segundos, reduciendo drásticamente el tiempo de atención al cliente.

**Versión Escritorio (Desktop)**
<p align="center">
  <img src="../img/mockups/soporte_desktop.png" width="90%">
</p>

**Versión Móvil**
<p align="center">
  <img src="../img/mockups/soporte_mobile.png" width="40%">
</p>

### Vista Logística: Dashboard de Despacho
**Asociado a:** CU-08 (Determinación y ejecución de despacho).  
**Propósito:** Herramienta interna para que el Jefe de Logística y su equipo vean las órdenes pendientes, el origen (Tienda o CD) y gestionen la integración de guías con LogiExpress, apuntando a reducir errores de envío en un 70%.

**Versión Escritorio (Desktop)**
<p align="center">
  <img src="../img/mockups/logistica_desktop.png" width="90%">
</p>

**Versión Móvil**
<p align="center">
  <img src="../img/mockups/logistica_mobile.png" width="40%">
</p>

> *Nota: Este prototipo visual fue generado basándose en la especificación de diseño arquitectónico de SGO-Logic.*

---

# 12. Matrices de trazabilidad

Las matrices demuestran que cada decisión responde a una preocupación real del negocio y que ninguna preocupación, escenario o parte interesada quedó sin atender.

## 12.1 Preocupación → Driver

| Preocupación | Drivers que la atienden |
|---|---|
| PRE-01 | EAC-01, EAC-04, CAR-03, CAR-04 |
| PRE-02 | CU-02, CU-03, EAC-01, CAR-03, CAR-04 |
| PRE-03 | CU-15, CAR-14 |
| PRE-04 | CAR-12 |
| PRE-05 | CU-05, EAC-03, CAR-06 |
| PRE-06 | CU-04, CAR-05 |
| PRE-07 | CU-05, EAC-03, CAR-06 |
| PRE-08 | CU-03, RES-08, CAR-04 |
| PRE-09 | CU-01, EAC-05, CAR-02 |
| PRE-10 | CU-06, CAR-02 |
| PRE-11 | CU-09, CU-08, CAR-10 |
| PRE-12 | CU-13, RES-02, CAR-09 |
| PRE-13 | CU-07, CAR-01 |
| PRE-14 | CU-10, EAC-02, CAR-07 |
| PRE-15 | CU-11, CAR-08 |
| PRE-16 | RES-01, RES-03 |
| PRE-17 | RES-12 |
| PRE-18 | RES-10, RES-11 |
| PRE-19 | RES-14, RES-15 |
| PRE-20 | CAR-03, CAR-04, CAR-05 |
| PRE-21 | CU-16, CU-17, RES-16, CAR-15 |
| PRE-22 | CU-18, CU-19, EAC-06, CAR-15 |

## 12.2 Driver → Decisión

| Driver | Decisión que lo resuelve |
|---|---|
| CU-01 / CAR-02 | ADR-005 |
| CU-02 / CAR-03 | ADR-002 |
| CU-03 / CAR-04 | ADR-009, ADR-010 |
| CU-04 / CAR-05 | ADR-011 |
| CU-05 / CAR-06 | ADR-006 |
| CU-07 / CAR-01 | ADR-001, ADR-004 |
| CU-08 | ADR-003 |
| CU-09 / CAR-10 | ADR-012 |
| CU-10 / CAR-07 | ADR-004 |
| CU-13 / CAR-09 | ADR-008 |
| CU-16 / RES-16 | ADR-007, ADR-008, ADR-019, ADR-023 |
| CU-17 / CU-18 / CU-19 / CAR-15 | ADR-001, ADR-002, ADR-003, ADR-010, ADR-016, ADR-023 |
| Integración ERP legacy | ADR-007 |
| EAC-01 / PRE-01 | ADR-001 |
| EAC-02 / PRE-14 | ADR-004 |

## 12.3 Decisión → Vista

| Decisión | Bloques | Contexto | Componentes | Despliegue |
|---|---|---|---|---|
| ADR-001 | Sí | Sí | Sí | Sí |
| ADR-002 | Sí | — | Sí | — |
| ADR-003 | — | — | Sí | Sí |
| ADR-004 | Sí | — | Sí | Sí |
| ADR-005 | — | — | Sí | Sí |
| ADR-006 | Sí | — | Sí | Sí |
| ADR-007 | Sí | Sí | Sí | — |
| ADR-008 | — | Sí | Sí | Sí |
| ADR-009 | — | Sí | Sí | — |
| ADR-010 | — | Sí | — | — |
| ADR-011 | Sí | Sí | Sí | — |
| ADR-012 | Sí | Sí | Sí | Sí |
| ADR-023 | Sí | Sí | Sí | Sí |

## 12.4 Escenario de calidad → Mecanismo y vista

| Escenario | Mecanismo | Se refleja en |
|---|---|---|
| EAC-01 | Escalado horizontal de Pagos; microservicios independientes | Despliegue |
| EAC-02 | CQRS con Redis materializado | Despliegue |
| EAC-03 | Bitácora append-only en base propia indexada | Despliegue |
| EAC-04 | Escalado horizontal independiente por servicio | Despliegue, Componentes |
| EAC-05 | Bloqueo optimista con versión y expiración | Despliegue, Componentes |
| EAC-06 | Perfil local precalculado, timeout corto y fallback a PayKlik | Contexto, Componentes, Despliegue |

## 12.5 Cobertura de partes interesadas

| Parte interesada | Preocupación principal | Decisiones que la atienden |
|---|---|---|
| STK-01 Junta Directiva | No tocar ERP ni POS | ADR-007, ADR-008, ADR-012 |
| STK-02 Dirección / Gerencia | Omnicanal dentro de presupuesto y a tiempo | ADR-001, ADR-004; RES-10, RES-11 |
| STK-03 Gerencia de TI | Continuidad, mantenibilidad, no tocar legacy | ADR-001, ADR-002, ADR-007; EAC-01 |
| STK-04 Dirección de Finanzas | Trazabilidad y cumplimiento fiscal | ADR-006, ADR-011; EAC-03 |
| STK-05 Jefatura de Logística | Reducir errores de despacho, visibilidad | ADR-012; CU-11, CAR-08 |
| STK-06 Dirección de Comercialización | Experiencia omnicanal sin silos | ADR-001, ADR-004; CU-07 |
| STK-07 Jefatura de Operaciones | No repetir la pérdida por indisponibilidad | ADR-009, ADR-002; EAC-01 |
| STK-14 SAT | Documentación fiscal válida y auditable | ADR-011, ADR-006 |
| STK-18 Reguladores de datos | Cumplimiento en tratamiento de datos | ADR-010; RES-09 |
| STK-19 Finanzas / Crédito | Historial verificable, línea y cobranza | ADR-006, ADR-008, ADR-023; CU-16, CU-19 |
| STK-20 Integración ERP | Traducción aislada y lotes confiables | ADR-007, ADR-008, ADR-019, ADR-023; CU-16 |

---

# 13. Vacíos de información y supuestos

Los siguientes puntos no están determinados por la información disponible y se documentan de forma explícita. Los que afectan el diseño se cubren con un supuesto declarado, sujeto a validación.

| ID | Vacío | Afecta a | Estado |
|---|---|---|---|
| VAC-03 | Definición precisa de "finalizar la orden" respecto de la SAT. | ADR-011, RES-07 | Supuesto adoptado: es el cierre fiscal, distinto de la confirmación comercial al cliente. Por validar con el cliente. |
| VAC-06 | Inventario de hardware existente on-premises. | RES-10, RES-14 | Abierto. Afecta el reparto del presupuesto entre inversión inicial y costo operativo. |
| VAC-09 | Fuente definitiva del Catálogo Comercial. | CAR-11, CUN-08 | Abierto. La administración de catálogo, precios y promociones queda fuera del alcance; el sistema la consume como dependencia externa. Por validar con el cliente. |

---

# 14. Cambios de la Fase 2

Esta sección documenta cómo se materializó la arquitectura definida en la Fase 1 durante la construcción del MVP. La Fase 2 respeta la arquitectura de la Fase 1 sin rediseñarla: los ADR-001 a ADR-012 no se modifican y el contenido de las secciones 1 a 13 corresponde a la Fase 1. Donde una decisión de la Fase 1 se concretó, precisó o acotó, la sección 10 incluye una nota que remite al ADR correspondiente. Las decisiones nuevas se registran como ADR independientes en `documentacion/ADR/`, del ADR-013 en adelante.

| Versión | Alcance |
|---|---|
| 1.0 | Definición de arquitectura de la Fase 1: secciones 1 a 13. |
| 2.0 | Incorpora la sección 14, Cambios de la Fase 2, y las referencias a ADR-013 en adelante en la sección 10. |
| 2.1 (2026-09-21) | Agrega ADR-020, autenticación en el borde, al índice de la sección 10 y a las secciones 14.5 a 14.7. Corrige la sección 14.1 (28 contenedores), la sección 14.2 (agrega `integracion-erp-worker` en el puerto 8096 y corrige `logiexpress-mock` al puerto 8098), la sección 14.6 (agrega la base `sgo_integracion_erp` a la instancia operacional) y las secciones 14.8 y 14.9 (ruta real del compose de escalado, `SGO-Logic/scripts/escalabilidad/docker-compose.scale.yml`). Corrige los enlaces de los diagramas CU-11 a CU-15 de la sección 11.5, cuyos archivos son `.drawio.svg`. |
| 2.2 (2026-09-22) | Cierre de la Fase 2. ADR-020 queda Aceptada con fecha 2026-09-22 y describe su implementación real (bandera `Gateway:ExigirJwt`, protección también de `/api/erp/*` y el límite de que el compose no pasa la bandera ni el secreto al gateway, agregado a 14.9). Agrega ADR-021, compensación de stock por expiración del TTL, al índice de la sección 10 y a las secciones 14.5, 14.7 y 14.9. Agrega a 14.6 la auditoría de cada intento de cobro fallido con el evento `IntentoPagoFallido` (`pagos-eventos-v1` v1.3, reflejado en 14.4 y 14.7), las migraciones al arranque de Integración POS e Integración ERP con su procedimiento de línea base, y la nota de que Prometheus no raspa los mocks externos. Actualiza 14.8 con la evidencia vigente de EAC-01 y del camino feliz del 2026-09-22, que reemplaza a la del 2026-09-19: 0 órdenes perdidas de 379 y 100% de intentos fallidos auditados (130 de 130). Reemplaza el enlace roto al diagrama de componentes de la sección 11.3 por una nota, porque el archivo no está en el repositorio, y corrige las rutas de `PLANTILLA-SERVICIO.md` y `despacho-eventos-v1.md`. Contrastados contra el código: 28 contenedores, puertos, bases por instancia, colas citadas y ADR-001 a ADR-021. |
| 2.3 (23/09/2026) | Refleja la integración de HU-003 y HU-014 en Inventario (merge `fa9b8e8`). Agrega ADR-022, compensación explícita de stock, que reemplaza a ADR-021: Inventario consume `ordenes.reserva.liberacion-solicitada` y `ordenes.devolucion.registrada` en la cola `inventario.ordenes`, libera la reserva en el momento y reingresa las unidades devueltas. Actualiza el índice de la sección 10 (ADR-021 queda Reemplazada por ADR-022) y las secciones 14.5, 14.7 y 14.9, que repetían la afirmación obsoleta de ADR-021; `inventario-eventos-v1` pasa a v1.1. Corrige en 14.5 y 14.9 la afirmación de que el compose no pasa la bandera al gateway: el servicio `api-gateway` recibe `Gateway__ExigirJwt` con `false` por defecto y `Auth__JwtSecret`. Agrega a 14.9 dos límites de Notificaciones: los avisos no se reintentan si el proveedor de mensajería falla, y el correo con el enlace de reintento de pago no se envía porque Notificaciones no consume `pagos.pago.pendiente`. |
| 2.4 (06/10/2026) | Agrega la Fase 3 de Crédito: EV-54 a EV-58, CUN-10, CAR-15, PRE-21 y PRE-22, CU-16 a CU-19, EAC-06, RES-16 y RES-17. Incorpora el contexto `credito-api`, el contrato `credito-eventos-v1`, la ampliación de `mock-erp` con snapshots de historial, y ADR-023 como decisión aceptada. Actualiza las matrices de trazabilidad, el mapa de servicios, los límites del MVP y la evidencia pendiente del recorrido `mock-erp -> Integración ERP -> Crédito -> checkout`. |
| 2.5 (13/10/2026) | Cierra la documentación gráfica de la Fase 3. Actualiza el diagrama de componentes de la sección 11.3 con los subsistemas `Crédito` (`credito-api`, puerto 8099, base `sgo_credito` en la instancia de finanzas) y `Web y Canales` (`web`, puerto 8092), incluido su enlace al `api-gateway` y al broker, y con el Growth del contenedor del sistema. Recupera las fuentes editables `CU-16.drawio` a `CU-19.drawio`, que solo existían como SVG exportado, y separa una página por archivo para no duplicar el modelo multipágina. Agrega el `Diagrama de Componentes.drawio` como fuente editable del diagrama de la sección 11.3. Corrige la nota obsoleta del historial 2.2 que decía que ese diagrama no estaba en el repositorio, y el enlace roto a `img/Trello.png` en `Gestion_Proyecto/Plan_Tablero_Trello.md`. |

Las rutas de archivos de esta sección se expresan desde la raíz del repositorio.

## 14.1 Entorno de despliegue del MVP

El entorno de ejecución del MVP es Docker Compose (ADR-013). El archivo `SGO-Logic/docker-compose.yml` levanta en una sola máquina, con `docker compose up --build`, 28 contenedores: los servicios del backbone, sus bases de datos por contexto, RabbitMQ, Redis, la pila de observabilidad y los mocks de los sistemas externos. La configuración se entrega por variables de entorno (`SGO-Logic/.env.example`) y los servicios del backbone exponen `/health/live` y `/health/ready`.

Kubernetes on-premises se mantiene como plataforma de producción (ADR-001, RES-14, RES-15, sección 9.2). Docker Compose no la reemplaza: la topología de mensajería y el aislamiento de bases de datos por contexto son idénticos en ambos entornos, de modo que migrar entre ellos exige cambiar manifiestos de despliegue y no código de aplicación.

| Componente | Versión en el MVP |
|---|---|
| Servicios .NET | .NET 8 |
| Motor relacional | PostgreSQL 16 (tres instancias, ver 14.6) |
| Broker de mensajería | RabbitMQ 3.13 con consola de administración |
| Modelo de lectura y carrito | Redis 7 |
| Métricas y tableros | Prometheus 2.55.1 y Grafana 11.3.1 |
| Mocks de sistemas externos | .NET 8 y Node (ver 14.3) |

## 14.2 Mapa de servicios y puertos

Los puertos corresponden a los publicados en el host por `SGO-Logic/docker-compose.yml`. Entre contenedores, los servicios .NET escuchan en el puerto 8080.

**Servicios de aplicación**

| Servicio | Rol | Puerto en el host | Almacén de datos |
|---|---|---|---|
| `api-gateway` | Borde: punto único de entrada y ruteo por prefijo hacia los contextos. | 8080 | Ninguno |
| `web` | Aplicación web ASP.NET Core MVC que consume el gateway. | 8092 | Ninguno |
| `auth-api` | Identidad, emisión de JWT y carrito unificado. | 8088 | `sgo_auth` (instancia operacional) y Redis |
| `ordenes-api` | Orquestador de la saga y ciclo de vida de la orden. | 8083 | `sgo_ordenes` (operacional) |
| `inventario-api` | Existencias, reservas con TTL y consultas de disponibilidad. | 8094 | `sgo_inventario` (operacional) |
| `pagos-api` | Cobro contra PayKlik, disyuntor y reembolsos. | 8085 | `sgo_pagos` (finanzas) |
| `facturacion-api` | Documento fiscal y certificación asíncrona ante la SAT. | 8087 | `sgo_facturacion` (finanzas) |
| `despacho-api` | Origen de despacho, guía con LogiExpress y estados de entrega. | 8097 | `sgo_despacho` (operacional) |
| `consulta-estado-api` | Modelo de lectura del estado de las órdenes. | 8091 | `sgo_consulta_estado` (operacional) y Redis |
| `notificaciones-api` | Notificaciones al cliente, aisladas del flujo principal. | 8090 | `sgo_notificaciones` (operacional) |
| `integracion-pos-worker` | Propagación de eventos de venta hacia las instancias de POS. | 8095 | `sgo_integracion_pos` (operacional) |
| `integracion-erp-worker` | Consolidación de ventas, procesamiento batch del historial y envío de eventos al ERP-Core, con conciliación. | 8096 | `sgo_integracion_erp` (operacional) |
| `credito-api` | Perfil crediticio, elegibilidad, línea, cuentas por cobrar y abonos a 30 días. | 8099 | `sgo_credito` (finanzas) |
| `auditoria-worker` | Bitácora append-only de eventos. | 8093 | `sgo_auditoria` (instancia de auditoría) |
| `sample-api` y `sample-worker` | Plantillas de servicio para crear contextos nuevos (`documentacion/docs/contratos/PLANTILLA-SERVICIO.md`). | 8081 y 8082 | `sgo_sample_api` y `sgo_sample_worker` (operacional) |

**Mocks de sistemas externos**

| Servicio | Simula | Puerto en el host |
|---|---|---|
| `payklik-mock` | PayKlik, pasarela de pago | 8084 |
| `sat-mock` | SAT, certificación fiscal | 8086 |
| `logiexpress-mock` | LogiExpress, transportista | 8098 |
| `mock-mensajeria` | Proveedor de SMS, correo y push | 8089 |
| `mock-erp` | ERP-Core sobre AS/400 | 4000 |
| `mock-pos` | 32 instancias de POS | 4001 |

**Infraestructura**

| Servicio | Función | Puerto en el host |
|---|---|---|
| `rabbitmq` | Broker de mensajería (AMQP y consola de administración) | 5672 y 15672 |
| `redis` | Modelo de lectura de Consulta de Estado y carrito | 6379 |
| `prometheus` | Recolección de métricas | 9090 |
| `grafana` | Tableros de observabilidad | 3000 |
| `db-operacional`, `db-finanzas`, `db-auditoria` | Instancias de PostgreSQL | No se publican |

## 14.3 Mocks de sistemas externos y fallas que simulan

Los sistemas externos se simulan con mocks en contenedor que exponen el contrato conocido del sistema real y una API de administración para provocar fallas (ADR-019). Ningún servicio de SGO-Logic recibe ni devuelve datos de tarjeta: el único lugar donde existen es el proceso de `payklik-mock`, que hace las veces de PayKlik al tokenizar (sección 14.3.1). Los demás mocks no reciben datos de tarjeta.

| Mock | Fallas que simula | Cómo se provoca |
|---|---|---|
| `payklik-mock` | Latencia configurable, caída (503), rechazo del cargo por fondos insuficientes e intermitencia con probabilidad configurable, tanto en el cargo como en la tokenización. Idempotencia por `Idempotency-Key`. Tokenización de la tarjeta (`POST /v1/tokens`) con token de un solo uso y vigencia corta; el cargo con un token inexistente, vencido o ya usado responde 422, distinto del rechazo por fondos. | `PUT /admin/modo` con `NORMAL`, `LENTO`, `CAIDO`, `RECHAZO` o `INTERMITENTE`. `GET /admin/tokens` lista los tokens emitidos (token, últimos cuatro, marca y vigencia, nunca el número de tarjeta). |
| `sat-mock` | Latencia, caída (503), rechazo de la certificación y una probabilidad de falla 503 aplicable en cualquier modo. | `PUT /admin/modo` con `NORMAL`, `LENTO`, `CAIDO` o `RECHAZO` |
| `logiexpress-mock` | Latencia, caída y un timeout determinístico: una de cada 50 solicitudes de guía no responde. | `PUT /admin/modo`; el cambio del estado de rastreo de una guía se hace con `POST /admin/guias/{guiaId}/estado` |
| `mock-mensajeria` | Falla del proveedor (respuesta 500) en SMS, correo y push. | Variable de configuración `MockMensajeria__ForzarFalla` o `POST /api/mensajes/config/falla?activa=true` |
| `mock-erp` | Rechazo de lotes con totales que no cuadran (422), lotes duplicados (409), secuencia retrocedida (409), exportación de snapshots de historial elegibles/no elegibles y caída con respuesta 503, 500 o timeout de 8 segundos. | `POST /erp/admin/caida`; administración de fixtures y lotes de historial según README del mock |
| `mock-pos` | Tienda fuera de línea con respuesta 503, 500 o timeout, por tienda o de forma masiva. Idempotencia por tienda y `eventoId`. | `POST /pos/admin/{tiendaId}/caida` y `POST /pos/admin/caida-masiva` |

El cambio de estado de rastreo que se aplica en `logiexpress-mock` no se propaga por sí solo a Despacho. En una demostración, los avances de entrega se informan al webhook de Despacho (`POST /api/despacho/{ordenId}/estados`), que es lo que haría el transportista real.

### 14.3.1 Flujo de tokenización de la tarjeta

Este flujo ejecuta ADR-010 sin modificarlo: la tokenización se delega a PayKlik y SGO-Logic guarda solo el token y los metadatos del intento. **El número de tarjeta (PAN), el código de seguridad (CVV) y la fecha de vencimiento nunca entran al perímetro de SGO-Logic.** Ningún servicio (Órdenes, Pagos, Facturación, `api-gateway` ni `web`) los recibe, los propaga, los registra en un log ni los persiste; lo único que cruza hacia SGO-Logic es el token y sus metadatos no sensibles (últimos cuatro dígitos y marca).

| Paso | Qué ocurre | Qué dato de tarjeta ve |
|---|---|---|
| 1. Tokenizar | El navegador envía la tarjeta directo a `POST /v1/tokens` de PayKlik (en el MVP, `payklik-mock`, puerto 8084; el mock abre CORS en ese endpoint porque lo llama el navegador). PayKlik valida el número con Luhn, el CVV de 3 o 4 dígitos y que la tarjeta no esté vencida, descarta el número y el CVV, y responde con un token opaco de un solo uso, los últimos cuatro dígitos, la marca deducida del BIN y la vigencia del token (900 segundos, configurable en `Tokenizacion:VigenciaSegundos`). | Solo el navegador y PayKlik. |
| 2. Crear la orden | El checkout llama a `POST /api/ordenes` con el campo `tokenMedioPago`, obligatorio cuando el medio de pago es `TARJETA` y ignorado para transferencia y billetera. Órdenes lo conserva solo hasta emitir `CobroSolicitado`, porque ese evento sale después, cuando Inventario confirma la reserva: la columna `ordenes.orden.token_medio_pago` es nula salvo en ese tramo y no se devuelve en ninguna respuesta ni en ningún evento distinto de `CobroSolicitado`. | Solo el token. |
| 3. Solicitar el cobro | `CobroSolicitado` lleva `tokenMedioPago` hacia Pagos (`ordenes-eventos-v1`, evento 5). Los campos `ultimosCuatro` y `marca` del evento quedan nulos: los resuelve PayKlik. | Solo el token. |
| 4. Cobrar | Pagos envía el token a `POST /v1/cargos`. PayKlik lo resuelve contra su almacén en memoria, consume el token y devuelve los últimos cuatro dígitos y la marca reales de la tarjeta. Un cargo con `TARJETA` sin token se rechaza en Pagos antes de llamar a PayKlik. | Solo el token, los últimos cuatro y la marca. |
| 5. Registrar | Pagos persiste el token, los últimos cuatro y la marca en `pagos.intento_pago`. El token no viaja en los eventos de resultado (`pagos-eventos-v1`, sección 1). | Solo el token, los últimos cuatro y la marca. |

Un token inexistente, vencido o ya usado es un error del cliente, no una caída del proveedor: PayKlik responde 422 con un código (`TOKEN_INVALIDO`, `TOKEN_VENCIDO`, `TOKEN_YA_USADO`), Pagos lo registra como intento `RECHAZADO` y publica `PagoRechazado` con ese `codigoRespuesta`, sin reintento, sin enlace de reintento y sin que cuente como falla para el disyuntor (ADR-009). Así se distingue del rechazo por fondos insuficientes, que PayKlik responde con 200, estado `RECHAZADO` y código `51`.

El almacén de tokens de `payklik-mock` vive solo en la memoria del proceso: no hay base de datos, un reinicio invalida los tokens y el mock conserva únicamente los últimos cuatro dígitos, la marca y el mes y año de expiración, no el número de tarjeta ni el CVV. La prueba automatizada `CobroTokenizadoTests` (`SGO-Logic/tests/SgoLogic.Pagos.Tests/Application/CobroTokenizadoTests.cs`) verifica que un cobro tokenizado no deja PAN ni CVV en la entidad persistida, en el modelo de la base ni en el payload del Outbox, y falla si se agrega a esos tipos un campo con datos de tarjeta.

## 14.4 Cambios a contratos de eventos

Todos los contratos comparten el envelope de `sgo-eventos-envelope-v1.md` (ADR-018). La regla de versionado distingue cambios aditivos, que incrementan la versión menor sin renegociar, de cambios rompientes, que exigen la versión mayor. Ningún cambio de la Fase 2 fue rompiente.

| Contrato | Versión | Cambios de la Fase 2 |
|---|---|---|
| `contratos/sgo-eventos-envelope-v1.md` | v1 | Sin cambio de versión. La topología del broker se completó en `TOPOLOGIA-MENSAJERIA.md`: colas quorum con límite de entregas y dueño de la dead letter. |
| `contratos/ordenes-eventos-v1.md` | v1.4 | v1.1: incorpora las objeciones de los demás contextos antes del congelamiento (envelope y enumeraciones compartidos, `sequence` por agregado, evento 12 `OrdenCertificadaFiscalmente`). v1.2: implementa los eventos 11 y 12 y agrega la cola `ordenes.facturacion` y los bindings de `pagos.ordenes` y `facturacion.ordenes` a `ordenes.devolucion.registrada`. v1.3: `DevolucionRegistrada` agrega `lineas[].ubicacionOrigenReservaId` y `lineas[].reservaId`. v1.4: `DespachoSolicitado` lleva el `reservaId` real y la `direccionEntrega`; `POST /api/ordenes` acepta `direccionEntrega` como campo opcional; `LiberacionReservaSolicitada` no se publica cuando la orden no tiene reserva; se agrega la cola `ordenes.despacho`. |
| `contratos/pagos-eventos-v1.md` | v1.3 | v1.1: agrega los eventos `ReembolsoAplicado` y `ReembolsoFallido`. v1.2: `PagoRechazado.codigoRespuesta` admite los códigos internos `TOKEN_REQUERIDO`, `TOKEN_INVALIDO`, `TOKEN_VENCIDO` y `TOKEN_YA_USADO`, por el flujo de tokenización de la sección 14.3.1. v1.3: agrega el evento 6, `IntentoPagoFallido` (`pagos.intento.fallido`), solo de auditoría, uno por cada intento de cobro fallido (sección 14.6). Cambios aditivos. |
| `contratos/inventario-eventos-v1.md` | v1 | Agrega el evento 3, `ReservaInventarioExpirada`, el 2026-09-12. Cambio aditivo. |
| `contratos/facturacion-eventos-v1.md` | v1 | Sin cambio de versión: `DocumentoFiscalCertificado` y `CertificacionSatFallida`. |
| `contratos/credito-eventos-v1.md` | v1 | Contrato de Fase 3: `HistorialClienteErpActualizado`, `PerfilCrediticioActualizado`, evaluación de crédito, cuentas por cobrar, abonos, vencimientos y reversos. La fuente del historial y el recorrido ejecutable se concretan en ADR-023. |
| `documentacion/docs/despacho-eventos-v1.md` | v1 | Contrato nuevo de la Fase 2: `OrigenDespachoDeterminado`, `GuiaAsignada`, `GuiaPendiente`, `PreparacionSolicitada`, `DespachoEstadoActualizado` y `PedidoListoParaRecoger`. |

## 14.5 Resumen de ADR-013 en adelante

Los archivos se encuentran en `documentacion/ADR/`. Todos tienen estado Aceptada, salvo ADR-021, reemplazada por ADR-022.

**ADR-013, Docker Compose como entorno de ejecución del MVP.** El MVP se ejecuta con Docker Compose para que pueda levantarse en una máquina limpia sin un clúster. Kubernetes on-premises sigue siendo la plataforma de producción y no se modifica. El escalado horizontal automático (HPA) no se ejecuta en el MVP.

**ADR-014, El desacople exigido se cubre con Outbox y disyuntor.** No se introduce un mecanismo de desacople adicional: el patrón Outbox (ADR-003) desacopla la publicación de la disponibilidad del broker, y el disyuntor con backoff (ADR-009) evita que la caída de un proveedor externo se propague al flujo de negocio. La consecuencia aceptada es la latencia del polling y la consistencia eventual entre contextos. ADR-017 precisa la única excepción a la ausencia de llamadas síncronas entre contextos.

**ADR-015, Bandera de alerta fiscal sin estado nuevo.** Cuando se agotan los reintentos de certificación ante la SAT, la orden se mantiene en `CONFIRMADA_PENDIENTE_FISCAL` con una bandera de alerta visible y consultable, y un reintento manual republica la solicitud. No se agrega un estado a la máquina de estados, cuyo contrato consumen otros contextos.

**ADR-016, La saga de Órdenes avanza por eventos de resultado.** Órdenes consume los resultados de Inventario, Pagos, Facturación y Despacho por colas propias, valida cada transición contra la máquina de estados y encadena en una sola transacción el paso fiscal tras el pago y la solicitud de despacho tras la certificación. Tolera eventos fuera de orden y no requiere transiciones manuales.

**ADR-017, Despacho consulta a Inventario por HTTP síncrono y degrada la guía sin circuit breaker.** Se aceptan dos excepciones acotadas a ADR-014 y ADR-009: una lectura síncrona de Inventario para determinar el origen del despacho y una única llamada a LogiExpress que, si falla, deja el despacho en `GUIA_PENDIENTE` sin detener la salida del pedido. El ADR registra el riesgo que queda: acoplamiento de disponibilidad con Inventario y ausencia de disyuntor durante una caída prolongada del proveedor.

**ADR-018, Contrato común de mensajería.** Un envelope único con `transactionId`, `causationId` y `sequence`, una topología con colas quorum, dead letter y límite de entregas, reintentos en dos niveles y bibliotecas compartidas (`SgoLogic.Contracts` y `SgoLogic.BuildingBlocks`) sostienen la idempotencia, el orden y la reconstrucción de transacciones por Auditoría.

**ADR-019, Los sistemas externos se simulan con mocks en contenedor.** Cada proveedor y sistema legacy tiene un mock con fallas configurables, lo que permite repetir las pruebas de resiliencia y levantar el MVP sin acceso a los sistemas reales. Node se usa únicamente en dos mocks y no forma parte del producto.

**ADR-020, La autenticación se valida en el borde.** El API Gateway valida el JWT emitido por Auth con `AddJwtBearer` y el secreto compartido `Auth:JwtSecret`, y lo exige en las rutas de órdenes, pagos, facturación, inventario, despacho y auditoría; `/api/auth/*` y `/health/*` quedan anónimas. La exigencia se activa con la bandera `Gateway:ExigirJwt` (variable de entorno `Gateway__ExigirJwt`), apagada por defecto para no romper los scripts de carga; la implementación protege además `/api/erp/*`. Los servicios internos confían en la red interna y no revalidan el token. El riesgo que queda: en el MVP los puertos de cada servicio siguen publicados en el host, y quien los use evade el gateway; en producción solo se expone el gateway. El compose del MVP pasa la bandera y el secreto al gateway (`Gateway__ExigirJwt` desde `GATEWAY_EXIGIR_JWT`, con `false` por defecto, y `Auth__JwtSecret` desde `JWT_SECRET`), así que en ese entorno la exigencia queda apagada salvo que se encienda en `.env`. Se descartaron la validación del JWT en cada servicio y el mTLS entre servicios.

**ADR-021, En el MVP la compensación de stock ocurre por expiración del TTL.** Reemplazada por ADR-022. Cerraba el MVP con la liberación de reservas solo por TTL y el reingreso por devolución como ajuste manual, porque Inventario no consumía los eventos de compensación de Órdenes. Se conserva como registro de la decisión anterior.

**ADR-022, La compensación de stock es explícita.** Inventario consume en su cola `inventario.ordenes`, además de `ordenes.reserva.solicitada`, los eventos `ordenes.reserva.liberacion-solicitada` y `ordenes.devolucion.registrada` con un único consumidor multi-evento (`OrdenesEventosConsumidor`). Ante una solicitud de liberación, libera en el momento la reserva `ACTIVA` de la orden, la pasa a `LIBERADA` y publica `ReservaInventarioLiberada` (`inventario.reserva.liberada`, HU-003). Ante una devolución, suma las unidades a la cantidad física de la ubicación de origen de cada línea, deja asiento en el ledger con motivo `REINGRESO_DEVOLUCION` y publica `StockReingresado` (`inventario.stock.reingresado`, HU-014). La expiración por TTL se mantiene como red de seguridad para las reservas que Órdenes no llega a conocer. Es un cambio aditivo: `inventario-eventos-v1` pasa a v1.1 y `ordenes-eventos-v1` no cambia. El riesgo que queda: la reserva de una orden pagada sigue venciendo por TTL, el reingreso no tiene idempotencia de negocio por `devolucionId` y ningún contexto de la saga consume todavía los dos eventos nuevos.

**ADR-023, Historial crediticio por lotes desde el mock ERP y perfil precalculado en Crédito.** `mock-erp` mantiene fixtures de historial y los entrega agrupados en snapshots por lote. Integración ERP usa una ACL para traducirlos al evento `HistorialClienteErpActualizado`, que publica por Outbox y consume Crédito. Crédito conserva el último corte válido, recalcula el perfil y atiende la elegibilidad localmente. El checkout nunca llama al ERP: si Crédito no responde dentro del límite, Web ofrece solo PayKlik. La saga de Órdenes solicita evaluación después de reservar inventario y conserva el final común de orden confirmada o rechazada. Se descartan derivar todo desde eventos internos, consultar el ERP en vivo, usar la siembra demo como flujo principal y contratar un score externo. El costo aceptado es consistencia eventual del historial y mantenimiento de fixtures; el recorrido y sus fallas son reproducibles en el MVP.

## 14.6 Decisiones de implementación registradas en este documento

Las siguientes decisiones no cambian un contrato entre contextos ni el entorno de despliegue: son detalles de implementación de un contexto o concretan una decisión de la Fase 1. Por eso se documentan aquí y no como ADR independientes.

**Persistencia.** PostgreSQL es el motor relacional (decisión cerrada el 2026-09-07, coherente con el costo de licenciamiento nulo de la sección 9.1). Cada contexto es dueño de su propia base, con nombre `sgo_<contexto>`, y ningún contexto lee ni escribe la base de otro (ADR-001). Las bases se agrupan en tres instancias: operacional (Órdenes, Inventario, Despacho, Consulta de Estado, Auth, Notificaciones, Integración POS, Integración ERP y las plantillas), finanzas (Pagos y Facturación) y auditoría (Auditoría, ADR-006). El acceso es con Entity Framework Core 8, las migraciones se aplican al arrancar cada servicio con `Database.Migrate()`, incluidos los workers de Integración POS e Integración ERP, y la concurrencia es optimista con una columna de versión (ADR-005). Una base `sgo_integracion_erp` creada antes con `EnsureCreated()` requiere registrar la migración inicial como línea base antes de desplegar (`documentacion/docs/MIGRACIONES-BASELINE.md`). Cada servicio conserva su tabla de Outbox en su propia base.

**Consulta de Estado.** El servicio consume los eventos de Órdenes y mantiene la vista de cada orden en Redis, como caché caliente con vencimiento de 24 horas, y en PostgreSQL (`ordenes_vista`), como respaldo durable y fuente del historial paginado. Si Redis no responde, la consulta se sirve desde PostgreSQL. Esta configuración concreta la estrategia de recarga que ADR-004 declaraba necesaria y mantiene su decisión de fondo: la consulta no orquesta llamadas en tiempo real a otros contextos ni a sistemas externos.

**Observabilidad.** Prometheus y Grafana implementan CAR-12. Cada servicio que registra los valores por defecto de la solución expone `/metrics` mediante prometheus-net, y Prometheus lo recoge cada 5 segundos según `SGO-Logic/observabilidad/prometheus.yml`. Grafana se aprovisiona por archivo con dos tableros, "SGO-Logic - Salud del sistema" y "SGO-Logic - Mensajería y resiliencia". Las métricas propias son `outbox_pendientes`, `outbox_publicados_total`, `outbox_fallidos_total`, `outbox_latencia_publicacion_segundos`, `mensajes_consumidos_total`, `mensajes_duplicados_total` y `alertas_fiscales_levantadas_total`, además de las métricas HTTP estándar. La instrumentación no incluye gestor de alertas, agregación de logs ni trazas distribuidas. Los sistemas externos se observan de forma indirecta, a través de las métricas de mensajería y de HTTP de los servicios que los consumen. Por decisión de diseño, Prometheus no raspa los mocks de sistemas externos (`payklik-mock`, `sat-mock`, `logiexpress-mock`, `mock-erp` y `mock-pos`): son simuladores de terceros y no forman parte del sistema, de modo que sus métricas no aportan a CAR-12. La única excepción es `mock-mensajeria`, que usa las bibliotecas comunes y expone `/metrics` como efecto de ellas.

**Borde, identidad y aplicación web.** El `api-gateway` usa YARP para enrutar por prefijo hacia los contextos. `auth-api` emite JWT y administra el carrito unificado en Redis. La validación del JWT en el gateway se decide en ADR-020. `web` es una aplicación ASP.NET Core MVC que consume el gateway.

**Auditoría de intentos de cobro.** Pagos registra cada intento de cobro en `pagos.intento_pago` antes de invocar a PayKlik (HU-004) y reintenta una vez con backoff ante timeout, 5xx o error de red (`PayKlik:MaxReintentos` = 1, ADR-009), de modo que un cobro puede producir dos intentos fallidos. Para cumplir la medida de EAC-01 (100% de intentos fallidos auditados), cada intento que termina `FALLIDO`, intermedio o final, publica por el Outbox, en la misma transacción que el intento, el evento `IntentoPagoFallido` (`pagos.intento.fallido`, evento 6 de `pagos-eventos-v1` v1.3), que Auditoría recibe por su binding `#`. Es un evento solo de auditoría: cada cobro sigue emitiendo un único evento de resultado (`PagoAplicado`, `PagoRechazado` o `PagoPendiente`) con la misma `sequence`, y ninguna cola de la saga está ligada a `pagos.intento.fallido`, así que Órdenes, Inventario y Despacho no cambian su comportamiento. Antes de este cambio, del 2026-09-22, solo el último intento fallido llegaba a Auditoría, dentro de `PagoPendiente`.

**Crédito y perfil precalculado.** Crédito es dueño de `sgo_credito`, del snapshot vigente de `historial_cliente`, `perfil_crediticio`, líneas, cargos, abonos y mensajes procesados. No lee ERP, Órdenes ni Consulta de Estado. Integración ERP procesa el lote de historial desde `mock-erp` o el adaptador del ERP real, normaliza el email, valida fecha de corte y publica `HistorialClienteErpActualizado` por Outbox. El consumidor de Crédito guarda historial, perfil, línea y evento de salida en una sola transacción; un corte igual o anterior no retrocede el perfil. `GET /api/credito/elegibilidad` solo consulta esta proyección y tiene un límite corto configurado en Web. `SembrarDemo` y `/interno/credito/historiales` quedan para desarrollo aislado, no para demostrar el recorrido batch.

**Parámetros del Outbox.** El publicador consulta la tabla de Outbox cada 2 segundos (`OUTBOX_POLLING_MS`), procesa lotes de 20 filas (`OUTBOX_BATCH_SIZE`) y reintenta hasta 5 veces (`OUTBOX_MAX_REINTENTOS`) antes de enviar el mensaje a la dead letter. El intervalo de polling determina la latencia mínima de cada salto entre contextos (ADR-014).

**Esquema de evidencia.** Las salidas de las corridas de verificación se guardan en `SGO-Logic/scripts/pruebas/evidencia/` como archivos `.txt` en UTF-8, con el nombre `<prueba>-<fecha>.txt`, y se versionan para conservar el historial. Los generan `SGO-Logic/scripts/pruebas/camino-feliz.ps1` (flujo completo de la saga), `SGO-Logic/scripts/pruebas-carga/eac01-hu023-carga.js` (carga con k6), `SGO-Logic/scripts/pruebas-carga/eac01-hu023-verificar.sh` (verificación contra las bases) y la lectura de `/metrics` de Órdenes. La evidencia de los demás escenarios de calidad se documenta en `documentacion/docs/`.

## 14.7 Trazabilidad de decisiones

Las rutas de código se expresan desde la raíz del repositorio.

| Decisión | ADR | Archivos de código |
|---|---|---|
| Docker Compose como entorno del MVP | ADR-013 | `SGO-Logic/docker-compose.yml`, `SGO-Logic/.env.example`, `SGO-Logic/src/Services/*/Dockerfile` |
| Auditoría de cada intento de cobro fallido | EAC-01 (sin ADR propio, ver 14.6; con ADR-003 y ADR-009) | `SGO-Logic/src/Services/SgoLogic.Pagos.Api/Application/Pagos/ServicioCobro.cs`, `SGO-Logic/src/BuildingBlocks/SgoLogic.Contracts/Eventos/Pagos/IntentoPagoFallido.cs`, `SGO-Logic/tests/SgoLogic.Pagos.Tests/Application/ServicioCobroTests.cs` |
| Desacople con Outbox y disyuntor | ADR-014 (con ADR-003 y ADR-009) | `SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Outbox/OutboxPublisher.cs`, `SGO-Logic/src/Services/SgoLogic.Pagos.Api/Infrastructure/PayKlik/DisyuntorPayKlik.cs`, `SGO-Logic/src/Services/SgoLogic.Pagos.Api/Infrastructure/PayKlik/ClientePayKlikConDisyuntor.cs` |
| Bandera de alerta fiscal con reintento manual | ADR-015 | `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Domain/Orden.cs`, `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/ManejadorCertificacionSatFallida.cs`, `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/ServicioConfirmacionOrden.cs` |
| Saga de Órdenes dirigida por eventos de resultado | ADR-016 | `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Consumers/` (`InventarioEventosConsumidor.cs`, `PagosEventosConsumidor.cs`, `DocumentoFiscalCertificadoConsumidor.cs`, `DespachoEventosConsumidor.cs`), `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/` (`ManejadorEventosInventario.cs`, `ManejadorEventosPagos.cs`, `ManejadorDocumentoFiscalCertificado.cs`, `ManejadorEventosDespacho.cs`, `ServicioConfirmacionOrden.cs`, `TransicionEncadenada.cs`, `Eventos/MapaEventosSagaOrden.cs`), `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Domain/MaquinaEstadosOrden.cs` |
| Despacho: consulta síncrona a Inventario y guía sin circuit breaker | ADR-017 | `SGO-Logic/src/Services/SgoLogic.Despacho.Api/Infrastructure/Inventario/ClienteInventarioHttp.cs`, `SGO-Logic/src/Services/SgoLogic.Despacho.Api/Infrastructure/LogiExpress/ClienteLogiExpressHttp.cs`, `SGO-Logic/src/Services/SgoLogic.Despacho.Api/Application/Despacho/ServicioDespacho.cs` |
| Contrato común de mensajería | ADR-018 | `SGO-Logic/src/BuildingBlocks/SgoLogic.Contracts/IntegrationEvent.cs`, `SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Messaging/`, `SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Idempotencia/EventConsumerBase.cs`, `SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Correlation/EventContext.cs` |
| Compensación explícita de stock: liberación por evento y reingreso por devolución, con el TTL como red de seguridad | ADR-022, que reemplaza a ADR-021 (con ADR-005) | `SGO-Logic/src/Services/SgoLogic.Inventario.Api/Consumers/OrdenesEventosConsumidor.cs`, `SGO-Logic/src/Services/SgoLogic.Inventario.Api/Application/Inventario/ServicioLiberacionReservas.cs`, `SGO-Logic/src/Services/SgoLogic.Inventario.Api/Application/Inventario/ServicioReingresoStock.cs`, `SGO-Logic/src/Services/SgoLogic.Inventario.Api/Workers/ExpiracionReservasWorker.cs`, `SGO-Logic/src/Services/SgoLogic.Inventario.Api/Application/Inventario/ServicioExpiracionReservas.cs`, `SGO-Logic/src/BuildingBlocks/SgoLogic.Contracts/Eventos/Inventario/ReservaInventarioLiberada.cs`, `SGO-Logic/src/BuildingBlocks/SgoLogic.Contracts/Eventos/Inventario/StockReingresado.cs`, `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/Eventos/MapaEventosSagaOrden.cs`, `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/ServicioDevolucion.cs` |
| Mocks de sistemas externos | ADR-019 | `SGO-Logic/src/Services/SgoLogic.Mocks.PayKlik/`, `SGO-Logic/src/Services/SgoLogic.Mocks.SAT/`, `SGO-Logic/src/Services/SgoLogic.Mocks.LogiExpress/`, `SGO-Logic/src/Services/SgoLogic.MockMensajeria/`, `Mocks/mock-erp/`, `Mocks/mock-pos/` |
| Historial ERP y perfil precalculado de Crédito | ADR-023, con ADR-007, ADR-008 y ADR-019 | `Mocks/mock-erp/server.js`, `Mocks/mock-erp/README.md`, `SGO-Logic/src/Services/SgoLogic.IntegracionErp.Worker/`, `SGO-Logic/src/BuildingBlocks/SgoLogic.Contracts/Eventos/IntegracionErp/HistorialClienteErpActualizado.cs`, `SGO-Logic/src/Services/SgoLogic.Credito.Api/Consumers/HistorialClienteErpConsumidor.cs`, `SGO-Logic/src/Services/SgoLogic.Credito.Api/Application/ServicioPerfilCrediticio.cs` |
| Persistencia con PostgreSQL, base por contexto y migraciones al arranque | ADR-001, ADR-005 (sin ADR propio, ver 14.6) | `SGO-Logic/scripts/postgres/crear-bases-adicionales.sh`, carpeta `Infrastructure/Persistencia/` de cada servicio |
| Consulta de Estado con Redis y respaldo en PostgreSQL | ADR-004 (sin ADR propio, ver 14.6) | `SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api/Application/ConsultaEstadoService.cs`, `SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api/Consumers/ConsultaEstadoOrdenesConsumidor.cs`, `SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api/Infrastructure/ConsultaEstadoDbContext.cs` |
| Observabilidad con Prometheus y Grafana | CAR-12 (sección 5.2, ver 14.6) | `SGO-Logic/observabilidad/prometheus.yml`, `SGO-Logic/observabilidad/grafana/provisioning/`, `SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Metricas/MetricasSgo.cs` |
| Borde con YARP e identidad con JWT | Sección 6.1, Borde (ver 14.6); ADR-020 para la validación del JWT en el gateway | `SGO-Logic/src/Services/SgoLogic.ApiGateway/Program.cs`, `SGO-Logic/src/Services/SgoLogic.ApiGateway/appsettings.json`, `SGO-Logic/src/Services/SgoLogic.Auth.Api/Application/JwtService.cs` |
| Esquema de evidencia de verificación | Sin ADR (ver 14.6) | `SGO-Logic/scripts/pruebas/`, `SGO-Logic/scripts/pruebas-carga/`, `SGO-Logic/scripts/pruebas/evidencia/` |

## 14.8 Umbrales del DoD y evidencia

Los umbrales son los de los escenarios de atributos de calidad de la sección 7. Los valores medidos en cada corrida no se registran en este documento: se anotan en la tabla de umbrales de la bitácora del proyecto y, para EAC-01, en `documentacion/docs/PRUEBA-CARGA-EAC01.md`.

| Atributo | Escenario (EAC) | Umbral según DDA sección 7 | Evidencia (ruta en el repositorio) |
|---|---|---|---|
| Disponibilidad | EAC-01 | Cero órdenes perdidas por indisponibilidad del proveedor; 100% de intentos fallidos auditados; disponibilidad del servicio de Pagos igual o superior a 99.9% durante el evento. | `SGO-Logic/scripts/pruebas-carga/eac01-hu023-carga.js`, `SGO-Logic/scripts/pruebas-carga/eac01-hu023-verificar.sh`, `SGO-Logic/scripts/pruebas/evidencia/eac01-carga-2026-09-22.txt`, `SGO-Logic/scripts/pruebas/evidencia/eac01-verificacion-2026-09-22.txt`, `SGO-Logic/scripts/pruebas/evidencia/metricas-ordenes-2026-09-22.txt`, `documentacion/docs/PRUEBA-CARGA-EAC01.md` |
| Rendimiento (consulta) | EAC-02 | Tiempo de respuesta inferior a 5 segundos para el 100% de las consultas, incluso bajo carga pico. | `SGO-Logic/scripts/pruebas-carga/eac02-hu024-carga.js`, `SGO-Logic/scripts/pruebas-carga/eac02-hu024-verificar.sh`, `documentacion/docs/PRUEBA-CARGA-EAC02.md` |
| Rendimiento (auditoría) | EAC-03 | Recuperación en menos de 1 segundo; cero transacciones sin respaldo. | `SGO-Logic/tests/SgoLogic.Auditoria.Tests/CargaTransaccionTests.cs`, `SGO-Logic/tests/SgoLogic.Auditoria.Tests/InsercionYRecuperacionTests.cs`, `documentacion/docs/AUDITORIA.md` |
| Escalabilidad | EAC-04 | Sostiene una demanda de ocho veces sin degradar EAC-01, EAC-02 ni EAC-03; la capacidad base se incrementa año a año dentro del techo operativo sin cambios arquitectónicos. | `deploy/k8s/consulta-estado-hpa.yaml`, `SGO-Logic/scripts/pruebas-carga/eac02-hu024-escalar.sh`, `SGO-Logic/scripts/escalabilidad/docker-compose.scale.yml`, `documentacion/docs/PRUEBA-CARGA-EAC02.md` |
| Consistencia de inventario | EAC-05 | Cero unidades comprometidas a más de un cliente; reservas no concluidas liberadas dentro del tiempo de expiración definido. | `SGO-Logic/tests/SgoLogic.Inventario.IntegrationTests/ReservaConcurrenteTests.cs`, `SGO-Logic/tests/SgoLogic.Inventario.Tests/Application/ServicioExpiracionReservasTests.cs`, `documentacion/docs/CONCURRENCIA-EAC05.md` |
| Crédito | EAC-06 | Consulta local en milisegundos, sin llamada al ERP; ante timeout se conserva PayKlik. | Pruebas de integración del lote ERP -> Crédito y evidencia de Checkout; PENDIENTE DE EVIDENCIA |

La evidencia del flujo completo de la saga, desde la creación de la orden hasta la entrega, se genera con `SGO-Logic/scripts/pruebas/camino-feliz.ps1` y se guarda en `SGO-Logic/scripts/pruebas/evidencia/camino-feliz-2026-09-22.txt`. No corresponde a un escenario de calidad, pero verifica que el ciclo de la orden avanza sin transiciones manuales (ADR-016).

## 14.9 Límites del MVP

Los siguientes límites son alcance deliberado del MVP. Cada uno indica qué existe, qué no se incluye y qué ADR aplica.

| Límite | Qué existe | Qué no se incluye | ADR aplicable |
|---|---|---|---|
| Guía en `GUIA_PENDIENTE` | Cuando LogiExpress no responde, el despacho pasa a `GUIA_PENDIENTE`, se publica `GuiaPendiente` y el pedido se prepara y sale igual. El dominio admite asignar la guía desde ese estado. | Un proceso que reintente automáticamente la solicitud de la guía. Mientras no se asigne, el cliente no recibe el enlace de rastreo. | ADR-017 |
| Compensación explícita de stock | Inventario consume en `inventario.ordenes` los eventos `ordenes.reserva.liberacion-solicitada` y `ordenes.devolucion.registrada`. Una solicitud de liberación devuelve en el momento las unidades de la reserva `ACTIVA` de la orden, la pasa a `LIBERADA` y publica `ReservaInventarioLiberada` (HU-003). Una devolución suma las unidades a la cantidad física de la ubicación de origen de cada línea, con asiento `REINGRESO_DEVOLUCION` en el ledger, y publica `StockReingresado` (HU-014). La expiración por TTL (900 segundos por defecto, `ExpiracionReservasWorker` cada 5 segundos) se mantiene para las reservas que Órdenes no llega a conocer. | Un estado de consumo de la reserva: la de una orden pagada también vence por TTL y devuelve sus unidades al disponible. Idempotencia de negocio del reingreso por `devolucionId`, que depende solo de la deduplicación por `eventId`. Un consumidor en la saga de `inventario.reserva.liberada` o `inventario.stock.reingresado`: solo Auditoría los recibe, por su binding `#`. | [ADR-022](../ADR/ADR-022-compensacion-explicita-de-stock.md), que reemplaza a ADR-021, con ADR-016 y ADR-005 |
| Autenticación en el borde en el entorno Docker Compose | El gateway valida el JWT de Auth cuando `Gateway:ExigirJwt` es `true` y protege las rutas de órdenes, pagos, facturación, inventario, despacho, auditoría y ERP. El servicio `api-gateway` de `SGO-Logic/docker-compose.yml` recibe `Gateway__ExigirJwt: ${GATEWAY_EXIGIR_JWT:-false}` y `Auth__JwtSecret: ${JWT_SECRET}`, y `.env.example` define `GATEWAY_EXIGIR_JWT=false`. | La exigencia encendida por defecto: con el valor de `.env.example`, o sin la variable, el gateway enruta sin validar el token; para encenderla hay que poner `GATEWAY_EXIGIR_JWT=true` en `.env` y recrear `api-gateway`. Los puertos de cada servicio siguen publicados en el host, y quien los use evade el gateway. | ADR-020 |
| Devoluciones de tipo `CAMBIO` | El registro de una devolución total o parcial con su nota de crédito fiscal y su reembolso. El tipo `CAMBIO` registra el SKU de reemplazo y lo transmite en `DevolucionRegistrada`. | La nueva reserva y el despacho del producto de reemplazo. | Sin ADR: es alcance funcional de HU-014 (`contratos/ordenes-eventos-v1.md`, evento 11) |
| Autoescalado en Kubernetes | `deploy/k8s/consulta-estado-hpa.yaml` define el Deployment, el Service y el HPA de Consulta de Estado (2 a 10 réplicas, umbrales de 70% de CPU y 80% de memoria) como evidencia documental de la plataforma de producción. El MVP demuestra réplicas manuales de `consulta-estado-api` balanceadas por el gateway, con `scripts/escalabilidad/docker-compose.scale.yml` y `scripts/pruebas-carga/eac02-hu024-escalar.sh`. | El autoescalado en ejecución y los manifiestos de los demás servicios. El manifiesto existente usa una imagen con etiqueta `latest`, y los secretos (`sgo-secrets`) y el namespace (`sgo-logic`) que referencia no están definidos en el repositorio. | ADR-013 y ADR-001 |
| Notificaciones sin reintento | Notificaciones consume `ordenes.orden.estado-cambiado` (cola `notificaciones.ordenes`) y `despacho.pedido.listo-para-recoger` (cola `notificaciones.despacho`) y envía el aviso al proveedor de mensajería (`mock-mensajeria`, `POST /api/mensajes/email`). Si el proveedor falla, el consumidor registra una advertencia y confirma el mensaje igual: la falla no llega a la saga y la confirmación de la orden no se bloquea, lo que respeta PRE-03. | Un reintento del aviso fallido: el aviso se pierde y solo queda el registro en el log. La base `sgo_notificaciones` solo guarda la tabla de mensajes procesados, así que tampoco queda constancia de la solicitud; esto se aparta de la sección 6.1, que asigna a Notificaciones la propiedad de las solicitudes de notificación. | Sin ADR: aislamiento de fallos de PRE-03 y RES-06 (CU-15, CAR-14) |
| Correo con el enlace de reintento de pago | Cuando el cobro no obtiene respuesta del proveedor, Pagos publica `PagoPendiente` (`pagos.pago.pendiente`) con el enlace de reintento, y Órdenes lo consume para pasar la orden a `PAGO_PENDIENTE`. Al cambiar de estado, Notificaciones envía el aviso genérico de cambio de estado de la orden. | El correo con el enlace de reintento que describe el flujo alternativo de la sección 6.3 (Pagos): Notificaciones no tiene ninguna cola ligada a `pagos.pago.pendiente`, y el aviso genérico que sí envía no incluye el enlace. | Sin ADR: alcance funcional de CU-15 y del flujo alternativo de Pagos (`contratos/pagos-eventos-v1.md`, `PagoPendiente`) |
| Crédito comercial a 30 días | Crédito importa snapshots de historial desde Integración ERP, precalcula la elegibilidad, bifurca la saga después de la reserva y permite consultar/abonar cuentas por cobrar. | No se incluyen cuotas de 3, 6 o 12 meses, intereses, score externo, consulta síncrona al ERP ni crédito en POS salvo que se amplíe el contrato de canales. La evidencia del recorrido productivo mock ERP -> Integración ERP -> Crédito queda pendiente de ejecución. | ADR-023; contrato `documentacion/docs/contratos/credito-eventos-v1.md` |

---

*Fin del documento.*
