# Catálogo de Evidencia — Proyecto Fase 1: SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC  
**Documento:** Insumo base del Documento de Definición de Arquitectura (DDA)  
**Elaborado por:** Angel Martinez (202300410) 

---

## 1. Propósito de este documento

Este catálogo extrae y numera toda la evidencia contenida en el enunciado del caso MegaStore. No contiene decisiones de diseño ni supuestos del equipo: cada fila es un hecho declarado por el cliente.

Su función es servir como **fuente única de referencia** para el resto del DDA. Ningún stakeholder, driver, escenario de calidad, restricción o decisión arquitectónica debe aparecer en el documento final sin apuntar a por lo menos un identificador `EV-##` de esta tabla. Esa es la condición que hace que las matrices de trazabilidad se puedan construir de forma mecánica al final, en lugar de reconstruirse a mano.

La columna "Implicación arquitectónica" sí es interpretación del equipo. Se incluye para acelerar la derivación de drivers, pero es discutible y debe validarse.

---

## 2. Convención de identificadores del proyecto

Todos los integrantes deben usar esta nomenclatura desde el primer día. Si cada quien numera distinto, las matrices de trazabilidad no cruzan.

| Prefijo | Artefacto | Responsable de asignar |
|---|---|---|
| `EV-##` | Evidencia del enunciado | Angel |
| `STK-##` | Stakeholder | Angel |
| `PRE-##` | Preocupación arquitectónica | Angel |
| `CAR-##` | Característica priorizada del sistema | Angel |
| `CUN-##` | Caso de uso de negocio | Angel |
| `CU-##` | Caso de uso expandido del sistema | Jonathan / Angel |
| `DRF-##` | Driver de requisito funcional | Angel / Jonathan |
| `EAC-##` | Escenario de atributo de calidad | Jose / Julio |
| `RES-##` | Driver de restricción | Francisco / Julio |
| `ADR-###` | Architecture Decision Record | Quien toma la decisión |
| `RSG-##` | Riesgo técnico identificado | Transversal |

*Nota: el prefijo originalmente previsto para el caso de uso expandido del sistema era `CDU-##`. Se oficializa `CU-##` como el prefijo vigente porque es el que ya está instanciado (15 fichas, CU-01 a CU-15, en `DDA_04_Drivers_Funcionales_CDU.md`); `CDU-##` queda descartado para no numerar el mismo artefacto con dos esquemas distintos.*

Formato sugerido de referencia cruzada dentro del DDA: `(EV-15, EV-16)` entre paréntesis al final de la afirmación que sustenta.

---

## 3. Contexto organizacional

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-01 | MegaStore es una cadena de tiendas por departamentos con 25 años de presencia en Guatemala, El Salvador y Costa Rica: 32 tiendas físicas, 6,500 empleados, facturación anual de USD 450 millones. | Sistema multi-país y multi-tienda. Obliga a considerar zonas horarias, monedas y normativa fiscal por país desde el modelo de dominio. | Contexto | Angel |
| EV-02 | El comercio electrónico representa el 25% de las ventas y crece aproximadamente 15% cada año. | El canal digital ya es material, no piloto. La arquitectura debe dimensionarse para un crecimiento sostenido, no solo para el pico estacional. | Contexto / EAC | Angel |
| EV-03 | Comercializa línea blanca, electrónica, ropa y calzado, con promociones estacionales, combos y financiamiento a 3, 6 y 12 meses. | El catálogo y el motor de precios/promociones tienen complejidad propia (combos, planes de financiamiento). Es un contexto delimitado candidato. | DRF | Angel |
| EV-04 | La Dirección desea que un cliente pueda iniciar, continuar y completar una compra desde una tienda, la web, una aplicación móvil o el centro de atención, con experiencia consistente sin importar el canal. | Objetivo estratégico raíz del proyecto. Exige identidad de cliente unificada y estado de compra compartido entre canales. | DRF / Objetivo | Angel |
| EV-05 | Opera dos centros de distribución, pero no dispone de una capacidad centralizada que presente existencias, ubicaciones, reservas, entradas y salidas de ambos centros y de las tiendas como una sola visión. | Justifica un contexto de Inventario Unificado como componente del backbone, con las tiendas y los CD como ubicaciones lógicas de un mismo modelo. | DRF | Angel |
| EV-06 | No cuenta con una herramienta integrada para que el centro de atención consulte el historial completo de compras, devoluciones e incidencias. | Justifica un modelo de lectura consolidado (vista 360 del cliente/orden) separado del modelo transaccional. | DRF | Angel |

