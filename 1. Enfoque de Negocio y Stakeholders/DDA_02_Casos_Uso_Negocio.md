# Caso de Negocio: Casos de Uso de Negocio y Primera Descomposición — SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC  
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)  
**Elaborado por:** Angel Martinez (202300410) 

---

## 1. Propósito y declaración de notación

Este apartado identifica el caso de negocio de MegaStore mediante casos de uso de negocio (CUN) de alto nivel y su primera descomposición, y delimita el sistema de interés.

**Declaración de notación (requerida por EV-50):** se utiliza UML 2.5.1 con las extensiones de modelado de negocio, aplicando los estereotipos siguientes:

| Estereotipo | Significado | Representación |
|---|---|---|
| `<<actor de negocio>>` | Entidad externa al negocio que interactúa con él y obtiene o aporta valor. | Actor fuera del límite del negocio |
| `<<trabajador de negocio>>` | Rol interno de MegaStore que participa en la ejecución de un proceso. | Actor dentro del límite del negocio |
| `<<caso de uso de negocio>>` | Proceso de negocio que produce un resultado observable de valor. | Elipse dentro del límite del negocio |

Los CUN describen **qué hace el negocio**, con independencia de si el proceso está automatizado. La delimitación de qué parte de cada CUN asume SGO-Logic se declara en la sección 6. Los diagramas se expresan en PlantUML, que genera notación UML estándar.

---

## 2. Actores de negocio y trabajadores de negocio

### 2.1 Actores de negocio (externos)

| ID | Actor | Stakeholder asociado | Papel en el negocio | Evidencia |
|---|---|---|---|---|
| AN-01 | Cliente | STK-13 | Inicia, continúa y concluye compras desde cualquier canal; solicita devoluciones y consulta estados. | EV-04, EV-21, EV-33 |
| AN-02 | SAT | STK-14 | Recibe y certifica la documentación fiscal; audita las operaciones. | EV-19, EV-30 |
| AN-03 | PayKlik | STK-15 | Procesa los cobros con tarjeta de crédito o débito. | EV-17 |
| AN-04 | LogiExpress | STK-16 | Genera guías y comunica estados de entrega en Centroamérica. | EV-18 |
| AN-05 | Proveedor de notificaciones | STK-17 | Entrega mensajes por SMS, correo y push. | EV-20 |

### 2.2 Trabajadores de negocio (internos)

| ID | Trabajador | Stakeholder asociado | Papel en el negocio | Evidencia |
|---|---|---|---|---|
| TN-01 | Vendedor de tienda | STK-09 | Registra ventas presenciales y atiende recogidas en tienda. | EV-16, EV-07 |
| TN-02 | Agente del centro de atención | STK-10 | Atiende compras y postventa por teléfono; consulta historial. | EV-06, EV-09 |
| TN-03 | Operador de logística y centro de distribución | STK-11 | Prepara, despacha y da seguimiento a los pedidos. | EV-08, EV-32 |
| TN-04 | Analista de Finanzas | STK-12 | Concilia ventas y participa en el cierre contable. | EV-10, EV-15 |
| TN-05 | Responsable de catálogo y comercialización | STK-06 | Define catálogo, precios, combos, promociones y planes de financiamiento. | EV-03 |

---

## 3. Casos de uso de negocio de alto nivel

Los CUN se derivan de la cadena de valor observada en el enunciado: captar la demanda, comprometer inventario, cobrar, facturar, entregar, atender postventa y conciliar.

