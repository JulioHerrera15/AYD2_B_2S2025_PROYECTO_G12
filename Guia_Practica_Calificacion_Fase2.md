# Guía práctica de sustentación — Calificación Fase 2

**Proyecto:** SGO-Logic  
**Duración sugerida:** 40 minutos  
**Modalidad:** Presencial  
**Objetivo:** demostrar cada apartado de la rúbrica mediante explicación, código y evidencia observable.

> Esta guía está diseñada para que cada punto de la rúbrica siga la misma secuencia: **requisito → diseño → código → prueba → evidencia → conclusión**.

---

## 1. Objetivo

Durante la sustentación se debe demostrar que las funcionalidades no solamente están descritas en la documentación, sino que también se encuentran implementadas, trazadas y verificadas mediante pruebas reproducibles.

Para cada elemento se debe responder:

1. **¿Qué exige la rúbrica?**
2. **¿Qué decisión de arquitectura lo sustenta?**
3. **¿Dónde está implementado?**
4. **¿Cómo se comprueba en ejecución?**
5. **¿Qué evidencia se muestra?**

---

# 2. Preparación antes de iniciar

Todos los comandos se ejecutan desde:

```bash
cd SGO-Logic
```

## 2.1 Levantar el sistema

```bash
cp .env.example .env
docker compose up -d --build
docker compose ps
```

Verificar que los contenedores estén `healthy`.

```bash
curl http://localhost:8080/health/live
```

## 2.2 Servicios que se utilizarán

| Servicio | Dirección | Uso |
|---|---|---|
| Web | `http://localhost:8092` | Demostración de usuario |
| API Gateway | `http://localhost:8080` | Entrada principal |
| RabbitMQ | `http://localhost:15672` | Eventos, colas y DLQ |
| Grafana | `http://localhost:3000` | Métricas y observabilidad |
| Prometheus | `http://localhost:9090/targets` | Targets del sistema |
| PayKlik mock | `http://localhost:8084` | Pagos |
| SAT mock | `http://localhost:8086` | Facturación |
| LogiExpress mock | `http://localhost:8098` | Despacho |
| Mock mensajería | `http://localhost:8089` | Notificaciones |
| Mock ERP | `http://localhost:4000` | Sincronización ERP |
| Mock POS | `http://localhost:4001` | Propagación al POS |

## 2.3 Evidencias de apoyo

Tener abiertos:

- `README.md`
- `documentacion/docs/DDA_SGOLogic.md`
- `documentacion/docs/bitacora.md`
- `documentacion/docs/AUDITORIA.md`
- `documentacion/docs/OBSERVABILIDAD.md`
- `documentacion/docs/PRUEBA-CARGA-EAC01.md`
- `documentacion/docs/PRUEBA-CARGA-EAC02.md`
- `documentacion/docs/CONCURRENCIA-EAC05.md`
- `documentacion/docs/ESCALABILIDAD.md`
- `documentacion/img/Diagrama de Contexto.png`
- `documentacion/img/Diagrama de Despliegue Lógico.png`
- `documentacion/img/TrelloInicial.png`
- `documentacion/img/TrelloFinal.png`

---

# 3. Distribución sugerida de los 40 minutos

| Tiempo | Apartado | Evidencia principal |
|---:|---|---|
| 0–3 min | Problema y arquitectura | DDA, diagrama de contexto y bloques |
| 3–11 min | Gestión de venta | Saga, pagos, factura XML y carrito |
| 11–14 min | Inventario | Reserva, concurrencia y liberación |
| 14–17 min | Logística y atención | LogiExpress, consulta de estado y notificaciones |
| 17–21 min | Auditoría, ERP y POS | Bitácora, CSV/XML, conciliación y eventos POS |
| 21–25 min | Pruebas de calidad | EAC-01 a EAC-05, métricas y tests |
| 25–29 min | Cambios de Fase 1 | ADR, migraciones, correcciones y decisiones |
| 29–32 min | Scrum y documentación | Trello, dailies, retrospectiva y burndown |
| 32–38 min | Demostración funcional | Flujo completo y falla de proveedor |
| 38–40 min | Cierre | Conclusiones, limitaciones y preguntas |

