using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Credito, contra su propia base (sgo_credito en
/// db-finanzas) y su propio esquema (credito). Trae mensajes_procesados porque
/// consume el historial del ERP y outbox_messages porque publica
/// PerfilCrediticioActualizado.
/// </summary>
public sealed class CreditoDbContext : DbContext
{
    public const string EsquemaPorDefecto = "credito";

    public CreditoDbContext(DbContextOptions<CreditoDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<HistorialCliente> Historiales => Set<HistorialCliente>();

    public DbSet<PerfilCrediticio> Perfiles => Set<PerfilCrediticio>();

    public DbSet<LineaCredito> Lineas => Set<LineaCredito>();

    public DbSet<CuentaPorCobrar> CuentasPorCobrar => Set<CuentaPorCobrar>();

    public DbSet<DecisionCredito> Decisiones => Set<DecisionCredito>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CreditoDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());

        // Igual que en Pagos y Ordenes: el publicador transversal barre
        // outbox_messages sin calificar esquema, asi que vive en public.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
