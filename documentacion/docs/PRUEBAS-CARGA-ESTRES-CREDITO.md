# Pruebas de carga y estrés — ruta de crédito e impacto en Inventario

**Responsable:** José Pirir (202300335)
**Proyecto:** SGO-Logic — Fase 3
**Ubicación de la suite:** `SGO-Logic/scripts/pruebas-carga/credito-inventario/`

---

## 1. Propósito y alcance

El enunciado de la Fase 3 pide un mínimo de **5 pruebas de carga** y **5 pruebas de
estrés**. Esta suite las implementa sobre la funcionalidad nueva de la fase, la
campaña "Compra ahora, paga en 30 días". Pone el foco en lo que la ruta de crédito
le hace al inventario.

Responde tres requisitos del enunciado y del DDA:

- *"La orden no puede quedar congelada en el checkout esperando una respuesta"*: ni el
  checkout ni el stock pueden quedar retenidos sin límite mientras Crédito decide.
- *"Sin que ninguna de las dos rutas se convierta en un obstáculo para la otra"*
  (EV-58): crédito y PayKlik conviven en el pico sin degradarse entre sí.
- **EAC-06** (hoy *PENDIENTE DE EVIDENCIA* en el DDA): la elegibilidad responde en
  milisegundos y ningún timeout de Crédito impide comprar con PayKlik.

Las pruebas corren contra el sistema levantado (`docker compose up -d`), no contra el
código. Es el mismo criterio que EAC-01 a EAC-05 (ver `UMBRALES-DOD.md`).

---

## 2. Por qué el crédito afecta al inventario

La saga reserva el stock **antes** de preguntarle a Crédito. Lo fijan ADR-016 y la
precondición de CU-18: *"Órdenes tiene una reserva confirmada… y seleccionó
`CREDITO`"*. El flujo es:

```
crear -> confirmar -> RESERVA_SOLICITADA -> INVENTARIO_RESERVADO -> EVALUACION_CREDITO_SOLICITADA
                                              (stock apartado)              |
                                      CreditoAprobado  <--------------------+-------------> CreditoRechazado / timeout 30 s
                                      CONFIRMADA_PENDIENTE_FISCAL                            RECHAZADA + LiberacionReservaSolicitada
                                      (la reserva sigue)                                     (el stock vuelve)
```

De ahí salen cuatro efectos medibles. Cada prueba mide alguno:

| Efecto | Qué se mide | Dónde |
|---|---|---|
| **Retención durante la decisión** | Segundos entre `INVENTARIO_RESERVADO` y el resultado de Crédito (p50/p95/máx), y unidades esperando a Crédito al cierre de cada minuto | `ordenes.transicion_orden` |
| **Retención estéril** | Unidades y unidad·segundo apartadas por compras a crédito que **al final se rechazaron**, desde que Inventario creó la reserva hasta el evento `ReservaInventarioLiberada` | `inventario.reserva` + Auditoría |
| **Ventas perdidas por retención** | Compradores con tarjeta que recibieron "sin stock" mientras había unidades apartadas por un crédito que luego se rechazó, y unidades que quedaron sin vender | cruce de las anteriores |
| **Integridad** | Fugas (reservas activas de órdenes rechazadas), huecos, sobreventa, cuadre `existencia.cantidad_reservada`, líneas sobre el límite, cargos huérfanos | las cuatro bases |

El timeout de la saga (`Credito:TimeoutEvaluacionSegundos` = 30, revisado cada 5 s)
es lo único que acota la retención cuando Crédito no responde. Por eso el umbral de
retención máxima es 45 s: 30 s del timeout, 5 s del ciclo y 10 s de margen.

---

## 3. Las 10 pruebas

### Carga (condiciones esperadas)

El volumen sale de la misma derivación de `PRUEBA-CARGA-EAC01.md`: 32 tiendas a 1
orden cada 3 minutos dan ≈ 11 órdenes/min en un día normal. El pico 8x da
80 órdenes/min.