---

## 4. Situación actual y costos del problema

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-07 | Las aplicaciones y procedimientos fueron incorporados en momentos distintos y no comparten una visión unificada; cada tienda conserva información local de ventas e inventario y el comercio electrónico opera con información separada. | Diagnóstico de silos. Sustenta la necesidad de un sistema de registro único para orden e inventario, con los sistemas existentes degradados a consumidores o adaptadores. | Contexto | Angel |
| EV-08 | Cuando se recibe una compra en línea, el personal de logística llama por teléfono a cada tienda para confirmar existencia. | Proceso manual crítico a eliminar. Es la justificación de negocio más directa del inventario en tiempo real. | DRF | Angel |
| EV-09 | Las devoluciones y los cambios pueden tardar hasta siete días hábiles porque el personal de atención no dispone de una vista consolidada de la compra, el pago, el despacho y la entrega. | Métrica de línea base para postventa. Refuerza la vista 360 (EV-06) y el modelo de lectura consolidado. | DRF | Angel |
| EV-10 | Finanzas recibe archivos de hojas de cálculo de distintos canales y tiendas para conciliar ventas; las diferencias se investigan manualmente y pueden retrasar el cierre y los reportes gerenciales. | Justifica automatizar la sincronización hacia el ERP y llevar un registro conciliable por origen. | DRF / EAC | Angel |
| EV-11 | Durante el último Black Friday la pasarela de pagos permaneció indisponible durante 45 minutos, provocando pérdidas estimadas en USD 150,000 en ventas. | Evidencia cuantificada del riesgo de dependencia externa. Es el argumento económico que justifica desacoplar el cobro del cierre de la orden. | EAC | Jose |
| EV-12 | Cada envío incorrecto genera un costo aproximado de USD 25 por logística inversa; el jefe de logística estableció como objetivo reducir estos errores en un 70%. | Meta medible de negocio. Debe poder rastrearse hasta un mecanismo concreto (asignación de origen de despacho basada en inventario confiable). | DRF / EAC | Angel |

---

## 5. Sistemas existentes y restricciones de integración

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-13 | El ERP es la plataforma contable-financiera, construida sobre AS/400 en la década de 1990; participa en contabilidad, facturación fiscal y cierre mensual. | Sistema legacy con modelo de datos y semántica propios. Requiere capa anticorrupción para no contaminar el dominio nuevo. | RES | Francisco |
| EV-14 | La Junta Directiva determinó que el ERP no será reemplazado ni modificado dentro del horizonte de esta iniciativa. | Restricción dura no negociable. Elimina de raíz cualquier alternativa que implique tocar el ERP. | RES | Francisco |
| EV-15 | El mecanismo disponible del ERP para recibir resúmenes de ventas son archivos CSV o XML con capacidad de ser procesados en intervalos de 30 minutos. | Integración batch, no en línea. Fija la consistencia contable como eventual con ventana de 30 minutos y obliga a un adaptador de exportación con control de lotes. | RES | Francisco |
| EV-16 | Cada una de las 32 tiendas usa una instancia local de POS; no forman parte de un programa de reemplazo y se espera que puedan recibir eventos desde SGO-Logic, incorporando manejo adecuado de errores y reintentos para garantizar la confiabilidad del proceso. | El POS es consumidor de eventos, no fuente. Exige entrega confiable (reintentos, idempotencia, cola de mensajes fallidos) hacia 32 destinos posiblemente intermitentes. | RES / EAC | Francisco |
| EV-17 | PayKlik procesa pagos de tarjeta de crédito o débito, con contrato vigente por tres años e interfaz REST de latencia cercana a 800 ms, que ha alcanzado cinco segundos en periodos de alta demanda. | Dependencia externa lenta y variable. No puede invocarse de forma bloqueante dentro de una transacción de negocio. | RES / EAC | Francisco |
| EV-18 | LogiExpress genera guías y comunica estados de entrega para Centroamérica, con contrato vigente por dos años e interfaz REST; aproximadamente una de cada 50 solicitudes termina por timeout. | Tasa de fallo del 2% declarada. Obliga a reintentos con respaldo y a un estado de "guía pendiente" que no bloquee el despacho. | RES / EAC | Francisco |
| EV-19 | Es obligatoria la emisión de facturas enviadas a la SAT por medio de XML antes de finalizar la orden; el servicio presenta tiempos de respuesta variables y exige que cada documento conserve su CUI y serie autorizada. | Punto de mayor tensión del caso: obligación fiscal sincrónica frente a un servicio de latencia impredecible. Requiere definir con precisión qué significa "finalizar la orden". | RES / DRF | Francisco |
| EV-20 | Se utiliza un proveedor externo para SMS, correo y notificaciones push, cuya disponibilidad se encuentra fuera del control de la organización. | Las notificaciones deben ser estrictamente asíncronas y su falla no puede propagarse al flujo principal. | RES | Francisco |