| ID | Caso de uso de negocio | Trabajadores y actores participantes | Valor producido | Evidencia principal |
|---|---|---|---|---|
| CUN-01 | Gestionar venta omnicanal | AN-01, TN-01, TN-02 | Una orden confirmada, iniciada en cualquier canal y concluida en cualquier otro, con inventario comprometido de forma confiable. | EV-04, EV-21, EV-22, EV-35 |
| CUN-02 | Administrar inventario unificado | TN-01, TN-03 | Una única visión de existencias, ubicaciones, reservas, entradas y salidas de tiendas y centros de distribución. | EV-05, EV-08, EV-22, EV-23 |
| CUN-03 | Gestionar cobro | AN-01, AN-03 | Un cobro aplicado y auditado, o una orden preservada cuando el cobro no puede completarse. | EV-24, EV-26, EV-28 |
| CUN-04 | Emitir documentación fiscal | AN-02, TN-04 | Documentos fiscales válidos, certificados y relacionados con la venta original, sus devoluciones y reembolsos. | EV-19, EV-29 |
| CUN-05 | Ejecutar despacho y entrega | AN-01, AN-04, TN-01, TN-03 | El producto entregado desde el origen correcto, con seguimiento del estado hasta la recepción del cliente. | EV-31, EV-32, EV-12 |
| CUN-06 | Atender postventa | AN-01, TN-02 | Devoluciones, cambios e incidencias resueltos con una vista consolidada de la compra. | EV-06, EV-09, EV-29 |
| CUN-07 | Conciliar y cerrar contablemente | TN-04, AN-02 | Ventas conciliadas, sincronizadas con el ERP y con evidencia recuperable para auditoría. | EV-10, EV-15, EV-30 |
| CUN-08 | Administrar catálogo, precios y promociones | TN-05 | Catálogo, combos, promociones y planes de financiamiento disponibles de forma consistente en todos los canales. | EV-03, EV-35 |
| CUN-09 | Comunicar al cliente el estado de su orden | AN-01, AN-05 | El cliente informado sobre confirmación, preparación, tránsito, entrega o incidencias. | EV-34, EV-20 |

### 3.1 Diagrama de casos de uso de negocio de alto nivel



**Nota sobre las relaciones:** CUN-09 se modela como `<<extend>>` y no como `<<include>>` de forma deliberada. El enunciado establece que una falla en los canales de comunicación no debe impedir que la operación principal continúe (EV-34), lo que significa que notificar no es parte obligatoria del proceso de venta. Esta decisión de modelado es la traducción directa de PRE-03 y debe sostenerse en la defensa.

---

## 4. Primera descomposición

Cada CUN de alto nivel se descompone en los procesos que lo conforman. Este nivel es el que alimenta la construcción de los casos de uso expandidos del sistema (CDU), responsabilidad de Jonathan.

### CUN-01 Gestionar venta omnicanal

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-01.1 | Reconocer al cliente en cualquier canal | Identificar al cliente y recuperar su contexto de compra sin importar desde qué canal se presenta. | EV-21, EV-36 |
| CUN-01.2 | Mantener el carrito unificado | Conservar la intención de compra de manera continua entre canales y dispositivos. | EV-21, EV-35 |
| CUN-01.3 | Verificar disponibilidad y reservar inventario | Comprometer las unidades antes de confirmar la venta, evitando que una unidad se asigne a más de un cliente. | EV-22 |
| CUN-01.4 | Confirmar la orden | Cerrar la intención de compra y dar inicio al cobro, la facturación y el despacho. | EV-04, EV-29 |
| CUN-01.5 | Modificar una orden confirmada | Permitir cambios posteriores a la confirmación, en particular del método de entrega. | EV-31 |
| CUN-01.6 | Liberar reservas de compras no concluidas | Devolver al inventario disponible las unidades reservadas por compras abandonadas. | EV-23 |

### CUN-02 Administrar inventario unificado

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-02.1 | Consolidar existencias de tiendas y centros de distribución | Presentar como una sola visión las existencias de las 32 tiendas y los 2 centros de distribución. | EV-05, EV-07 |
| CUN-02.2 | Registrar entradas y salidas | Incorporar movimientos de inventario originados en ventas presenciales, despachos, recepciones y devoluciones. | EV-05, EV-16 |
| CUN-02.3 | Gestionar reservas y su vencimiento | Administrar el ciclo de vida de las reservas, incluida su expiración automática. | EV-22, EV-23 |
| CUN-02.4 | Consultar disponibilidad en tiempo real | Responder consultas de disponibilidad por ubicación para venta y logística. | EV-08, EV-32 |

### CUN-03 Gestionar cobro

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-03.1 | Ofrecer medios de pago según el canal | Presentar tarjeta, transferencia bancaria o billetera digital según corresponda al canal de compra. | EV-24, EV-35 |
| CUN-03.2 | Procesar un intento de cobro | Ejecutar el cobro contra el medio seleccionado. | EV-24, EV-17 |
| CUN-03.3 | Registrar y auditar cada intento | Dejar constancia del intento con su orden, canal de origen, fecha y resultado, incluidos los fallidos. | EV-26, EV-28 |
| CUN-03.4 | Preservar la orden ante indisponibilidad del proveedor | Conservar la orden y habilitar una ruta alternativa cuando el proveedor externo no responde. | EV-28, EV-11 |
| CUN-03.5 | Procesar reembolsos | Devolver montos asociados a devoluciones, manteniendo la relación con la venta original. | EV-29 |

