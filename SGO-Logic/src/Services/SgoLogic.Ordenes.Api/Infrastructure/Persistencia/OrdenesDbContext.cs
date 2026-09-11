using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Ordenes.Api.Domain;
using MensajeProcesadoConfigurationBuildingBlocks = SgoLogic.BuildingBlocks.Idempotencia.MensajeProcesadoConfiguration;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Ordenes. Contra su propia base (sgo_ordenes) y su
/// propio esquema (ordenes); ningun otro servicio escribe estas tablas. Incluye la
/// tabla del outbox transversal de BuildingBlocks (nunca centralizada). Desde que
/// Ordenes consume los resultados de Pagos, trae ademas su propia tabla de
/// deduplicacion <c>ordenes.mensaje_procesado</c> (distinta de
/// <c>mensajes_procesados</c> de BuildingBlocks: esta lleva el tipo de evento).
///
/// <c>mensajes_procesados</c> de BuildingBlocks tambien debe mapearse aca: es la
/// tabla que <c>EventConsumerBase&lt;TDbContext, TData&gt;</c> consulta via
/// <c>contexto.Set&lt;MensajeProcesado&gt;()</c> para deduplicar (la usan, por
/// ejemplo, <see cref="Consumers.DocumentoFiscalCertificadoConsumidor" />). Sin
/// esta linea, <c>ApplyConfigurationsFromAssembly</c> solo recorre el ensamblado de
/// Ordenes.Api y nunca encuentra la configuracion de BuildingBlocks: el tipo queda
/// fuera del modelo y <c>Set&lt;MensajeProcesado&gt;()</c> lanza
/// <c>InvalidOperationException</c> en cada mensaje. FacturacionDbContext ya aplica
/// esta misma configuracion (funciona ahi porque nunca tuvo una tabla de
/// deduplicacion propia con la que chocar en nombre).
/// </summary>
public sealed class OrdenesDbContext : DbContext
{
    public const string EsquemaPorDefecto = "ordenes";

    public OrdenesDbContext(DbContextOptions<OrdenesDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<Orden> Ordenes => Set<Orden>();

    public DbSet<TransicionOrden> Transiciones => Set<TransicionOrden>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);

        // Secuencia que numera las ordenes: alimenta el id
        // ORD-<anio>-<correlativo de 6 digitos> y el numero de orden visible.
        modelBuilder.HasSequence<long>("orden_correlativo", EsquemaPorDefecto);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdenesDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfigurationBuildingBlocks());

        // La tabla outbox vive fuera del esquema propio del contexto: el publicador
        // transversal (OutboxPublisher) la barre con un "SELECT * FROM
        // outbox_messages" sin calificar esquema, igual que en el resto de
        // servicios, que no fijan un esquema por defecto.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
