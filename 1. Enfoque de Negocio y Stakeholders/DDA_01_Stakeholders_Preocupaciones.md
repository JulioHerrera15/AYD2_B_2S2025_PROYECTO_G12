# Stakeholders y Preocupaciones Arquitectónicas — SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC  
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)  
**Elaborado por:** Angel Martinez (202300410) — Arquitecto de Negocio y Stakeholders  
**Depende de:** `DDA_00_Catalogo_Evidencia.md`  
**Versión:** 0.1 (borrador para revisión del equipo)

---

## 1. Alcance y criterios de identificación

Este apartado identifica a las partes interesadas en la arquitectura de SGO-Logic y documenta sus preocupaciones arquitectónicas. Toda entrada apunta a evidencia del enunciado mediante identificadores `EV-##`.

Se aplican tres criterios de rigor:

1. **Un stakeholder es una persona, grupo u organización con interés legítimo en el sistema.** Los sistemas de software no son stakeholders. El ERP-Core, el POS y las interfaces de los proveedores son actores externos del diagrama de contexto; sus dueños u operadores sí son stakeholders. Esta distinción se detalla en la sección 5 y es insumo directo para el Diagrama de Contexto.
2. **Una preocupación arquitectónica es un interés sobre una propiedad del sistema, no una funcionalidad solicitada.** "El cliente debe recibir notificaciones" es un requisito funcional; "una falla de notificaciones no debe detener la operación" es una preocupación arquitectónica.
3. **Un stakeholder no identificado en el enunciado no se inventa.** Cuando el enunciado menciona un rol sin nombre propio, se registra el rol y se deja explícita la ausencia del dato.

---

## 2. Listado de stakeholders

### 2.1 Gobierno y patrocinio

| ID | Stakeholder | Tipo | Evidencia | Interés principal | Poder sobre la arquitectura |
|---|---|---|---|---|---|
| STK-01 | Junta Directiva | Interno | EV-14, EV-16, EV-40 | Preservar la inversión en los sistemas existentes y limitar el riesgo de transición. | **Decisorio.** Su determinación sobre ERP-Core y POS es una restricción no negociable. |
| STK-02 | Dirección / Gerencia General (patrocinador) | Interno | EV-04, EV-41, EV-42, EV-43 | Habilitar la operación omnicanal dentro del presupuesto aprobado y antes del Black Friday. | **Decisorio.** Aprueba la inversión y fija el plazo comercial. |

*Nota: el enunciado usa indistintamente "La Dirección" (objetivo estratégico) y "La Gerencia" (aprobación presupuestaria). Se modelan como un mismo stakeholder patrocinador hasta que el cliente indique lo contrario.*

### 2.2 Ejecutivos con interés directo en el sistema

| ID | Stakeholder | Tipo | Evidencia | Interés principal | Poder sobre la arquitectura |
|---|---|---|---|---|---|
| STK-03 | Carlos Méndez — Gerente de TI | Interno | EV-37, EV-46 | Operar la solución sin interrupciones en picos de demanda, sin tocar el legacy y con un equipo interno capaz de mantenerla. | **Alto.** Declara explícitamente que rechaza propuestas que modifiquen ERP-Core o POS. |
| STK-04 | Ana Lucía Fuentes — Directora de Finanzas | Interno | EV-38, EV-30, EV-10 | Trazabilidad completa de las operaciones y cumplimiento fiscal ante auditorías de la SAT. | **Alto, con veto formal** sobre decisiones que reduzcan trazabilidad o comprometan el cumplimiento. |
| STK-05 | Roberto Quezada — Jefe de Logística | Interno | EV-39, EV-12, EV-32 | Reducir errores de despacho en 70% y obtener visibilidad oportuna de las órdenes. | **Medio.** Define metas operativas medibles, sin autoridad de veto declarada. |
| STK-06 | Directora de Comercialización  | Interno | EV-36, EV-35 | Experiencia de compra idéntica en todos los canales, sin silos. | **Medio-alto.** Es la voz del objetivo omnicanal ante la Dirección. |
| STK-07 | Jefe de Operaciones  | Interno | EV-11, EV-27 | Evitar la repetición de la pérdida de USD 150,000 por indisponibilidad en alta demanda. | **Medio.** Aporta la evidencia cuantificada del incidente y la proyección de demanda. |