| ID | Script | Pregunta | Carga | Criterio de aprobación |
|---|---|---|---|---|
| **C1** | `c1-linea-base-credito.js` | ¿Cuánto tarda Crédito y cuánto se retiene el stock en un día normal? | 11 órdenes/min, 100% crédito elegible, 5 min | checkout p95 < 500 ms; decisión p95 ≤ 5 s; 0 rechazos; integridad |
| **C2** | `c2-pico-mixto.js` | ¿Crédito y tarjeta se estorban en el pico? | 80/min (40 crédito + 40 tarjeta), 5 min | checkout p95 < 1 s **por ruta**; 0 rechazos en ambas; decisión p95 ≤ 5 s; integridad |
| **C3** | `c3-pico-rechazos.js` | ¿Cada rechazo de crédito devuelve su stock, y rápido? | 80/min crédito, 50% no elegible, 5 min | todos los no elegibles RECHAZADA; liberación p95 ≤ 5 s tras el rechazo; 0 rechazos sin evento de liberación; integridad |
| **C4** | `c4-checkout-lectura.js` | (EAC-06) ¿Elegibilidad y disponibilidad responden en ms con el pico escribiendo? | 30 usuarios en checkout + 80/min de compras a crédito, 4 min | elegibilidad p95 < 300 ms; disponibilidad p95 < 300 ms; criterios de C3 |
| **C5** | `c5-agotamiento-cupo.js` | ¿Crédito corta en el límite y los rechazos por cupo devuelven stock? | 40/min, 20 clientes REGULAR (Q1 500) comprando Q500, 4 min | ≥ 1 rechazo por cupo; 0 líneas sobre el límite; saldo = Σ cuentas vigentes; integridad |

### Estrés (por encima de lo esperado)

| ID | Script | Pregunta | Carga | Criterio de aprobación |
|---|---|---|---|---|
| **E1** | `e1-rampa-quiebre.js` | ¿Dónde se rompe la decisión de crédito? | escalones de 60 s: 40 → 80 → 160 → 320 → 480 → 640 /min, 70% crédito | integridad tras el drenado. El **punto de quiebre** (primer minuto con decisión p95 > 5 s o timeouts) se reporta como hallazgo |
| **E2** | `e2-pico-subito.js` | ¿Absorbe una avalancha sin rampa y se recupera solo? | 10/min → **600/min** en 5 s durante 60 s → 10/min durante 120 s | integridad; decisión p95 de los últimos 120 s ≤ 5 s (recuperación) |
| **E3** | `e3-credito-caido.js` | ¿Qué pasa si Crédito se cae en pleno pico? | 80/min (50/50); `credito-api` detenido de t = 90 s a t = 180 s | 0 rechazos de tarjeta (EAC-06); ≥ 1 timeout; retención máx ≤ 45 s; 0 cargos huérfanos al volver; integridad |
| **E4** | `e4-stock-escaso.js` | ¿Cuántas ventas con tarjeta se pierden porque el crédito retiene las últimas unidades? | 40 unidades; 60 compras a crédito que se rechazarán + 60 con tarjeta en 30 s. Control: `SOLO_TARJETA=1` | integridad (0 sobreventa, 0 fugas). El impacto se reporta como hallazgo |
| **E5** | `e5-rafaga-mismo-cliente.js` | ¿Una ráfaga del mismo cliente sobregira la línea o fuga stock? | 5 clientes REGULAR × 20 compras de Q200 simultáneas (100 VUs a la vez) | 0 líneas sobre el límite (máx. 7 aprobadas por cliente); ≥ 1 rechazo por cupo; integridad |

**Integridad** (se exige en las 10): 0 órdenes atascadas en un paso intermedio,
0 sobreventa, `cantidad_reservada` igual a la suma de reservas ACTIVA/CONFIRMADA e
igual a lo que deberían retener las órdenes vivas, 0 fugas, 0 huecos, 0 líneas sobre
el límite, 0 líneas incoherentes y 0 cargos huérfanos.

---

## 4. Cómo correrlas

### Requisitos

- El stack arriba desde `SGO-Logic/`: `docker compose up -d`, con todos los servicios
  `healthy`.
- Docker y **bash**. En Windows sirve Git Bash, el mismo que usan los demás `.sh` del
  repositorio. El orquestador ya resuelve las rutas de Git Bash para `docker run`.
- No hace falta instalar k6: corre en contenedor (`grafana/k6`) dentro de la red
  `sgo-logic_default`.
