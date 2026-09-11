using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Inventario. Contra su propia base (sgo_inventario
/// en db-operacional) y su propio esquema (inventario); ningun otro servicio
/// escribe estas tablas (ADR-006). Trae ambas tablas transversales:
/// mensajes_procesados porque este servicio consume <c>ordenes.reserva.solicitada</c>,
/// y outbox_messages porque publica <c>inventario.reserva.*</c>.
/// </summary>
public sealed class InventarioDbContext : DbContext
{
    public const string EsquemaPorDefecto = "inventario";

    public InventarioDbContext(DbContextOptions<InventarioDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<Existencia> Existencias => Set<Existencia>();

    public DbSet<Reserva> Reservas => Set<Reserva>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);

        // Secuencia que numera las reservas: alimenta el id RSV-<anio>-<correlativo
        // de 6 digitos>.
        modelBuilder.HasSequence<long>("reserva_correlativo", EsquemaPorDefecto);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventarioDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());

        // La tabla outbox vive fuera del esquema propio del contexto: el publicador
        // transversal (OutboxPublisher) la barre con un "SELECT * FROM
        // outbox_messages" sin calificar esquema, igual que en Ordenes y Pagos.
        // mensajes_procesados si puede quedar en el esquema del contexto porque
        // EventConsumerBase la consulta por LINQ, no por SQL crudo.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
