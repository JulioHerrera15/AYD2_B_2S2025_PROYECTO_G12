using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Pagos. Contra su propia base (sgo_pagos en
/// db-finanzas) y su propio esquema (pagos). Trae ambas tablas transversales:
/// mensajes_procesados porque este servicio consume, y outbox_messages porque
/// publica.
/// </summary>
public sealed class PagosDbContext : DbContext
{
    public const string EsquemaPorDefecto = "pagos";

    public PagosDbContext(DbContextOptions<PagosDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<Pago> Pagos => Set<Pago>();

    public DbSet<IntentoPago> Intentos => Set<IntentoPago>();

    public DbSet<EnlaceReintento> EnlacesReintento => Set<EnlaceReintento>();

    /// <summary>Tarjetas guardadas: solo referencia de boveda de PayKlik y metadatos, nunca PAN ni CVV.</summary>
    public DbSet<TarjetaGuardada> TarjetasGuardadas => Set<TarjetaGuardada>();

    /// <summary>Boveda transitoria: tokens con PAN y CVV cifrados con AES-GCM mientras se cobra.</summary>
    public DbSet<TokenPago> TokensPago => Set<TokenPago>();

    /// <summary>Reembolsos por cancelacion de orden y de abonos a credito (los procesa un worker).</summary>
    public DbSet<Reembolso> Reembolsos => Set<Reembolso>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PagosDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());

        // La tabla outbox vive fuera del esquema propio del contexto: el
        // publicador transversal (OutboxPublisher) la barre con un
        // "SELECT * FROM outbox_messages" sin calificar esquema. Misma decision
        // que en Ordenes. mensajes_procesados si puede quedar en el esquema del
        // contexto porque EventConsumerBase la consulta por LINQ, no por SQL crudo.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