---

# 4. Evaluación práctica de la rúbrica

## 4.1 Gestión de venta — 20 puntos

### 4.1.1 Confirmar orden — 5 puntos

### Requisito

La orden debe crearse, confirmar sus estados y mantener trazabilidad hasta el despacho.

### Diseño

- `CU-02 Confirmar orden`.
- `ADR-002 Saga orquestada centralizada`.
- `ADR-016 Saga dirigida por eventos de resultado`.

### Código

- `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/`
- `MaquinaEstadosOrden.cs`
- `ServicioConfirmacionOrden.cs`
- Consumidores de Inventario, Pagos, Facturación y Despacho.

### Demostración

1. Ingresar a `http://localhost:8092`.
2. Autenticarse o registrar el usuario demo.
3. Agregar productos al carrito.
4. Confirmar la orden.
5. Registrar el identificador de la orden.
6. Consultar su estado mediante el gateway o el servicio de Órdenes.

```bash
curl http://localhost:8083/api/ordenes/ORD-XXXX
```

### Evidencia que debe mostrarse

- Orden persistida.
- Estados sucesivos.
- Eventos de transición.
- Registro correspondiente en Auditoría.
- Vista de estado en Redis/Consulta de Estado.

### Conclusión esperada

> La orden no se trata como un registro aislado: su ciclo de vida se coordina mediante la saga y cada transición queda disponible para consulta, auditoría y notificación.

---

### 4.1.2 Recibir pago — 3 puntos

### Requisito

Integrar el cobro con PayKlik y registrar el resultado.

### Diseño

- `CU-03 Procesar cobro con preservación de la orden`.
- `ADR-009 Circuit breaker y reintentos`.
- `ADR-010 Tokenización y no almacenamiento de datos de tarjeta`.

### Código

- `SGO-Logic/src/Services/SgoLogic.Pagos.Api/`
- `ClientePayKlik` o cliente REST equivalente.
- Consumidores de eventos de Órdenes.
- `documentacion/docs/contratos/pagos-eventos-v1.md`.

### Demostración

1. Iniciar el mock de PayKlik en modo normal.
2. Crear una orden hasta el cobro.
3. Revisar el intento de pago registrado.
4. Verificar el resultado en el estado de la orden.
5. Mostrar el evento de pago en RabbitMQ o Auditoría.

```bash
curl http://localhost:8083/api/ordenes/ORD-XXXX
```

```bash
curl http://localhost:8093/auditoria/agregados/ORD-XXXX
```

### Evidencia

- Intento de pago con orden, medio y resultado.
- Token de pago, nunca PAN/CVV.
- Estado `PAGO_APLICADO`.
- Evento de auditoría.
- Registro de la llamada al mock.

---

### 4.1.3 Reintentar pago — 2 puntos

### Requisito

Ante latencia mayor a cinco segundos o caída del proveedor, la orden debe conservarse y el cobro poder reintentarse.

### Demostración con PayKlik caído

```bash
curl -X PUT http://localhost:8084/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo":"CAIDO"}'
```

Crear una orden y confirmar que llegue al cobro.

Después:

```bash
curl http://localhost:8083/api/ordenes/ORD-XXXX
```

```bash
curl http://localhost:8093/auditoria/agregados/ORD-XXXX
```

Restaurar el mock:

```bash
curl -X PUT http://localhost:8084/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo":"NORMAL"}'
```

### Evidencia

- Orden en `PAGO_PENDIENTE`, no perdida.
- Intento fallido registrado.
- Outbox pendiente o reencolado.
- Reintento posterior.
- Evento de auditoría por cada intento fallido.

### Evidencia versionada