- Memoria: las pruebas de estrés empujan el stack. Reserva los 8 GB que pide el README
  y no corras dos pruebas a la vez.

### Comandos (desde `SGO-Logic/`)

```bash
bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c1        # una prueba
bash scripts/pruebas-carga/credito-inventario/ejecutar.sh carga     # C1..C5  (~35 min)
bash scripts/pruebas-carga/credito-inventario/ejecutar.sh estres    # E1..E5  (~30 min)
bash scripts/pruebas-carga/credito-inventario/ejecutar.sh todas     # las 10  (~65 min)

# Control de E4 (solo tarjeta), para comparar contra la corrida normal:
SOLO_TARJETA=1 bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e4

# Cualquier parámetro de los scripts se puede ajustar por variable de entorno:
ORDENES_POR_MINUTO=120 bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c2
```

Por cada prueba, `ejecutar.sh` hace lo siguiente:

1. Genera el `RUN_ID` (`<prueba>-<MMddHHmmss>`) y lee `JWT_SECRET` de `ordenes-api`.
2. Corre el script k6. En E3, además, detiene y levanta `credito-api` a mitad de la
   carga.
3. Cada 10 s muestrea CPU y memoria de los contenedores clave y los mensajes en las
   colas de crédito, inventario y órdenes. Es la parte de "consumo de recursos" que
   pide el enunciado.
4. Espera el drenado: cero órdenes de la corrida en un paso intermedio y las colas
   vacías (máximo 10 min).
5. Corre `verificar.sh` y deja la evidencia en `scripts/pruebas/evidencia/`:
   - `cci-<RUN_ID>-k6.txt`: salida completa de k6, con sus umbrales.
   - `cci-<RUN_ID>-verificacion.txt`: medidas, tabla por minuto, recursos y
     `RESULTADO=`.
   - `cci-<RUN_ID>-recursos.txt`: muestras crudas de `docker stats`.
   - `cci-resumen.txt`: una línea por prueba con su veredicto.

Para repetir la verificación de una corrida ya hecha:

```bash
RUN_ID=c3-1011170512 bash scripts/pruebas-carga/credito-inventario/verificar.sh
```

### Cómo leer el resultado

Hay dos veredictos por prueba, y los dos quedan en `cci-resumen.txt`:

- **Umbrales de k6**: lo que ve el cliente en el checkout (latencia y éxito de crear y
  confirmar). Está en el bloque `THRESHOLDS` de `-k6.txt`.
- **Verificación**: lo que pasó después en la saga y en las bases (decisión, retención,
  liberación e integridad). Es la línea `RESULTADO=` de `-verificacion.txt`.

En E1 y E2 los umbrales de k6 por etapa o fase pueden fallar a propósito, porque se
busca el límite. El veredicto de esas pruebas es el de la verificación, y el punto de
quiebre se lee en `primer_minuto_degradado` y en la tabla por minuto.

---

## 5. Umbrales y de dónde salen

| Umbral | Valor | Justificación |
|---|---|---|
| Checkout (crear + confirmar) p95, carga normal / pico | 500 ms / 1 s | El checkout no depende de Crédito (la evaluación es asíncrona). Si se acerca al segundo, algo síncrono se coló en la ruta |
| Decisión de crédito p95 | ≤ 5 s | Mismo límite de 5 s que EAC-02 fija para que el cliente sepa el estado de su orden; el DDA pide una decisión "rápida" (EV-56) |
| Retención máxima por crédito | ≤ 45 s | Timeout de la saga de 30 s + ciclo del worker de 5 s + 10 s de margen. Si se pasa, el timeout no está acotando la retención |
| Liberación tras el rechazo p95 | ≤ 5 s | Un rechazo debe devolver el stock en el mismo orden de magnitud que la decisión |
| Elegibilidad / disponibilidad p95 | < 300 ms | EAC-06: "la consulta local responde en milisegundos" |
| Integridad | 0 | No es negociable: cualquier fuga, sobreventa o cargo huérfano rechaza la prueba |