### 2.3 Operativos internos (usuarios y mantenedores)

| ID | Stakeholder | Tipo | Evidencia | Interés principal | Poder sobre la arquitectura |
|---|---|---|---|---|---|
| STK-08 | Equipo interno de desarrollo y mantenimiento de TI | Interno | EV-46, EV-37 | Que la solución sea construible y mantenible con Java, .NET y conocimientos básicos de Docker. | **Bajo formal, alto efectivo.** Su capacidad técnica condiciona la viabilidad real de cualquier alternativa. |
| STK-09 | Personal de tienda (vendedores, operadores de POS) | Interno | EV-07, EV-16, EV-08 | Vender sin depender de llamadas telefónicas para confirmar existencias, sin cambiar su herramienta de trabajo. | **Bajo.** Usuario afectado, sin participación en la decisión. |
| STK-10 | Agentes del centro de atención | Interno | EV-06, EV-09, EV-33 | Consultar en una sola vista compras, pagos, despachos, devoluciones e incidencias. | **Bajo.** Usuario afectado; su productividad es métrica de éxito. |
| STK-11 | Personal de logística y centros de distribución | Interno | EV-32, EV-31, EV-08 | Saber qué preparar, desde dónde despachar y a quién entregar, con inventario confiable. | **Bajo.** Usuario operativo directo. |
| STK-12 | Personal de Finanzas y contabilidad | Interno | EV-10, EV-15 | Conciliar ventas sin hojas de cálculo manuales y cerrar el mes sin retrasos. | **Bajo.** Usuario afectado; reporta a STK-04. |

### 2.4 Externos

| ID | Stakeholder | Tipo | Evidencia | Interés principal | Poder sobre la arquitectura |
|---|---|---|---|---|---|
| STK-13 | Cliente final | Externo | EV-04, EV-21, EV-33, EV-34, EV-36 | Comprar desde cualquier canal con experiencia consistente, conocer el estado de su orden y recibir notificaciones. | **Nulo formal, determinante en valor.** Es el beneficiario del objetivo estratégico. |
| STK-14 | SAT (Superintendencia de Administración Tributaria) | Externo | EV-19, EV-29, EV-30, EV-44 | Recibir documentación fiscal válida y poder auditar las operaciones. | **Alto, no negociable.** Impone obligaciones legales al diseño. |
| STK-15 | PayKlik (proveedor de pasarela de pagos) | Externo | EV-17, EV-45, EV-25 | Mantener el contrato vigente y su interfaz REST actual. | **Bajo, pero con alta dependencia técnica** por su latencia variable. |
| STK-16 | LogiExpress (proveedor logístico) | Externo | EV-18, EV-45 | Mantener el contrato vigente y su integración REST actual. | **Bajo, pero con alta dependencia técnica** por su tasa de timeout del 2%. |
| STK-17 | Proveedor de notificaciones (SMS, correo, push) | Externo | EV-20, EV-34 | Prestar el servicio bajo sus propias condiciones de disponibilidad. | **Bajo.** Su disponibilidad está fuera del control de MegaStore. |
| STK-18 | Entes reguladores de protección de datos (Guatemala, El Salvador, Costa Rica) | Externo | EV-44, EV-01 | Cumplimiento de la normativa aplicable en los tres países de operación. | **Alto, no negociable.** Afecta retención, cifrado y tratamiento de datos personales. |

---

## 3. Preocupaciones arquitectónicas

Cada preocupación indica los stakeholders que la sostienen y la evidencia que la origina. Se ordenan por bloque temático, no por prioridad.

### 3.1 Continuidad y resiliencia

| ID | Preocupación | Stakeholders | Evidencia |
|---|---|---|---|
| PRE-01 | El sistema debe sostener la operación durante picos de demanda de hasta ocho veces la carga normal, sin degradación que interrumpa la venta. | STK-03, STK-07, STK-02 | EV-11, EV-27, EV-43 |
| PRE-02 | La falla de un sistema externo no debe provocar la pérdida de una orden ya iniciada por el cliente. | STK-03, STK-07, STK-02 | EV-28, EV-11, EV-18 |
| PRE-03 | Las fallas de canales secundarios, en particular las notificaciones, deben permanecer aisladas del flujo principal de venta. | STK-03, STK-13 | EV-34, EV-20 |
| PRE-04 | El comportamiento y la disponibilidad de las dependencias externas deben ser observables de forma continua. | STK-03 | EV-37, EV-17, EV-18 |