- `documentacion/docs/PRUEBA-CARGA-EAC01.md`
- `SGO-Logic/scripts/pruebas/evidencia/eac01-carga-2026-09-22.txt`
- `SGO-Logic/scripts/pruebas/evidencia/eac01-verificacion-2026-09-22.txt`

Resultados documentados: 0 órdenes perdidas y 100 % de intentos fallidos auditados.

---

### 4.1.4 Emitir factura — 3 puntos

### Requisito

Generar un documento fiscal con CUI y serie autorizada y enviarlo asíncronamente a la SAT.

### Diseño y código

- `CU-04 Certificar documento fiscal ante la SAT`.
- `ADR-011 Certificación fiscal asíncrona ante la SAT`.
- `ADR-015 Bandera de alerta fiscal`.
- `SGO-Logic/src/Services/SgoLogic.Facturacion.Api/`
- `documentacion/diagramas_CDU/CU-04.svg`.

### Prueba en vivo

1. Poner el mock SAT en modo normal:

```bash
curl -X PUT http://localhost:8086/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo":"NORMAL"}'
```

2. Crear y confirmar una orden.
3. Esperar a que Facturación genere el documento.
4. Consultar el documento vinculado a la orden:

```bash
curl http://localhost:8087/api/facturacion/documentos/ORD-XXXX
```

5. Revisar los logs de Facturación y SAT:

```bash
docker compose logs -f facturacion-api
docker compose logs -f sat-mock
```

6. Mostrar el XML o payload enviado al `sat-mock`, incluyendo CUI, serie, orden relacionada y estado de certificación.

### Alternativa para capturar el XML

Buscar el XML generado en los logs o en la base de Facturación:

```bash
docker compose logs facturacion-api | grep -iE "xml|CUI|serie|documento"
```

Si el XML se persiste como archivo o registro, mostrar directamente su contenido y la relación con `ORD-XXXX`.

### Prueba de falla fiscal

```bash
curl -X PUT http://localhost:8086/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo":"CAIDO"}'
```

Verificar que:

- La orden no se pierda.
- El documento quede en reintento.
- Se genere la alerta fiscal correspondiente.
- Se registre el resultado en Auditoría.

Restaurar:

```bash
curl -X PUT http://localhost:8086/admin/modo \
  -H "Content-Type: application/json" \
  -d '{"modo":"NORMAL"}'
```

### Evidencia obligatoria

- XML generado.
- CUI y serie.
- Identificador de la orden.
- Envío asíncrono al `sat-mock`.
- Respuesta de certificación.
- Estado persistido.
- Evento de auditoría.

---

### 4.1.5 Consultar factura — 2 puntos

### Prueba

```bash
curl http://localhost:8087/api/facturacion/documentos/ORD-XXXX
```

Mostrar:

- Documento fiscal.
- Número o serie.
- CUI.
- Estado.
- Relación con la orden.
- Fecha de generación y certificación.
- Nota de crédito o reversión cuando corresponda.

### Evidencia

- Endpoint de consulta.
- Tabla de documentos fiscales.
- Relación `OrdenId`/documento.
- Auditoría de creación y certificación.

### Conclusión

> La factura no es únicamente un archivo aislado: se genera desde el flujo de la orden, se procesa de manera asíncrona y queda consultable y auditable.

---

### 4.1.6 Continuar compra en otro canal — 3 puntos

### Requisito

Mantener el carrito compartido entre canales.

### Demostración

1. Crear o recuperar el usuario.
2. Agregar productos desde Web.
3. Consultar el carrito desde la API:

```bash
curl http://localhost:8088/api/carrito
```

4. Mostrar la clave y TTL de Redis:

```bash
docker compose exec redis redis-cli --scan --pattern "carrito:*"
```

5. Mostrar que la sesión no depende de una sesión local del navegador.

### Evidencia