Los cuatro umbrales de tiempo de la verificación se pueden cambiar por variable de
entorno (`UMBRAL_DECISION_P95`, `UMBRAL_RETENCION_MAX`, `UMBRAL_LIBERACION_P95`,
`VENTANA_RECUPERACION_SEG`). Si se cambian, se documenta junto a la evidencia.

---

## 6. Decisiones de diseño de la suite

- **Un SKU propio por corrida** (`SKU-CCI-<RUN_ID>`). Así las cuentas de inventario de
  la verificación no se mezclan con la demo ni con otras pruebas, y "stock retenido"
  significa exactamente lo que retuvo esta corrida.
- **Clientes de crédito sintéticos**, con prefijo `cci-<RUN_ID>-`. Sus historiales se
  cargan con `POST /interno/credito/historiales` (mismo cuerpo que
  `HistorialClienteErpActualizado`, ADR-023) y copian a clientes demo de
  comportamiento conocido:
  - `b` = cliente1, elegible.
  - `r` = cliente3, REGULAR, Q1 500.
  - `x` = cliente6, con un incidente grave, que es un descarte directo de la política.

  Cientos de clientes distintos evitan que el cupo de un solo cliente distorsione las
  pruebas que no son de cupo.
- **JWT firmado por la prueba.** Órdenes y Crédito solo validan la firma HS256 con
  `Auth:JwtSecret` y leen el claim `email` (`Program.cs` de ambos, sin
  issuer/audience). Registrar cientos de usuarios reales exige el código de
  verificación que `auth-api` escribe en sus logs, y eso no escala. El login real lo
  cubren `credito-saga.ps1` y las pruebas E2E. Lo que se mide aquí es Órdenes →
  Inventario → Crédito.
- **La verdad está en las bases, no en k6.** k6 mide el checkout; la saga es
  asíncrona. `verificar.sh` extrae lo de la corrida de `sgo_inventario`,
  `sgo_auditoria` y `sgo_credito`, lo carga como tablas temporales en una sesión de
  `sgo_ordenes` y cruza todo en SQL (`lib/analisis.sql`). No necesita Python ni jq en
  la máquina.
- **Se verifica después del drenado.** EAC-01 ya documentó que verificar antes de
  drenar da falsos "perdidos".

---

## 7. Qué queda fuera

- **La latencia real del ERP.** Crédito decide con el perfil precalculado (ADR-023);
  el lote del ERP no participa en la ruta crítica, y probarlo es tarea de
  Integración ERP.
- **El autoescalado.** Igual que en EAC-01, el MVP corre una instancia por servicio en
  docker compose (ADR-013). El escalado horizontal ya lo cubre EAC-04
  (`ESCALABILIDAD.md`).
- **Despacho y entrega.** Las órdenes aprobadas se dejan en
  `CONFIRMADA_PENDIENTE_FISCAL` o más allá, con la reserva retenida: es lo correcto
  para una orden vendida y no despachada.
- **El 40% de aumento del ticket.** Es una expectativa de negocio, no un atributo del
  sistema. C5 y E5 sí ejercitan compras de ticket alto contra el cupo.

---

## 8. Validación previa del arnés

Antes de correr la suite contra el stack, los scripts se ejercitaron de punta a punta
contra un simulador local de la saga. El simulador reproduce los contratos HTTP de
Órdenes, Inventario, Crédito y Pagos, y las tablas que lee el verificador, sobre
PostgreSQL 16. Con k6 v1.4.0 (y compatibilidad confirmada con v0.54.0) se comprobó:

- que el JWT firmado se acepta;
- que las diez pruebas generan su carga y la verificación las aprueba;
- que, **con una fuga de stock inyectada a propósito** (1 de cada 5 liberaciones
  omitida), la verificación de C3 sale `RECHAZADO` con `fugas_stock`,
  `inventario_dif_existencia_vs_ordenes` y `rechazos_sin_evento_liberacion`
  distintos de cero.

Esto valida el arnés, no el sistema. Los números de la sección 9 salen solo de
corridas contra el stack real.

Hay cuatro supuestos sobre el sistema real que conviene confirmar en la primera
corrida. Si alguno falla, el síntoma es claro:

1. El `payload` de `ReservaInventarioLiberada` en `eventos_auditoria` trae `ordenId`
   en camelCase, igual que los eventos de Pagos que ya consulta
   `eac01-hu023-verificar.sh`. Síntoma si no: `rechazos_sin_evento_liberacion` igual
   a todos los rechazos.
2. Los clientes copiados de cliente3 quedan REGULAR (Q1 500) con
   `CREDITO_PUNTAJE_MINIMO=50`. Los logs del `setup()` imprimen la elegibilidad de un
   cliente de muestra. C5 está dimensionada para agotar el cupo aun si quedaran
   BUENO (Q3 500).
3. `ordenes-api` expone `Auth__JwtSecret` como variable de entorno (así lo define
   `docker-compose.yml`). Síntoma si no: `ejecutar.sh` se detiene al inicio.
4. La red del compose se llama `sgo-logic_default`. Si no, se pasa `RED=...`.

---

## 9. Resultados

Corrida del 11 de octubre de 2026 contra el stack local (`docker compose up -d --build`,
una instancia por servicio, Docker Desktop con 16 CPU y 7.7 GB). La evidencia está en
`SGO-Logic/scripts/pruebas/evidencia/cci-<RUN_ID>-*.txt`.

| Prueba | RUN_ID | k6 | Verificación | Decisión crédito p95 | Retención máx | Retención estéril (unid · unid·s) | Hallazgo |
|---|---|---|---|---|---|---|---|
| C1 | c1-1011215306 | cumplidos | APROBADO | 1.00 s | 1.07 s | 0 · 0 | 56 órdenes, todas aprobadas; checkout p95 62.7 ms |
| C2 | c2-1011215848 | cumplidos | APROBADO | 1.08 s | 1.23 s | 0 · 0 | 190 a crédito + 190 con tarjeta, 0 rechazos en ambas rutas |
| C3 | c3-1011220513 | cumplidos | APROBADO | 1.05 s | 1.22 s | 189 · 281.6 | 189 rechazos, todos con su liberación; liberación p95 0.67 s |
| C4 | c4-1011221136 | cumplidos | APROBADO | 1.05 s | 1.55 s | 160 · 241.9 | elegibilidad p95 16.7 ms, disponibilidad p95 14.2 ms (EAC-06) |
| C5 | c5-1011221701 | cumplidos | APROBADO | 0.98 s | 1.14 s | 505 · 684.1 | 101 rechazos por `CUPO_INSUFICIENTE`; uso máximo de línea 100%, 0 líneas sobre el límite |
| E1 | e1-1011222224 | cumplidos | APROBADO | 34.46 s | 35.56 s | 370 · 26 385.5 | punto de quiebre: escalón de 320/min (p95 5.83 s). A 640/min aparecen 370 timeouts |
| E2 | e2-1011223130 | cumplidos | **RECHAZADO** | 22.56 s | 23.24 s | 0 · 0 | recuperación: p95 de los últimos 120 s = 22.00 s (> 5 s). Vuelve a 1.04 s en el minuto 4 |
| E3 | e3-1011223657 | cumplidos | APROBADO | 33.65 s | 34.84 s | 42 · 1 396.5 | timeouts / cargos anulados: 42 / 42. 0 rechazos de tarjeta, 0 cargos huérfanos |
| E4 | e4-1011224322 (control: e4-1011224629) | cumplidos | APROBADO | 1.76 s | 1.91 s | 54 · 137.6 | ventas perdidas vs control: 0 (40 unidades vendidas con tarjeta en ambas corridas) |
| E5 | e5-1011224518 | **NO cumplidos** | APROBADO | 3.84 s | 4.32 s | 126 · 1 118.1 | máx. aprobadas por cliente: 7 (el tope). 2 de 100 creaciones respondieron HTTP 500 |

En las once corridas la integridad quedó en cero: sin sobreventa, fugas, huecos, líneas
sobre el límite ni cargos huérfanos.

### Hallazgos