### 3.2 Trazabilidad y cumplimiento

| ID | Preocupación | Stakeholders | Evidencia |
|---|---|---|---|
| PRE-05 | Debe ser posible reconstruir quién ejecutó cada operación, cuándo ocurrió y qué se modificó, sin ambigüedad. | STK-04, STK-14 | EV-30, EV-26 |
| PRE-06 | La documentación fiscal debe ser válida, oportuna y mantener la relación entre venta original, devoluciones y reembolsos. | STK-04, STK-14, STK-12 | EV-19, EV-29 |
| PRE-07 | Ninguna transacción puede quedar sin respaldo recuperable, ya que ello deriva en multas o inconsistencias fiscales. | STK-04, STK-14 | EV-30 |
| PRE-08 | El tratamiento de datos personales y de medios de pago debe cumplir la normativa aplicable, sin almacenar datos sensibles de tarjeta. | STK-18, STK-04, STK-13 | EV-25, EV-44 |

### 3.3 Integridad y consistencia del negocio

| ID | Preocupación | Stakeholders | Evidencia |
|---|---|---|---|
| PRE-09 | Una misma unidad de inventario no puede comprometerse a más de un cliente. | STK-05, STK-06, STK-13 | EV-22 |
| PRE-10 | El inventario reservado por compras no concluidas debe liberarse automáticamente en un plazo acorde a la operación comercial. | STK-05, STK-06 | EV-23 |
| PRE-11 | La información propagada hacia los sistemas legacy no puede perderse ni duplicarse, aun cuando el destino esté temporalmente indisponible. | STK-03, STK-12, STK-04 | EV-15, EV-16, EV-10 |
| PRE-12 | La consistencia contable con el ERP está sujeta a una ventana de hasta 30 minutos y debe ser conciliable. | STK-04, STK-12 | EV-15, EV-10 |

### 3.4 Experiencia y valor para el negocio

| ID | Preocupación | Stakeholders | Evidencia |
|---|---|---|---|
| PRE-13 | La experiencia de compra debe ser equivalente en todos los canales, permitiendo iniciar en uno y concluir en otro. | STK-06, STK-13, STK-02 | EV-04, EV-21, EV-35, EV-36 |
| PRE-14 | Las consultas de estado e historial de órdenes deben responder dentro de límites de tiempo aceptables para el cliente y el personal de atención. | STK-10, STK-13, STK-04 | EV-33, EV-30, EV-06 |
| PRE-15 | La operación debe contar con visibilidad oportuna de órdenes e inventario para reducir errores de despacho. | STK-05, STK-11 | EV-12, EV-32, EV-08 |

### 3.5 Viabilidad técnica y económica

| ID | Preocupación | Stakeholders | Evidencia |
|---|---|---|---|
| PRE-16 | Los sistemas ERP-Core y POS no pueden ser modificados ni reemplazados dentro del horizonte de la iniciativa. | STK-01, STK-03 | EV-14, EV-16, EV-37 |
| PRE-17 | La arquitectura debe ser mantenible por el equipo interno con sus competencias actuales; toda desviación debe cuantificar el impacto de adopción y capacitación. | STK-03, STK-08 | EV-46, EV-37 |
| PRE-18 | La inversión inicial y el costo operativo mensual deben mantenerse dentro de los límites aprobados, con eficiencia en el consumo energético. | STK-02, STK-01 | EV-41, EV-42, EV-47 |
| PRE-19 | La solución debe desplegarse inicialmente on-premises y permitir una migración planificada a la nube sin rediseño arquitectónico. | STK-01, STK-03, STK-02 | EV-48, EV-49 |
| PRE-20 | Debe existir una ruta viable dentro del primer mes para proteger los procesos de pagos y facturación antes del Black Friday de noviembre. | STK-02, STK-07, STK-03 | EV-43 |

---