- `SgoLogic.Auth.Api`.
- Redis como almacenamiento del carrito.
- Clave `carrito:{id}`.
- TTL configurable.
- Fusión entre carrito anónimo y usuario autenticado.

### Alcance que debe aclararse

> El MVP demuestra la persistencia yassociation del carrito entre interfaces y canales equivalentes. La aplicación móvil y el POS físico no son clientes的生产 completos; ambos permanecen como canales/adaptadores dentro del diseño y el alcance documentado.

---

### 4.1.7 Agregar o quitar productos — 2 puntos

### Demostración

1. Agregar un producto al carrito.
2. Consultar su contenido.
3. Quitarlo.
4. Confirmar que la actualización sea consistente.
5. Crear la orden y comprobar que el carrito y la orden no dupliquen líneas.

### Código y evidencia

- `SgoLogic.Auth.Api/Controllers/CarritoController.cs`.
- Servicio de carrito.
- Redis y TTL.
- Contratos de carrito.

### Conclusión

> El carrito es estado compartido, con operaciones consistentes y persistencia en Redis, no solamente un formulario local.

---

## 4.2 Gestión de inventario — 10 puntos

### 4.2.1 Consultar inventario — 3 puntos

### Requisito

Mostrar existencias por tienda, centro de distribución y ubicación.

### Prueba

```bash
curl http://localhost:8094/api/inventario/existencias
```

Si el endpoint requiere filtros, consultar la API de Inventario y utilizar el identificador de ubicación disponible en los datos demo.

### Evidencia

- `SKU` o producto.
- Ubicación.
- Cantidad física.
- Cantidad reservada.
- Cantidad disponible.
- Servicio y base de Inventario.

### Conclusión

> La vista de inventario representa las ubicaciones de la operación, no solamente el stock de una tienda aislada.

---

### 4.2.2 Reservar producto — 2 puntos

### Prueba de concurrencia

Ejecutar la prueba documentada para EAC-05:

```bash
cd SGO-Logic
dotnet test tests/SgoLogic.Inventario.IntegrationTests
```

Si el SDK no está instalado, utilizar el runner de Docker o el procedimiento de pruebas indicado en el README.

### Evidencia

- Dos o más solicitudes concurrentes.
- Existencia inicial.
- Una reserva exitosa.
- Rechazo de la reserva conflictiva.
- Ninguna unidad comprometida dos veces.

### Conclusión

> El bloqueo optimista mediante `version_column` protege la consistencia cuando dos clientes intentan reservar la misma unidad al mismo tiempo.

---

### 4.2.3 Liberar inventario — 2 puntos

### Prueba

1. Crear una reserva.
2. Dejar expirar el TTL o cancelar la orden.
3. Consultar la reserva y la existencia.
4. Mostrar que la cantidad vuelve a estar disponible.
5. Verificar el evento de liberación.

```bash
curl http://localhost:8094/api/inventario/reservas/ORD-XXXX
```

### Evidencia

- Estado `LIBERADA`, `EXPIRADA` o equivalente.
- Actualización de disponibilidad.
- Evento publicado.
- Registro de auditoría.

---

### 4.2.4 Actualizar inventario — 3 puntos

### Demostración

1. Consultar existencia inicial.
2. Registrar una reserva.
3. Crear o confirmar una orden.
4. Consultar el cambio de cantidad reservada.
5. Probar una devolución o cancelación.
6. Mostrar el reingreso de stock.
7. Revisar el evento entregado al POS.

### Evidencia

- Cambios de existencia.
- Movimientos registrados.
- Eventos de Inventario.
- Registros del `integracion-pos-worker`.
- Auditoría de la operación.

---

## 4.3 Logística y entrega — 8 puntos

### 4.3.1 Solicitar envío a LogiExpress — 3 puntos

### Prueba

1. Confirmar una orden elegible para despacho.
2. Esperar la solicitud de guía.
3. Consultar la guía:

```bash
curl http://localhost:8097/api/despacho/ORD-XXXX
```