---

## 6. Operación omnicanal esperada

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-21 | Una compra puede iniciar en un canal y finalizar en otro; se requiere reconocer al cliente durante todo el proceso y conservar la continuidad de su intención de compra. | Exige identidad de cliente unificada y persistencia del estado de compra fuera de la sesión del canal. | DRF | Angel |
| EV-22 | Antes de confirmar una venta debe verificarse la disponibilidad de los productos para evitar que una misma unidad sea comprometida a más de un cliente. | Único punto del caso que exige consistencia fuerte. Define el mecanismo de reserva de inventario como núcleo transaccional. | DRF / EAC | Angel |
| EV-23 | Cuando una compra no concluye, el inventario reservado debe liberarse en un tiempo acorde con la operación comercial. | Reservas con vencimiento. Requiere un mecanismo de expiración temporal y define un parámetro de negocio a confirmar con el cliente. | DRF | Angel |
| EV-24 | La empresa acepta pagos mediante tarjetas, transferencias bancarias y billeteras digitales. | Múltiples medios de pago con flujos distintos. Sustenta un contexto de Pagos con estrategia por medio. | DRF | Angel |
| EV-25 | Por políticas corporativas y de cumplimiento, no se almacenan números completos de tarjeta ni códigos CVV. | Restricción de seguridad y cumplimiento. Obliga a tokenización y descarta construir un procesador propio de tarjeta. | RES | Francisco |
| EV-26 | Cada intento de cobro debe quedar relacionado con la orden, el canal de origen, la fecha y su resultado. | Registro de intentos, no solo del resultado final. Empuja hacia un modelo de eventos de pago inmutables. | DRF | Angel |
| EV-27 | Para el próximo Black Friday se espera una demanda de hasta ocho veces superior a la de un día normal. | Factor de escalamiento declarado. Es el insumo directo para dimensionar los escenarios de carga. | EAC | Jose |
| EV-28 | La organización busca no perder la orden si el proveedor falla al procesar el pago, poder procesar pagos sin depender del proveedor externo, y que cada intento fallido pueda ser auditado. | Exige desacoplar la captura del cobro. Nota: combinada con EV-25, esta evidencia no puede interpretarse como construir un procesador propio, sino como rutas de pago alternativas y captura diferida de intentos. | EAC / DRF | Jose |
| EV-29 | Una vez confirmada la venta, el proceso debe generar la documentación fiscal correspondiente y mantener la relación entre la venta original, las devoluciones y los reembolsos. | Trazabilidad documental encadenada. El modelo debe conservar referencias entre documentos fiscales relacionados. | DRF | Angel |
| EV-30 | Debido a las auditorías anuales de la SAT, la directora de Finanzas requiere reconstruir quién ejecutó cada operación, cuándo ocurrió y qué cambios se realizaron; esperan recuperar la evidencia de cualquier transacción en menos de un segundo y sincronizar las ventas con el ERP, ya que una transacción sin respaldo puede derivar en multas o inconsistencias fiscales. | Auditoría reconstructiva con presupuesto de latencia explícito. Empuja hacia un registro append-only indexado, no hacia tablas mutables con bitácora. | EAC | Jose |
| EV-31 | El despacho puede realizarse desde un centro de distribución, una tienda física o mediante recogida por el cliente, y el método de entrega puede modificarse incluso después de confirmada la compra. | La orden es un agregado de larga vida y mutable después de confirmada. Refuerza la necesidad de orquestación con compensación. | DRF | Angel |
| EV-32 | Logística necesita conocer oportunamente qué pedidos preparar, desde qué ubicación despacharlos y a quién entregarlos, además de consultar la disponibilidad del inventario en tiempo real. | Vista operativa dedicada para logística, alimentada por eventos del ciclo de vida de la orden. | DRF | Angel |
| EV-33 | Los clientes y el personal de atención esperan consultar el estado de cualquier orden desde distintos dispositivos; consultas que superen los cinco segundos son inaceptables. | Presupuesto de latencia de consulta explícito. Justifica separar el modelo de lectura del modelo de escritura. | EAC | Jose |
| EV-34 | El cliente debe recibir notificaciones sobre confirmación, preparación, tránsito, entrega o incidencias; sin embargo, una falla en los canales de comunicación no debe impedir que la operación principal continúe. | El cliente autoriza explícitamente el desacople. Es la evidencia que respalda priorizar notificaciones al final del backbone. | DRF / EAC | Angel |
| EV-35 | Se contará con un carrito de compras unificado entre todos los canales, y el cliente podrá disponer de diferentes métodos de pago según el canal de compra. | Componente central del backbone. El carrito es estado compartido, no estado de sesión del canal. | DRF | Angel |

