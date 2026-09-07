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

3. Agrega el proyecto a `SGO-Logic.sln`:

   ```bash
   dotnet sln SGO-Logic.sln add src/Services/SgoLogic.<Contexto>.Api/SgoLogic.<Contexto>.Api.csproj -s Services
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

La carpeta `Consumers/` viene vacía a propósito (con un `.gitkeep`). Es para los
consumidores de eventos RabbitMQ, que se agregan en una sesión posterior de
mensajería. No la uses todavía.

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

6. En `docker-compose.yml`, si tu servicio necesita su propia base (distinta de
   la de los ejemplos), agrega tu propia variable de conexión y, si el equipo
   decide separar bases por contexto, tu propio servicio de PostgreSQL. El
   compose mínimo de esta plantilla comparte una sola base entre los dos
   servicios de ejemplo solo para simplificar el arranque; **no repliques ese
   atajo para servicios reales**.

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
- No implementes el patrón Outbox, consumidores de RabbitMQ, ni nada de
  mensajería todavía: eso llega en una sesión posterior y ya tiene su propio
  lugar reservado (`Consumers/`).
- No copies el evento de ejemplo (`EjemploCreadoEvento`) como si fuera un
  contrato real: cuando tu contexto necesite publicar eventos, se define su
  propio contrato en `SgoLogic.Contracts`, siguiendo el mismo envelope
  (`IntegrationEvent<TData>`), pero con su propio tipo de dato y su propio
  `eventType`.

## 6. Checklist rápido

- [ ] Carpeta copiada y renombrada (`SgoLogic.<Contexto>.Api` o `.Worker`).
- [ ] Proyecto agregado a `SGO-Logic.sln`.
- [ ] Namespace renombrado en todos los archivos.
- [ ] Entidad y controlador de ejemplo eliminados; los tuyos en su lugar.
- [ ] `DbContext` propio, con su configuración de entidades y su migración inicial.
- [ ] `Dockerfile` con las rutas de tu servicio.
- [ ] Servicio agregado a `docker-compose.yml` con su propia variable de conexión.
- [ ] `dotnet build` y `dotnet test` en verde antes de abrir el PR.