4. Revisar el mock:

```bash
curl http://localhost:8098/admin/guias
```

### Evidencia

- Identificador de la orden.
- Ubicación de origen.
- Destino.
- Guía generada.
- Registro del envío.
- Evento de despacho.

---

### 4.3.2 Asignar origen y destino — 3 puntos

### Demostración

Usar el camino feliz y revisar la transición de despacho:

```bash
curl http://localhost:8083/api/ordenes/ORD-XXXX
```

Mostrar:

- SKU reservado.
- Ubicación de origen.
- Centro de distribución o tienda seleccionada.
- Destino de la orden.
- Estado `DESPACHO_SOLICITADO` o posterior.

### Evidencia

- Lógica de selección en `SgoLogic.Despacho.Api`.
- Relación entre Inventario y Despacho.
- Guía o instrucción de preparación.

---

### 4.3.3 Actualizar estado de envío — 2 puntos

### Prueba con LogiExpress

Cambiar el estado de la guía desde el mock:

```bash
curl -X POST http://localhost:8098/admin/guias/GUIA-ID/estado \
  -H "Content-Type: application/json" \
  -d '{"estado":"EN_TRANSITO"}'
```

Después consultar:

```bash
curl http://localhost:8097/api/despacho/ORD-XXXX
```

```bash
curl http://localhost:8091/api/estado/ordenes/ORD-XXXX
```

### Evidencia

- Estado persistido de la guía.
- Estado actualizado de la orden.
- Vista de Consulta de Estado.
- Notificación de cambio.
- Auditoría del evento.

### Probar timeout

Configurar el mock en modo `CAIDO` o timeout y comprobar que el sistema:

- No pierde la orden.
- Reintenta o deja la guía pendiente.
- Permite que el despacho continúe según la decisión documentada.

---

## 4.4 Atención al cliente — 5 puntos

### 4.4.1 Consultar estado — 2 puntos

### Prueba

```bash
curl http://localhost:8091/api/estado/ordenes/ORD-XXXX
```

Mostrar que la respuesta incluye, según la etapa:

- Compra.
- Cobro.
- Documento fiscal.
- Despacho.
- Entrega.

### Evidencia de rendimiento

Consultar:

- `documentacion/docs/PRUEBA-CARGA-EAC02.md`
- `documentacion/docs/OBSERVABILIDAD.md`

Resultados documentados:

- 17,955 consultas procesadas.
- Aproximadamente 450 req/s.
- P95 de 9.27 ms.
- P100 de 232.09 ms.

El resultado cumple el criterio de menos de cinco segundos.

---

### 4.4.2 Registrar atención — 1 punto

Crear y consultar una incidencia vinculada a una orden.

```bash
curl -X POST http://localhost:8091/api/incidencias \
  -H "Content-Type: application/json" \
  -d '{"ordenId":"ORD-XXXX","descripcion":"Incidencia de demostración"}'
```

El nombre exacto del endpoint debe ajustarse al controller real si la API utiliza otra ruta.

### Evidencia

- Incidencia persistida.
- Relación con la orden.
- Estado de la incidencia.
- Historial de cambios.
- Registro de auditoría.

---

### 4.4.3 Consultar historial — 2 puntos

Mostrar:

- Historial de órdenes del cliente.
- Incidencias.
- Devoluciones.
- Reembolsos cuando corresponda.
- Estados y fechas.

### Evidencia

- Vista de Consulta de Estado.
- Persistencia relacional.
- Índices por `cliente_id` y `orden_id`.
- DTOs o consulta de historial.

---

## 4.5 Auditoría y consolidación — 7 puntos

### 4.5.1 Sincronizar ventas con ERP — 3 puntos

### Requisito

Generar y enviar automáticamente un lote CSV o XML al ERP cada 30 minutos, con reintentos.

### Diseño y código