### CUN-04 Emitir documentación fiscal

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-04.1 | Generar el documento fiscal de la venta | Construir el XML correspondiente conservando CUI y serie autorizada. | EV-19 |
| CUN-04.2 | Certificar el documento ante la SAT | Enviar el documento y obtener su certificación antes de finalizar la orden. | EV-19 |
| CUN-04.3 | Emitir documentos por devolución o reembolso | Generar los documentos fiscales asociados a la reversión de una venta. | EV-29 |
| CUN-04.4 | Conservar y relacionar la evidencia fiscal | Mantener la trazabilidad entre venta original, devoluciones, reembolsos y sus documentos. | EV-29, EV-30 |

### CUN-05 Ejecutar despacho y entrega

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-05.1 | Determinar el origen del despacho | Seleccionar si el pedido sale de un centro de distribución, de una tienda o queda para recogida. | EV-31, EV-32 |
| CUN-05.2 | Preparar el pedido | Alistar físicamente el pedido en la ubicación de origen determinada. | EV-32 |
| CUN-05.3 | Coordinar la guía con el proveedor logístico | Solicitar la guía y transmitir los datos de entrega. | EV-18 |
| CUN-05.4 | Atender la recogida en tienda | Entregar el pedido al cliente en el punto físico seleccionado. | EV-31 |
| CUN-05.5 | Dar seguimiento al estado de entrega | Incorporar los estados comunicados por el proveedor hasta la entrega final. | EV-18, EV-34 |

### CUN-06 Atender postventa

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-06.1 | Consultar el historial consolidado | Acceder a compras, pagos, despachos, entregas, devoluciones e incidencias en una sola vista. | EV-06, EV-09 |
| CUN-06.2 | Registrar una devolución o cambio | Iniciar el proceso de reversión total o parcial de una venta. | EV-09, EV-29 |
| CUN-06.3 | Reingresar el producto al inventario | Devolver las unidades a la ubicación correspondiente. | EV-05 |
| CUN-06.4 | Gestionar incidencias | Registrar y dar seguimiento a problemas reportados sobre una orden. | EV-34, EV-06 |

### CUN-07 Conciliar y cerrar contablemente

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-07.1 | Consolidar resúmenes de ventas por canal y tienda | Agrupar las ventas de todos los canales en un resumen único por periodo. | EV-10, EV-15 |
| CUN-07.2 | Transferir los resúmenes al ERP | Entregar los archivos CSV o XML en la ventana de 30 minutos que admite el ERP. | EV-15 |
| CUN-07.3 | Conciliar diferencias | Detectar y explicar diferencias entre lo registrado y lo transferido, sin investigación manual. | EV-10 |
| CUN-07.4 | Reconstruir evidencia para auditoría | Recuperar quién ejecutó cada operación, cuándo y qué cambió. | EV-30 |

### CUN-08 Administrar catálogo, precios y promociones

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-08.1 | Publicar el catálogo unificado por canal | Mantener una definición consistente de productos disponible en todos los canales. | EV-03, EV-35 |
| CUN-08.2 | Aplicar promociones, combos y financiamiento | Resolver el precio final considerando promociones estacionales, combos y planes a 3, 6 y 12 meses. | EV-03 |

### CUN-09 Comunicar al cliente el estado de su orden

| ID | Subproceso | Descripción | Evidencia |
|---|---|---|---|
| CUN-09.1 | Emitir notificaciones del ciclo de vida de la orden | Informar confirmación, preparación, tránsito, entrega o incidencias. | EV-34 |
| CUN-09.2 | Atender la consulta de estado bajo demanda | Permitir que cliente y agente consulten el estado desde distintos dispositivos. | EV-33 |

---

## 5. Descripción textual de los casos de uso de negocio principales

Se documentan aquí los tres CUN que concentran el valor y el riesgo del caso. Los restantes siguen la misma estructura y se completan en la versión 1.0.

### CUN-01 Gestionar venta omnicanal