## 4. Matriz stakeholder frente a preocupación

Marca `X` donde el stakeholder sostiene la preocupación de forma directa.

| Stakeholder | 01 | 02 | 03 | 04 | 05 | 06 | 07 | 08 | 09 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 | 19 | 20 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| STK-01 Junta Directiva | | | | | | | | | | | | | | | | X | | X | X | |
| STK-02 Dirección / Gerencia | X | X | | | | | | | | | | | X | | | | | X | X | X |
| STK-03 Carlos Méndez (TI) | X | X | X | X | | | | | | | X | | | | | X | X | | X | X |
| STK-04 Ana Lucía Fuentes (Finanzas) | | | | | X | X | X | X | | | X | X | | X | | | | | | |
| STK-05 Roberto Quezada (Logística) | | | | | | | | | X | X | | | | | X | | | | | |
| STK-06 Dir. Comercialización | | | | | | | | | X | X | | | X | | | | | | | |
| STK-07 Jefe de Operaciones | X | X | | | | | | | | | | | | | | | | | | X |
| STK-08 Equipo interno de TI | | | | | | | | | | | | | | | | | X | | | |
| STK-09 Personal de tienda | | | | | | | | | | | | | X | | X | X | | | | |
| STK-10 Centro de atención | | | | | | | | | | | | | X | X | | | | | | |
| STK-11 Logística y CD | | | | | | | | | | | | | | | X | | | | | |
| STK-12 Finanzas / contabilidad | | | | | | X | | | | | X | X | | | | | | | | |
| STK-13 Cliente final | | X | X | | | | | X | X | | | | X | X | | | | | | |
| STK-14 SAT | | | | | X | X | X | | | | | | | | | | | | | |
| STK-15 PayKlik | | | | X | | | | X | | | | | | | | | | | | |
| STK-16 LogiExpress | | | | X | | | | | | | | | | | X | | | | | |
| STK-17 Proveedor de notificaciones | | | X | X | | | | | | | | | | | | | | | | |
| STK-18 Reguladores de datos | | | | | | | | X | | | | | | | | | | | | |

---

## 5. Actores externos que no son stakeholders

Distinción necesaria para el Diagrama de Contexto (responsable: Francisco). Estos elementos aparecen como actores o sistemas externos, pero no como partes interesadas.

| Elemento | Naturaleza | Stakeholder asociado | Modo de interacción declarado |
|---|---|---|---|
| ERP-Core (AS/400) | Sistema legacy interno | STK-01, STK-12 | Recepción de resúmenes de ventas en CSV o XML, procesables en intervalos de 30 minutos (EV-15) |
| POS local (32 instancias) | Sistema legacy interno | STK-03, STK-09 | Recepción de eventos desde SGO-Logic con manejo de errores y reintentos (EV-16) |
| Interfaz REST de PayKlik | Servicio externo | STK-15 | Invocación REST, latencia de 800 ms hasta 5 s (EV-17) |
| Interfaz REST de LogiExpress | Servicio externo | STK-16 | Invocación REST, 1 de cada 50 solicitudes con timeout (EV-18) |
| Servicio de certificación de la SAT | Servicio externo regulatorio | STK-14 | Envío de XML con CUI y serie autorizada, tiempos de respuesta variables (EV-19) |
| Servicio de notificaciones | Servicio externo | STK-17 | SMS, correo y push, disponibilidad fuera de control (EV-20) |

---

## 6. Análisis de poder e interés

Clasificación usada para decidir a quién se debe convencer y a quién únicamente informar durante la defensa.

| Cuadrante | Stakeholders | Estrategia de gestión |
|---|---|---|
| **Alto poder, alto interés** (gestionar de cerca) | STK-03, STK-04, STK-02 | Cada decisión arquitectónica relevante debe llevar justificación explícita dirigida a sus preocupaciones. Son la audiencia principal de la presentación. |
| **Alto poder, interés puntual** (mantener satisfechos) | STK-01, STK-14, STK-18 | Sus restricciones se tratan como límites del espacio de solución, no como variables a optimizar. |
| **Bajo poder, alto interés** (mantener informados) | STK-05, STK-06, STK-07, STK-08, STK-13 | Sus preocupaciones se traducen en escenarios de calidad y casos de uso; se muestran como evidencia de que la solución atiende la operación real. |
| **Bajo poder, bajo interés** (monitorear) | STK-09, STK-10, STK-11, STK-12, STK-15, STK-16, STK-17 | Se documentan como usuarios o dependencias; no requieren negociación arquitectónica directa. |

