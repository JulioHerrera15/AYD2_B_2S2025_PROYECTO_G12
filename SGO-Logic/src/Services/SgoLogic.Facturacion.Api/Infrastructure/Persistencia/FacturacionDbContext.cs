using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Facturacion.Api.Domain;

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Facturacion. Contra su propia base
/// (sgo_facturacion en db-finanzas) y su propio esquema (facturacion). Trae ambas
/// tablas transversales: mensajes_procesados porque este servicio consume, y
/// outbox_messages porque publica.
/// </summary>
public sealed class FacturacionDbContext : DbContext
{
    public const string EsquemaPorDefecto = "facturacion";

    /// <summary>Secuencia de PostgreSQL que numera los documentos fiscales (serie + correlativo).</summary>
    public const string SecuenciaCorrelativo = "documento_fiscal_correlativo";

    public FacturacionDbContext(DbContextOptions<FacturacionDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<DocumentoFiscal> DocumentosFiscales => Set<DocumentoFiscal>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);

        modelBuilder.HasSequence<long>(SecuenciaCorrelativo, EsquemaPorDefecto);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FacturacionDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());

        // La tabla outbox vive fuera del esquema propio del contexto: el publicador
        // transversal (OutboxPublisher) la barre con un "SELECT * FROM
        // outbox_messages" sin calificar esquema. Misma decision que en Pagos y
        // Ordenes. mensajes_procesados si puede quedar en el esquema del contexto
        // porque EventConsumerBase la consulta por LINQ, no por SQL crudo.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