| Campo | Contenido |
|---|---|
| **Objetivo** | Permitir que un cliente inicie, continúe y concluya una compra desde cualquier canal, con inventario comprometido de forma confiable y experiencia equivalente. |
| **Actor primario** | Cliente (AN-01) |
| **Participantes** | Vendedor de tienda (TN-01), Agente del centro de atención (TN-02), PayKlik (AN-03), SAT (AN-02) |
| **Disparador** | El cliente manifiesta intención de compra en cualquier canal. |
| **Precondición de negocio** | El catálogo y los precios vigentes están publicados; existe inventario registrado en alguna ubicación. |
| **Flujo de negocio** | Se reconoce al cliente; se conforma el carrito; se verifica disponibilidad y se reserva; se confirma la orden; se ejecuta el cobro; se emite la documentación fiscal; se dispara el despacho. |
| **Resultado esperado** | Orden confirmada, inventario comprometido, cobro registrado y documento fiscal emitido. |
| **Resultado alternativo** | Si el cobro no puede completarse, la orden se preserva y queda pendiente por una ruta alternativa (EV-28). Si la compra no concluye, la reserva expira y el inventario se libera (EV-23). |
| **Preocupaciones asociadas** | PRE-09, PRE-13, PRE-01, PRE-02 |

### CUN-03 Gestionar cobro

| Campo | Contenido |
|---|---|
| **Objetivo** | Aplicar el cobro de una orden dejando constancia auditable de cada intento, sin perder la orden ante fallas del proveedor. |
| **Actor primario** | Cliente (AN-01) |
| **Participantes** | PayKlik (AN-03) |
| **Disparador** | Confirmación de una orden o solicitud de reembolso. |
| **Precondición de negocio** | Existe una orden con medio de pago seleccionado según el canal. |
| **Flujo de negocio** | Se selecciona el medio de pago; se ejecuta el intento; se registra el resultado; si el proveedor no responde, se preserva la orden y se habilita la ruta alternativa. |
| **Resultado esperado** | Cobro aplicado y registrado con su orden, canal, fecha y resultado. |
| **Resultado alternativo** | Orden en estado de pago pendiente, con todos los intentos registrados para auditoría. |
| **Restricción relevante** | No se almacenan números completos de tarjeta ni códigos CVV (EV-25), lo que descarta interpretar EV-28 como la construcción de un procesador propio. |
| **Preocupaciones asociadas** | PRE-02, PRE-05, PRE-08 |

### CUN-07 Conciliar y cerrar contablemente

| Campo | Contenido |
|---|---|
| **Objetivo** | Sincronizar las ventas con el ERP y conciliar diferencias sin intervención manual, conservando evidencia auditable. |
| **Actor primario** | Analista de Finanzas (TN-04) |
| **Participantes** | SAT (AN-02) |
| **Disparador** | Vencimiento de la ventana de consolidación o inicio del cierre mensual. |
| **Precondición de negocio** | Existen ventas registradas en el periodo. |
| **Flujo de negocio** | Se consolidan los resúmenes por canal y tienda; se transfieren al ERP en el formato admitido; se concilian diferencias; se conserva la evidencia. |
| **Resultado esperado** | ERP actualizado y diferencias explicadas de forma automática. |
| **Restricción relevante** | El ERP solo acepta archivos CSV o XML procesables en intervalos de 30 minutos (EV-15), lo que establece consistencia eventual con el contexto contable. |
| **Preocupaciones asociadas** | PRE-11, PRE-12, PRE-07 |

---

## 6. Delimitación del sistema de interés

Esta sección es la frontera del proyecto. Declara qué parte de cada proceso de negocio asume SGO-Logic y qué permanece fuera. Es el insumo directo del Diagrama de Contexto (responsable: Francisco) y del Diagrama de Bloques.