- `CU-13 Consolidar y transferir ventas al ERP`.
- `ADR-007 Capa anticorrupción para ERP AS/400`.
- `ADR-008 Integración batch cada 30 minutos`.
- `SGO-Logic/src/Services/SgoLogic.IntegracionErp.Worker/`.

### Prueba en vivo

Para que la demostración no espere 30 minutos, crear un override con intervalo corto antes de levantar el worker:

```bash
docker compose run --rm -e Erp__IntervaloConsolidacionMinutos=1 integracion-erp-worker
```

Si la configuración utiliza la variable `ERP_INTERVALO_MINUTOS`, usar la forma equivalente documentada en `docker-compose.yml`:

```bash
ERP_INTERVALO_MINUTOS=1 docker compose up -d integracion-erp-worker
```

1. Poner `mock-erp` en línea.
2. Crear y confirmar una venta.
3. Esperar el siguiente ciclo de consolidación.
4. Consultar los lotes:

```bash
curl http://localhost:4000/erp/lotes
curl http://localhost:4000/erp/admin/estado
```

5. Revisar logs:

```bash
docker compose logs -f integracion-erp-worker
```

6. Mostrar el archivo CSV o XML generado y su respuesta de envío.

### Evidencia obligatoria

- Archivo CSV o XML real.
- Fecha y periodo del lote.
- Número de registros.
- Totales.
- Código HTTP o confirmación del `mock-erp`.
- Registro del lote en la base de Integración ERP.
- Evento `LoteErpProcesado`.
- Registro de conciliación.
- Auditoría del proceso.

### Prueba de reintento

Forzar una falla:

```bash
curl -X POST http://localhost:4000/erp/admin/caida \
  -H "Content-Type: application/json" \
  -d '{"activar":true,"tipo":"no_disponible"}'
```

Verificar que:

- El lote no se marque como enviado exitosamente.
- El worker reintente.
- El error quede registrado.
- La conciliación no oculte la diferencia.

Restaurar:

```bash
curl -X POST http://localhost:4000/erp/admin/reset
```

### Conclusión

> La integración no consiste únicamente en tener un archivo: el worker lo genera, lo envía al ERP dentro de la ventana definida y conserva el resultado para poder auditarlo y conciliarlo.

---

### 4.5.2 Informe de ventas — 4 puntos

### Prueba

Después de la sincronización:

1. Consultar el lote enviado.
2. Consultar el resumen recibido por el mock.
3. Comparar conteos y montos.
4. Mostrar la conciliación de diferencias.
5. Consultar el informe o registro asociado.
6. Mostrar la trazabilidad hacia la orden original.

### Evidencia

- Lote enviado.
- Totales del ERP.
- Reporte conciliado.
- Diferencias, si existen.
- Eventos y auditoría.
- Referencias a las órdenes que componen el lote.

### Punto de atención

La rúbrica pide específicamente un **informe de ventas conciliado y auditable**. Por ello no basta con mostrar que el worker creó un archivo. Debe mostrarse el resultado final de la conciliación y su relación con el lote.

---

## 4.6 Propagación al POS

La rúbrica no contiene una sección independiente para POS, pero sí lo incluye dentro de la coherencia con auditoría y consolidate.

Mostrar:

- Evento de venta publicado por Órdenes.
- Binding del `integracion-pos-worker`.
- Mapeo de tienda a instancia POS.
- Idempotencia por `eventId`.
- Reintento o DLQ cuando el POS falla.
- Registro de envío.

Prueba de falla:

```bash
curl -X POST http://localhost:4001/pos/admin/T01/caida \
  -H "Content-Type: application/json" \
  -d '{"activar":true,"tipo":"no_disponible"}'
```

Después revisar los registros del worker y restaurar el POS.

---

# 5. Pruebas de atributos de calidad

## EAC-01 — Disponibilidad ante falla de PayKlik

Evidencia:

```bash
cat documentacion/docs/PRUEBA-CARGA-EAC01.md
cat SGO-Logic/scripts/pruebas/evidencia/eac01-verificacion-2026-09-22.txt
```