1. **La saga se atrasa a partir de ~320 órdenes/min (E1, E2).** Con 80/min la decisión
   de crédito tarda 1 s; a 320/min sube a 5.8 s, a 480/min a 28 s y a 640/min a 35 s.
   El atraso no es de Crédito: en E2 las órdenes con tarjeta tardaron lo mismo (p95
   22.69 s contra 22.56 s). Tampoco es de recursos: las APIs no pasaron de 42% de CPU,
   `db-operacional` llegó a 59% y las colas de RabbitMQ nunca tuvieron más de 4
   mensajes. (El contenedor de RabbitMQ marca picos de 330% a 380% de CPU en todas las
   corridas, incluida C1 a 11 órdenes/min: lo causa el `rabbitmqctl` del muestreo, no
   la carga.) El checkout no se entera (p95 de
   39.8 ms en el pico de E2). Hipótesis por confirmar: el ritmo lo limita el despacho
   por lotes del outbox (`OUTBOX_POLLING_MS`, `OUTBOX_BATCH_SIZE`).
2. **E2 no cumple el criterio de recuperación.** Tras 60 s a 600/min, las órdenes que
   entran en el minuto siguiente heredan la cola (p95 21.58 s). El sistema se recupera
   solo y sin pérdidas (680 de 680 órdenes terminadas, 0 timeouts), pero tarda cerca de
   un minuto y la ventana de 120 s lo registra. Se deja rechazada.
3. **Con la saga saturada, el timeout rechaza compras que Crédito sí aprobó (E1).** En
   el escalón de 640/min, 370 órdenes vencieron los 30 s con Crédito arriba. Las 370
   cuentas por cobrar quedaron `ANULADA` y su stock se liberó (p95 43.26 s): la
   compensación funciona, pero son ventas rechazadas por lentitud y no por riesgo.
4. **Crédito caído no afecta a PayKlik (E3, EAC-06).** Con `credito-api` detenido 90 s,
   las 200 compras con tarjeta se completaron. Las 42 de crédito que cayeron en la
   ventana vencieron por timeout, con retención máxima de 34.84 s (umbral 45 s). Al
   volver Crédito la decisión regresó a 1.13 s.
5. **Reservar antes de evaluar el crédito no costó ventas (E4).** 20 compradores con
   tarjeta recibieron "sin stock" mientras un crédito luego rechazado retenía
   unidades, pero la demanda era de 60 para 40 unidades: el control sin crédito
   rechazó a los mismos 20. El stock retenido volvió en 1.38 s (p95) y se vendió.
6. **Una ráfaga de 100 peticiones simultáneas agota las conexiones de PostgreSQL
   (E5).** Dos `POST /api/ordenes` respondieron 500 por `53300: too many clients
   already` en `db-operacional`. La línea de crédito no se sobregiró: 7 aprobadas por
   cliente, que es el tope de Q1 500 / Q200. Por eso el umbral de k6 falla (98% de
   éxito contra 99%) y la verificación aprueba.
7. **`auditoria-worker` no sobrevive un arranque en frío.** Al levantar el stack salió
   con `BrokerUnreachableException` porque RabbitMQ aún no aceptaba conexiones, y no
   tiene política de reinicio. Se levantó a mano antes de la corrida.

Los supuestos de la sección 8 se confirmaron: `ordenId` llega en camelCase a Auditoría,
los clientes `r` quedan con Q1 500 de límite, `Auth__JwtSecret` se lee de `ordenes-api`
y la red es `sgo-logic_default`.

### Cómo convertir E4 en una conclusión de arquitectura

Para comparar E4 con su control (`SOLO_TARJETA=1`) hay que mirar tres valores:

- `unidades_vendidas_tarjeta`: en el control debería ser igual al stock.
- `tarjetas_rechazadas_mientras_credito_retenia`.
- `ventas_tarjeta_perdidas_por_retencion`.

Si la corrida con crédito vende menos a tarjeta que el control, ese es el costo
medido de reservar antes de evaluar el crédito. Las opciones que abre ese resultado,
para discutir en el ADR que corresponda, son:

- **Filtrar antes de reservar.** El checkout ya oculta crédito a quien no es elegible
  (EAC-06). Es el mecanismo que más reduce los rechazos estériles.
- **Acortar el timeout de la saga.** Reduce la retención en el peor caso, pero sube
  los rechazos falsos cuando Crédito está lento.
- **Evaluar el crédito antes de reservar.** Elimina la retención estéril, pero obliga
  a compensar un cargo aprobado cuando después no hay stock.