| CUN | Alcance en SGO-Logic | Justificación |
|---|---|---|
| CUN-01 Gestionar venta omnicanal | **Completo** | Es el objetivo estratégico declarado (EV-04). SGO-Logic es el sistema de registro de la orden. |
| CUN-02 Administrar inventario unificado | **Completo** | La capacidad centralizada no existe hoy (EV-05) y es condición para eliminar las llamadas telefónicas (EV-08). |
| CUN-03 Gestionar cobro | **Completo en la orquestación, delegado en la ejecución** | La ejecución del cobro con tarjeta permanece en PayKlik por contrato vigente (EV-45). SGO-Logic orquesta, registra y audita. |
| CUN-04 Emitir documentación fiscal | **Completo en la generación y certificación** | El registro contable del documento permanece en el ERP (EV-13), que no se modifica (EV-14). |
| CUN-05 Ejecutar despacho y entrega | **Completo en la decisión y el seguimiento** | El transporte físico y la generación de guías permanecen en LogiExpress (EV-18, EV-45). |
| CUN-06 Atender postventa | **Completo** | Requiere la vista consolidada que hoy no existe (EV-06, EV-09). |
| CUN-07 Conciliar y cerrar contablemente | **Parcial** | SGO-Logic consolida, transfiere y concilia. El cierre contable permanece en el ERP por decisión de la Junta (EV-14). |
| CUN-08 Administrar catálogo, precios y promociones | **Parcial, a confirmar** | El enunciado no indica dónde reside hoy la gestión de catálogo y promociones. Se declara como vacío de información. |
| CUN-09 Comunicar al cliente | **Completo en la orquestación, delegado en la entrega** | El envío efectivo depende de un proveedor cuya disponibilidad está fuera de control (EV-20). |

**Vacío de información adicional detectado:** `VAC-09` — El enunciado no especifica qué sistema administra hoy el catálogo, los precios, los combos y los planes de financiamiento (EV-03), ni si SGO-Logic debe asumirlo o consumirlo. La respuesta cambia el alcance de CUN-08 y afecta el Diagrama de Contexto. Debe consultarse al cliente. Este vacío se agrega al catálogo de la sección 10 de `DDA_00`.

---

## 7. Trazabilidad de casos de uso de negocio

| CUN | Preocupaciones atendidas | Stakeholders beneficiados | Conflictos que toca |
|---|---|---|---|
| CUN-01 | PRE-09, PRE-10, PRE-13, PRE-01 | STK-13, STK-06, STK-02 | CON-02 |
| CUN-02 | PRE-09, PRE-10, PRE-15 | STK-05, STK-11, STK-09 | — |
| CUN-03 | PRE-02, PRE-05, PRE-08 | STK-07, STK-04, STK-13 | CON-03 |
| CUN-04 | PRE-06, PRE-07 | STK-04, STK-14 | CON-02 |
| CUN-05 | PRE-15, PRE-04 | STK-05, STK-11, STK-13 | — |
| CUN-06 | PRE-14, PRE-06 | STK-10, STK-13 | — |
| CUN-07 | PRE-11, PRE-12, PRE-07 | STK-04, STK-12 | CON-05 |
| CUN-08 | PRE-13 | STK-06, STK-13 | — |
| CUN-09 | PRE-03, PRE-14 | STK-13 | — |

---

## 8. Derivación hacia los casos de uso expandidos

Los siguientes subprocesos son los candidatos a convertirse en casos de uso expandidos del sistema (CDU). Se entregan a Jonathan como base de trabajo, priorizados según el criterio declarado por el cliente en EV-43 (pagos y facturación primero) y según la lógica de dependencia técnica.

| Prioridad | Subproceso origen | CDU candidato |
|---|---|---|
| 1 | CUN-01.3 | Reservar inventario para una orden |
| 2 | CUN-01.4 | Confirmar orden |
| 3 | CUN-03.2, CUN-03.4 | Procesar cobro con preservación de la orden |
| 4 | CUN-04.2 | Certificar documento fiscal ante la SAT |
| 5 | CUN-01.1, CUN-01.2 | Reconocer cliente y mantener carrito unificado |
| 6 | CUN-02.4 | Consultar disponibilidad de inventario |
| 7 | CUN-05.1 | Determinar origen de despacho |
| 8 | CUN-06.1 | Consultar historial consolidado de una orden |
| 9 | CUN-07.2 | Transferir resúmenes de ventas al ERP |
| 10 | CUN-01.6 | Liberar reservas vencidas |
| 11 | CUN-05.5 | Actualizar estado de entrega |
| 12 | CUN-09.1 | Notificar cambio de estado de orden |

Esta priorización es preliminar y debe confirmarse en el apartado de características priorizadas del sistema, que es el siguiente artefacto del DDA.

---