Resultado:

- 0 órdenes perdidas.
- 100 % de intentos fallidos auditados.
- 100 % de checks de k6 exitosos.
- La disponibilidad de 99.9 % no se mide en una corrida local corta.

## EAC-02 — Consulta de estado

Mostrar el reporte de `PRUEBA-CARGA-EAC02.md` y, si es posible, repetir una consulta con Prometheus o Grafana.

Criterio: respuesta menor a cinco segundos.

## EAC-03 — Recuperación de evidencia

```bash
curl http://localhost:8093/auditoria/transacciones/TRANSACTION-ID
```

Mostrar:

- Todos los eventos de la transacción.
- Orden cronológico.
- `transactionId`.
- `eventId`.
- `aggregateId`.
- Payload de cada evento.

La documentación reporta recuperación menor a un segundo con aproximadamente 1,000 eventos.

## EAC-04 — Escalabilidad

Revisar:

```text
documentacion/docs/ESCALABILIDAD.md
```

Mostrar:

- Réplicas de `ordenes-api`.
- Réplicas de `auditoria-worker`.
- Uso de `SKIP LOCKED`.
- Colas quorum.
- Deduplicación.
- Ausencia de estado local en memoria.
- Configuración por variables de entorno.

## EAC-05 — Consistencia de inventario

Mostrar la prueba de reservas concurrentes y la explicación del bloqueo optimista.

---

# 6. Cambios justificados según feedback de Fase 1

Durante la exposición, conectar cada cambio con la decisión y el ADR correspondiente.

| Cambio | Evidencia | Decisión |
|---|---|---|
| Docker Compose para el MVP | `SGO-Logic/docker-compose.yml` | ADR-013 |
| Outbox y desacople | `SgoLogic.BuildingBlocks/Outbox/` | ADR-003 y ADR-014 |
| Saga por eventos | Consumidores de Órdenes | ADR-016 |
| Mocks de sistemas externos | `Mocks/` | ADR-019 |
| JWT en el borde | `ApiGateway` | ADR-020 |
| Compensación de stock | Servicios de Inventario | ADR-022 |
| Migraciones EF | `documentacion/docs/MIGRACIONES-BASELINE.md` | Cambios de Fase 2 |
| Auditoría con permisos y trigger | `documentacion/docs/AUDITORIA.md` | ADR-006 y implementación de Fase 2 |

### Preguntas que debe poder responder el equipo

- ¿Qué supuesto de Fase 1 falló?
- ¿Qué cambio de código fue necesario?
- ¿Cómo se verificó que el cambio no rompía el diseño?
- ¿Qué evidencia quedó versionada?
- ¿Qué deuda técnica queda para producción?

---

# 7. Gestión Scrum y documentación

## 7.1 Daily standups

Mostrar `documentacion/docs/bitacora.md`.

Cada registro debe responder:

1. ¿Qué se materializó ayer?
2. ¿Qué resultado se obtuvo?
3. ¿Qué se implementará hoy?
4. ¿Cuál fue el impedimento?

## 7.2 Sprint Planning

Mostrar:

- `Plan_Tablero_Trello.md`.
- `Backlog_Historias_Usuario.md`.
- Captura inicial `TrelloInicial.png`.
- Priorización por olas.
- Columnas del tablero.

## 7.3 Retrospectiva

Mostrar la sección de retrospectiva de cada integrante en `bitacora.md`.

Debe explicar:

- Decisiones consolidadas.
- Supuestos fallidos.
- Refactorizaciones de emergencia.
- Mejoras futuras.

## 7.4 Burndown

Mostrar la tabla y el gráfico Mermaid de `bitacora.md`.

Resultado documentado:

- 47 tarjetas en `Done`.
- 0 pendientes.
- Evidencia de avance por fecha.

## 7.5 Release

