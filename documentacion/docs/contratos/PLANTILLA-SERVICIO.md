# Cómo crear un servicio nuevo a partir de la plantilla

Este documento es la guía para que cualquiera de los 12 microservicios del DDA
arranque copiando una de las dos plantillas (`SgoLogic.Sample.Api` o
`SgoLogic.Sample.Worker`) y renombrando. No hay generador ni script: es copiar
una carpeta y ajustar un puñado de archivos.

Elegí la plantilla `Api` si tu contexto expone endpoints HTTP (CRUD, consultas).
Elegí `Worker` si tu contexto solo reacciona a eventos o corre tareas de fondo,
sin exponer endpoints de negocio.

## 1. Copiar y renombrar

1. Copia la carpeta completa de la plantilla que corresponda:
   - `src/Services/SgoLogic.Sample.Api` → `src/Services/SgoLogic.<Contexto>.Api`
   - `src/Services/SgoLogic.Sample.Worker` → `src/Services/SgoLogic.<Contexto>.Worker`

   `<Contexto>` es el nombre del contexto delimitado tal como aparece en el DDA
   (ej. `Ordenes`, `Inventario`, `Facturacion`, `Logistica`).

2. Dentro de la carpeta copiada, renombra:
   - El archivo `.csproj` (`SgoLogic.Sample.Api.csproj` → `SgoLogic.<Contexto>.Api.csproj`).
   - El namespace raíz en todos los `.cs` (busca y reemplaza
     `SgoLogic.Sample.Api` por `SgoLogic.<Contexto>.Api`).
   - `UserSecretsId` si el `.csproj` lo trae (opcional, evita colisiones locales).

3. Agrega el proyecto a `SgoLogic.sln`:

   ```bash
   dotnet sln SgoLogic.sln add src/Services/SgoLogic.<Contexto>.Api/SgoLogic.<Contexto>.Api.csproj -s Services
   ```

4. Borra lo que trajo el ejemplo y no aplica a tu contexto:
   - La entidad `Domain/ItemEjemplo.cs` y todo lo que la usa
     (`Application/IItemEjemploService.cs`, `Application/ItemEjemploService.cs`,
     `Controllers/ItemsEjemploController.cs`,
     `Infrastructure/Configuraciones/ItemEjemploConfiguration.cs`).
   - La carpeta `Infrastructure/Migrations` (vas a generar tus propias
     migraciones desde cero, ver sección 3).
   - El evento de ejemplo (`SgoLogic.Contracts/Eventos/EjemploCreadoEvento.cs`)
     no se copia: es solo un ejemplo dentro de `Contracts`, no forma parte de la
     plantilla de servicio.

5. Ajusta el `Dockerfile` copiado: cambia las rutas `src/Services/SgoLogic.Sample.Api/...`
   por las de tu servicio, y el `.dll` del `ENTRYPOINT`.

La carpeta `Consumers/` es para tus consumidores de eventos RabbitMQ (clases que
heredan de `EventConsumerBase<TDbContext, TData>`, ver sección 7). Si tu
contexto no consume eventos, dejala vacía con el `.gitkeep` que trae.

## 2. Registrar los transversales (`AddSgoDefaults` / `UseSgoDefaults`)

La plantilla ya trae esto resuelto en `Program.cs`; al copiarla lo heredas tal
cual. Solo tenés que mantener el orden:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();      // Serilog JSON + transactionId, health checks, JSON compartido

// acá van tus servicios propios: DbContext, casos de uso, etc.

var app = builder.Build();

app.UseSgoDefaults();          // manejo de errores + correlación + /health/live y /health/ready

// acá tus middlewares/rutas propias (MapControllers, etc.)