---

## 7. Conflictos entre preocupaciones

Estos conflictos son el eje de la defensa oral. La columna de estrategia es una hipótesis preliminar que deberá formalizarse y decidirse mediante ADR; no constituye aún una decisión tomada.

| ID | Conflicto | Preocupaciones enfrentadas | Stakeholders | Estrategia preliminar a evaluar |
|---|---|---|---|---|
| CON-01 | Trazabilidad exhaustiva frente a rendimiento en picos de demanda | PRE-05, PRE-07 vs. PRE-01, PRE-14 | STK-04 vs. STK-03 | Registro de auditoría desacoplado de la ruta crítica de escritura, con garantía de no pérdida. Es el conflicto central del caso. |
| CON-02 | Obligación fiscal previa al cierre de la orden frente a tiempos de respuesta aceptables | PRE-06 vs. PRE-13, PRE-14 | STK-14 vs. STK-13, STK-06 | Precisar qué significa "finalizar la orden" (VAC-03) y separar la confirmación al cliente de la certificación fiscal. |
| CON-03 | Independencia frente al proveedor de pagos y prohibición de almacenar datos de tarjeta | PRE-02 vs. PRE-08 | STK-07 vs. STK-18, STK-04 | Rutas de pago alternativas y captura diferida de intentos tokenizados, no un procesador propio. |
| CON-04 | Ambición de resiliencia y elasticidad frente al techo presupuestario | PRE-01, PRE-19 vs. PRE-18 | STK-03 vs. STK-02, STK-01 | Cuantificar el costo de cada mecanismo de resiliencia y priorizarlos según el valor en riesgo declarado en EV-11. |
| CON-05 | Prohibición de modificar el legacy frente a la necesidad de consistencia contable oportuna | PRE-16, PRE-12 vs. PRE-07 | STK-01, STK-03 vs. STK-04 | Aceptar consistencia eventual con ventana de 30 minutos y compensarla con conciliación automática verificable. |
| CON-06 | Mantenibilidad con las competencias actuales frente a la sofisticación técnica requerida | PRE-17 vs. PRE-01, PRE-02 | STK-08, STK-03 vs. STK-07 | Limitar el número de tecnologías nuevas y justificar cada una con impacto de adopción, según exige EV-46. |

---

## 8. Trazabilidad hacia el resto del DDA

| Preocupación | Se materializa en | Responsable del artefacto siguiente |
|---|---|---|
| PRE-01, PRE-02, PRE-03, PRE-04 | Escenarios de atributo de calidad de disponibilidad y tolerancia a fallos (`EAC`) | Jose |
| PRE-05, PRE-06, PRE-07, PRE-08 | `EAC` de auditabilidad y seguridad; `RES` regulatorias | Jose, Francisco |
| PRE-09, PRE-10, PRE-11, PRE-12 | Drivers funcionales de inventario e integración (`DRF`); `EAC` de integridad | Angel, Francisco |
| PRE-13, PRE-14, PRE-15 | Casos de uso de negocio y expandidos (`CUN`, `CDU`); `EAC` de rendimiento | Angel, Jonathan, Jose |
| PRE-16, PRE-17, PRE-18, PRE-19, PRE-20 | Drivers de restricción (`RES`); diagrama de despliegue | Francisco, Julio |
| CON-01 a CON-06 | Architecture Decision Records (`ADR`) y guion de la defensa oral | Todo el equipo |

---

## 9. Pendientes de este apartado

1. Confirmar con el cliente si "La Dirección" y "La Gerencia" (STK-02) son el mismo stakeholder.
2. Solicitar los nombres de la Directora de Comercialización (STK-06) y del Jefe de Operaciones (STK-07), o declarar formalmente su ausencia en el DDA.
3. Validar con el equipo la matriz de la sección 4 antes de derivar los drivers, ya que es el insumo de la matriz de trazabilidad final.