---

## 7. Stakeholders y posiciones declaradas

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-36 | La directora de comercialización indicó que el cliente compra desde el celular a las 10 pm, desde la tienda física a las 3 pm o llama por teléfono a las 8 am, que la experiencia debe ser la misma y que no pueden tener silos. | Confirma el objetivo omnicanal desde la voz del negocio. Stakeholder sin nombre propio en el enunciado. | STK / PRE | Angel |
| EV-37 | Carlos Méndez, Gerente de TI, será responsable de operar la solución. Su preocupación principal es la continuidad del negocio durante eventos de alta demanda; rechaza propuestas que requieran modificar el ERP-Core o los POS existentes; considera indispensable que la arquitectura pueda ser mantenida por el equipo interno; recalcó soportar los picos de demanda y tener monitoreo constante de los sistemas externos. | Stakeholder con poder de rechazo técnico. Sus tres preocupaciones (continuidad, no tocar legacy, mantenibilidad interna) restringen fuertemente el espacio de alternativas. | STK / PRE / RES | Angel |
| EV-38 | Ana Lucía Fuentes, Directora de Finanzas, posee autoridad de veto sobre cualquier decisión que reduzca la trazabilidad de las operaciones o comprometa el cumplimiento fiscal. | Stakeholder con veto formal. Cualquier optimización de rendimiento que sacrifique registro de auditoría es inviable. Fuente del conflicto central con EV-37. | STK / PRE | Angel |
| EV-39 | Roberto Quezada, Jefe de Logística, prioriza la reducción de errores de despacho y la visibilidad oportuna de las órdenes. | Stakeholder operativo. Sus métricas se conectan directamente con EV-12 y EV-32. | STK / PRE | Angel |
| EV-40 | La Junta Directiva determinó la permanencia del ERP-Core y del POS, y esa decisión permanece vigente. | Stakeholder de gobierno. Origen de las restricciones EV-14 y EV-16. | STK / RES | Angel |
| EV-41 | La Gerencia aprobó un presupuesto total de USD 150,000. | Límite de inversión inicial. Cada alternativa debe explicar su inversión. | RES | Julio |