app.Run();
```

No dupliques esta configuración manualmente (no vuelvas a registrar Serilog,
JsonSerializerOptions o health checks a mano): si necesitas algo adicional,
regístralo en `SgoLogic.BuildingBlocks` para que lo hereden todos los servicios,
no en el servicio individual.

## 3. Tu propio DbContext, tu propia base

**Regla dura: nadie escribe tablas en la base de datos de otro contexto.**
Cada servicio tiene su propio `DbContext` y corre sus propias migraciones contra
su propia base de datos. No hay una base compartida entre contextos, ni
siquiera "solo para leer".

1. Crea tu `DbContext` en `Infrastructure/<Contexto>DbContext.cs`, siguiendo el
   patrón de `SampleApiDbContext`: un `DbSet` por agregado, y
   `OnModelCreating` cargando las configuraciones con
   `modelBuilder.ApplyConfigurationsFromAssembly(...)`.

   Si tu contexto publica eventos, tu tabla outbox va en esta misma base (nunca
   centralizada) — ver sección 6. Si además consume eventos, lo mismo con la
   tabla de idempotencia — ver sección 7. Ninguna de las dos se agrega con
   `ApplyConfigurationsFromAssembly` porque viven en el ensamblado de
   `SgoLogic.BuildingBlocks`, no en el tuyo: se agregan a mano, como se explica
   en esas secciones.

2. Registra tu configuración de entidad en `Infrastructure/Configuraciones/`
   (una clase `IEntityTypeConfiguration<T>` por entidad, igual que
   `ItemEjemploConfiguration`).

3. Registra el `DbContext` en `Program.cs` apuntando a tu propia cadena de
   conexión (nunca hardcodeada, siempre desde configuración):

   ```csharp
   builder.Services.AddDbContext<TuContextoDbContext>(opciones =>
       opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
   ```

4. Genera tu migración inicial:

   ```bash
   dotnet ef migrations add Inicial \
     --project src/Services/SgoLogic.<Contexto>.Api/SgoLogic.<Contexto>.Api.csproj \
     --startup-project src/Services/SgoLogic.<Contexto>.Api/SgoLogic.<Contexto>.Api.csproj \
     --output-dir Infrastructure/Migrations
   ```

   (Necesitas la herramienta `dotnet-ef` instalada una vez:
   `dotnet tool install --global dotnet-ef`.)

5. La plantilla aplica las migraciones automáticamente al arrancar
   (`contexto.Database.Migrate()` en `Program.cs`, justo antes de `app.Run()`).
   Es el comportamiento del entorno de MVP con Docker Compose; no es un patrón
   de despliegue a producción, pero para este proyecto es suficiente.

6. En `docker-compose.yml`, agrega tu servicio apuntando a la base de datos que
   le corresponda según el ADR-006: `db-operacional`, `db-finanzas` o
   `db-auditoria` son los tres contenedores de PostgreSQL del entorno, cada uno
   con su propio volumen. Dentro de `db-operacional`, cada servicio tiene su
   **propia base** (no una compartida): `sample-api` y `sample-worker` son el
   ejemplo — corren en el mismo contenedor `db-operacional` pero contra
   `sgo_sample_api` y `sgo_sample_worker` respectivamente, dos bases distintas
   creadas por `scripts/postgres/crear-bases-adicionales.sh` vía la variable
   `POSTGRES_OPERACIONAL_MULTIPLE_DATABASES` en `.env`. Si tu contexto va en
   `db-operacional`, agregá el nombre de tu base a esa misma variable; si va en
   `db-finanzas` o `db-auditoria`, coordiná con quien sea dueño de ese
   contenedor. **Nunca apuntes tu `ConnectionStrings:Postgres` a una base que ya
   usa otro servicio.**

## 4. Configuración por variables de entorno

Nada de cadenas de conexión, URLs ni credenciales hardcodeadas en el código ni
en `appsettings.json`. Usa:

- `appsettings.json`: valores vacíos o placeholders no funcionales.
- `appsettings.Development.json`: valores cómodos para desarrollo local sin Docker.
- Variables de entorno (formato `Seccion__Clave`, ej. `ConnectionStrings__Postgres`)
  para todo lo demás, incluido Docker Compose vía `.env` (copia `.env.example`
  a `.env`, nunca subas `.env` al repo).

## 5. Qué NO traer de la plantilla

- No agregues un repositorio genérico, MediatR, AutoMapper ni una Clean
  Architecture de 4 proyectos. La plantilla es deliberadamente simple:
  `Controllers → Application → Domain/Infrastructure`, un solo `.csproj`.
- No copies el evento de ejemplo (`EjemploCreadoEvento` / `EjemploProcesadoEvento`)
  como si fuera un contrato real: cuando tu contexto necesite publicar eventos,
  se define su propio contrato en `SgoLogic.Contracts`, siguiendo el mismo
  envelope (`IntegrationEvent<TData>`), pero con su propio tipo de dato y su
  propio `eventType`.
- No publiques nunca directo al broker desde tu código de aplicación (con
  `IPublicadorMensajes` o un `IModel` a mano). Toda publicación pasa por
  `IOutboxWriter` — es una regla dura del envelope (sección 2, primera regla,
  de `sgo-eventos-envelope-v1.md`), no una preferencia de estilo.

## 6. Publicar un evento con Outbox

**Regla central: el evento se encola en la MISMA transacción que el cambio de
negocio.** `IOutboxWriter.Encolar(...)` solo agrega la fila a tu `DbContext`
(no llama a `SaveChangesAsync`); vos guardás todo junto.

1. Aplica la configuración de la tabla outbox a tu `DbContext`, en
   `OnModelCreating`, además de tus propias entidades:

   ```csharp
   protected override void OnModelCreating(ModelBuilder modelBuilder)
   {
       modelBuilder.ApplyConfigurationsFromAssembly(typeof(TuContextoDbContext).Assembly);
       modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
   }
   ```

2. En `Program.cs`, registra la mensajería y el outbox (en ese orden, para que
   la topología se declare antes de que corra el publicador):

   ```csharp
   builder.Services.AddSgoMessaging();
   builder.Services.AddOutbox<TuContextoDbContext>(builder.Configuration);
   ```

   Esto registra `IOutboxWriter` (inyectable donde lo necesites) y arranca
   `OutboxPublisher<TuContextoDbContext>` como `BackgroundService`: hace polling
   sobre tu tabla outbox (`Outbox:PollingIntervalMs`, default 2000ms) y publica
   lo pendiente, marcando `Publicado` solo tras el confirm del broker.

3. Agrega la sección `Outbox` a tu `appsettings.json` (al menos `Source`, tu
   contexto en minúscula — es el campo `source` del envelope):

   ```json
   "Outbox": {
     "Source": "tu-contexto",
     "PollingIntervalMs": 2000,
     "BatchSize": 20,
     "MaxReintentos": 5
   }
   ```

   Y la cadena `ConnectionStrings:RabbitMq` (URI amqp completa), igual que ya
   haces con `Postgres`.

4. Donde tengas el cambio de negocio, encola el evento y guardá todo junto:

   ```csharp
   public sealed class TuServicio
   {
       private readonly TuContextoDbContext _contexto;
       private readonly IOutboxWriter _outboxWriter;

       public async Task HacerAlgoAsync(...)
       {
           var entidad = new TuEntidad(...);
           _contexto.TusEntidades.Add(entidad);

           _outboxWriter.Encolar(
               "TuEvento.Ocurrido",       // eventType, PascalCase
               "TuAgregado",              // aggregateType
               entidad.Id.ToString(),     // aggregateId
               sequence: entidad.Secuencia, // la asigna TU agregado, no el outbox
               exchange: TopologiaMensajeria.ExchangeEventos,
               routingKey: "tu-contexto.tu-entidad.tu-hecho",
               data: new TuEventoData(...));

           await _contexto.SaveChangesAsync(); // entidad + outbox, una sola transaccion
       }
   }
   ```

   Fijate que nunca pasás `transactionId` ni `causationId`: `IOutboxWriter` los
   lee de `EventContext` (poblado por `CorrelationMiddleware` en una solicitud
   HTTP nueva, o por `EventConsumerBase` si esto corre dentro del manejo de un
   evento consumido — ver sección 7). Tampoco inventes el `sequence`: es
   responsabilidad de tu agregado llevar su propio contador monotónico, como
   `Orden.Secuencia` en el dominio de Órdenes.

   Rutas de exchange/routing key y cómo declarar tu propia cola de consumo (si
   además consumís) están en `docs/TOPOLOGIA-MENSAJERIA.md`, no acá.

5. Métricas de outbox (`outbox_pendientes`, `outbox_publicados_total`,
   `outbox_fallidos_total`, `outbox_latencia_publicacion_segundos`) salen solas
   en `/metrics` (`UseSgoDefaults()` ya expone el endpoint). No hay que
   instrumentar nada a mano.

## 7. Consumir un evento con EventConsumerBase

Si tu contexto reacciona a eventos de otro, escribí un consumidor en
`Consumers/`, heredando de `EventConsumerBase<TDbContext, TData>`:

```csharp
public sealed class MiConsumidor : EventConsumerBase<TuContextoDbContext, DatosDelEventoQueConsumis>
{
    public MiConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<MiConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "tu-contexto.evento-que-consumis";
    protected override string NombreServicio => "tu-contexto";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "contexto-emisor.entidad.hecho")
    ];

    protected override Task ManejarAsync(
        IntegrationEvent<DatosDelEventoQueConsumis> evento,
        TuContextoDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        // Tu logica. Si necesitas publicar un evento derivado, resolvelo de
        // "servicios" (mismo scope que "contexto") y encolalo con Encolar(...):
        // no llames a SaveChangesAsync aca, la clase base lo hace por vos junto
        // con la marca de procesado, en una sola transaccion.
        var outboxWriter = servicios.GetRequiredService<IOutboxWriter>();
        outboxWriter.Encolar(...);

        return Task.CompletedTask;
    }
}
```

Registralo en `Program.cs` como `IHostedService`, después de `AddSgoMessaging()`
y (si también vas a publicar un derivado) de `AddOutbox<TuContextoDbContext>(...)`:

```csharp
builder.Services.AddHostedService<MiConsumidor>();
```

Aplica también `MensajeProcesadoConfiguration` a tu `DbContext`, igual que
`OutboxMessageConfiguration` en la sección 6:

```csharp
modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
```

`EventConsumerBase` te da, sin que escribas nada de esto:

- **Deduplicación por `eventId`.** El broker entrega al menos una vez; antes de
  llamar a `ManejarAsync` chequea `mensajes_procesados` y, si ya está, hace ack
  y descarta sin reprocesar.
- **Correlación poblada.** Antes de `ManejarAsync`, `EventContext.TransactionId`
  queda en el `transactionId` recibido y `EventContext.CausationId` en el
  `eventId` recibido. Cualquier `IOutboxWriter.Encolar(...)` que llames adentro
  hereda eso automáticamente.
- **Ack/nack correcto.** Éxito hace ack. Excepción o `ManejarAsync` fallido hace
  nack con requeue (para que la cola quorum cuente la redelivery contra
  `x-delivery-limit` y, al agotarlo, mande sola el mensaje a dead letter — ver
  `docs/TOPOLOGIA-MENSAJERIA.md` sección 4.2).
- **Métricas.** `mensajes_consumidos_total` y `mensajes_duplicados_total` salen
  solas en `/metrics`.

`SgoLogic.Sample.Worker` (`Consumers/EjemploCreadoConsumidor.cs`) es el ejemplo
completo y funcionando: consume `EjemploCreadoEvento` y publica
`EjemploProcesadoEvento` como derivado.

## 8. Checklist rápido

- [ ] Carpeta copiada y renombrada (`SgoLogic.<Contexto>.Api` o `.Worker`).
- [ ] Proyecto agregado a `SgoLogic.sln`.
- [ ] Namespace renombrado en todos los archivos.
- [ ] Entidad y controlador de ejemplo eliminados; los tuyos en su lugar.
- [ ] `DbContext` propio, con su configuración de entidades y su migración inicial.
- [ ] Si publicás eventos: `OutboxMessageConfiguration` aplicada, `AddSgoMessaging()`
      + `AddOutbox<TuContextoDbContext>(...)` en `Program.cs`, sección `Outbox`
      en `appsettings.json` con tu `Source`.
- [ ] Si consumís eventos: `MensajeProcesadoConfiguration` aplicada, tu
      `EventConsumerBase<TDbContext, TData>` en `Consumers/` y registrado con
      `AddHostedService<TuConsumidor>()`.
- [ ] `ConnectionStrings:RabbitMq` configurado (vacío en `appsettings.json`,
      real en `appsettings.Development.json` y en `docker-compose.yml`).
- [ ] `Dockerfile` con las rutas de tu servicio.
- [ ] Servicio agregado a `docker-compose.yml`, con su propia base de datos
      (nunca compartida con otro servicio) y `depends_on` con
      `condition: service_healthy` sobre su Postgres y sobre `rabbitmq` si aplica.
- [ ] `dotnet build` y `dotnet test` en verde antes de abrir el PR.