```bash
git branch -a
git log --oneline --decorate -n 20
git log --all --oneline --grep="#carnet"
```

Mostrar commits, merges y releases cuando estén disponibles.

---

# 8. Demostración final de extremo a extremo

La demostración final debe seguir el camino feliz:

1. Usuario demo.
2. Carrito.
3. Confirmación de orden.
4. Reserva de inventario.
5. Pago.
6. XML fiscal.
7. Guía de despacho.
8. Estados de entrega.
9. Consulta de estado.
10. Auditoría.
11. Sincronización ERP.
12. Falla de un mock externo.
13. Reintento o estado pendiente.
14. Confirmación de que no se perdió la orden.

### Script recomendado

```powershell
.\scripts\pruebas\camino-feliz.ps1
```

La evidencia documentada termina con:

```text
RESULTADO=APROBADO
```

y una orden en estado `ENTREGADA`.

### Para la demostración de factura y ERP

Ejecutar además:

```bash
docker compose logs facturacion-api
docker compose logs sat-mock
docker compose logs integracion-erp-worker
curl http://localhost:4000/erp/lotes
```

La presentación debe mostrar el archivo o payload real, no solo indicar que el servicio lo genera.

---

# 9. Limitaciones que deben declararse

1. El MVP usa Docker Compose y no tiene HPA de Kubernetes.
2. RabbitMQ es una instancia única, aunque utiliza colas quorum.
3. PostgreSQL no está replicado como clúster.
4. La disponibilidad real de 99.9 % no se puede probar en una corrida local corta.
5. Los proveedores externos son mocks.
6. El catálogo comercial permanece como dependencia externa.
7. Los clientes móvil, call center y POS físicos están representados dentro del diseño, pero la UI ejecutable principal es la Web demo.

Presentar estas limitaciones como límites conscious del alcance del MVP, con una ruta identificada para producción.

---

# 10. Checklist final

## Antes de iniciar

- [ ] Docker está encendido.
- [ ] `docker compose ps` muestra servicios saludables.
- [ ] Gateway responde `200`.
- [ ] Los mocks están en modo normal.
- [ ] El usuario demo está verificado.
- [ ] Hay existencias disponibles.
- [ ] El código de la ruta ERP está preparado.

## Para cada prueba

- [ ] Mostrar primero el requisito de la rúbrica.
- [ ] Mostrar el diagrama o ADR relacionado.
- [ ] Mostrar el archivo o componente de código.
- [ ] Ejecutar la prueba.
- [ ] Mostrar la respuesta o el archivo generado.
- [ ] Consultar la evidencia persistente.
- [ ] Explicar el criterio de aceptación.

## Al finalizar

- [ ] Restaurar mocks a modo normal.
- [ ] Mostrar RabbitMQ y DLQ si se 】 utilbzaron fallas.
- [ ] Mostrar Grafana o métricas.
- [ ] Mostrar evidencia de Scrum.
- [ ] Mencionar limitaciones.
- [ ] Responder preguntas individuales de cada integrante.

---

# 11. Conclusión

La forma más fuerte de defender este proyecto es demostrar que cada punto de la rúbrica sigue una cadena completa:

```text
Rúbrica
   ↓
Requisito y actor
   ↓
DDA / ADR / CU
   ↓
Diagrama y modelo de datos
   ↓
Servicio y código
   ↓
Prueba ejecutable
   ↓
Evidencia observable
   ↓
Criterio de aceptación cumplido
```

Para emitir factura, por ejemplo, no debe demostrarse únicamente que existe un servicio de Facturación: debe verse el XML, su CUI y serie, su relación con la orden y el envío al `sat-mock`.

Para sincronizar ventas con ERP, no debe mostrarse únicamente el código del worker: debe verse el archivo CSV/XML, su envío al `mock-erp`, la respuesta recibida y el registro de conciliación.

Esa es la diferencia entre una presentación descriptiva y una sustentación trazable y verificable frente a la rúbrica.