---

## 8. Restricciones económicas, técnicas, temporales y regulatorias

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-42 | Después de la puesta en operación, la infraestructura no debe superar USD 1,200 mensuales sin justificación y aprobación adicional; cada alternativa deberá explicar tanto su inversión como su costo operativo. | Techo operativo estrecho. Descarta topologías con muchos componentes de infraestructura licenciados o con alta huella de cómputo permanente. | RES | Julio |
| EV-43 | El Black Friday ocurrirá en noviembre y la fecha comercial no puede trasladarse; la organización espera contar durante el primer mes con una ruta viable para proteger los procesos asociados a pagos y facturación. | Restricción temporal dura y criterio de priorización explícito del cliente: pagos y facturación primero. | RES / CAR | Julio |
| EV-44 | La solución deberá operar conforme a la normativa de protección de datos aplicable y a las disposiciones de facturación electrónica de la SAT. | Cumplimiento regulatorio en tres países. Afecta retención, cifrado y residencia de datos personales. | RES | Francisco |
| EV-45 | Los contratos vigentes con PayKlik y LogiExpress deben respetarse. | Impide reemplazar proveedores dentro del horizonte. Cualquier redundancia debe ser aditiva, no sustitutiva. | RES | Francisco |
| EV-46 | El equipo interno tiene experiencia principalmente en Java y .NET y conocimientos básicos de Docker; una alternativa que requiera competencias distintas deberá incluir impacto de adopción, capacitación y operación. | Restricción de capacidad organizacional. Es el argumento decisivo contra tecnologías exóticas y se conecta con la preocupación de mantenibilidad de EV-37. | RES | Julio |
| EV-47 | La organización desea minimizar el consumo energético de la infraestructura y mantener la facturación totalmente electrónica. | Criterio de eficiencia que penaliza el sobredimensionamiento permanente y favorece escalamiento bajo demanda. | RES | Julio |
| EV-48 | El despliegue inicial se realizará en infraestructura local (on-premises) para aprovechar la inversión existente, mantener control operativo durante la adopción y reducir el riesgo de transición de los sistemas legacy. | Fija la topología inicial. Restringe el uso de servicios administrados y obliga a operar el middleware internamente. | RES | Julio |
| EV-49 | La arquitectura deberá permitir una migración planificada hacia la nube conforme aumenten la demanda y la operación regional, para facilitar la elasticidad ante eventos como Black Friday, mejorar las opciones de recuperación y evitar que el crecimiento dependa de ampliar permanentemente la capacidad física instalada. | Requisito de portabilidad diferida. Favorece empaquetado en contenedores y aislamiento de dependencias de infraestructura detrás de interfaces. | EAC / RES | Julio |

---

## 9. Restricciones de proceso y entrega

| ID | Evidencia | Implicación arquitectónica | Driver candidato | Responsable |
|---|---|---|---|---|
| EV-50 | Se debe usar UML 2.5.1 cuando corresponda, con declaración explícita de cualquier notación complementaria. | Si se usa notación C4 o similar para el diagrama de bloques, debe declararse explícitamente en el DDA. | RES | Angel |
| EV-51 | El tablero Kanban debe manejarse con seis columnas obligatorias (To Do, Blocked, In Progress, Ready for Testing, Test/QA, Done) y priorizar los componentes base de la arquitectura antes que las funcionalidades cosméticas. | El backlog es de componentes, patrones y decisiones, no de funcionalidades. Alinea con la priorización de características. | RES | Jose |
| EV-52 | Toda la evolución del código y la configuración de infraestructura debe estar centralizada en un repositorio privado de GitHub con la nomenclatura oficial indicada. | Artefacto de gestión obligatorio. Verificar el año correcto de la nomenclatura antes de crear el repositorio. | RES | Jose |
| EV-53 | Los ADR se utilizarán como registro vivo para el refinamiento continuo de las decisiones, y los diagramas constituirán vistas del DDA. | Define la relación entre artefactos: los diagramas no son anexos independientes sino vistas referenciadas desde el DDA. | RES | Angel |



