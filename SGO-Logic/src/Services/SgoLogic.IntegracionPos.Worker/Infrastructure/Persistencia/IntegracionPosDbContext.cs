using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.IntegracionPos.Worker.Infrastructure.Persistencia;

public sealed class IntegracionPosDbContext : DbContext
{
    public IntegracionPosDbContext(DbContextOptions<IntegracionPosDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<MapeoTiendaPos> MapeosTiendas => Set<MapeoTiendaPos>();
    public DbSet<RegistroEnvioPos> RegistrosEnvioPos => Set<RegistroEnvioPos>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MapeoTiendaPosConfiguration());
        modelBuilder.ApplyConfiguration(new RegistroEnvioPosConfiguration());
    }
}

public sealed class MapeoTiendaPos
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string TiendaOrigenId { get; set; } = string.Empty;
    public string PosInstanciaId { get; set; } = "T01";
    public bool Activo { get; set; } = true;
}

public sealed class RegistroEnvioPos
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string EventoId { get; set; } = string.Empty;
    public string OrdenId { get; set; } = string.Empty;
    public string NumeroOrden { get; set; } = string.Empty;
    public string TiendaId { get; set; } = string.Empty;
    public string PosInstanciaId { get; set; } = string.Empty;
    public string TipoEvento { get; set; } = string.Empty;
    public string EstadoEnvio { get; set; } = string.Empty;
    public int? CodigoHttp { get; set; }
    public string? Respuesta { get; set; }
    public int IntentoNumero { get; set; }
    public DateTime FechaEnvio { get; set; } = DateTime.UtcNow;
}

public sealed class MapeoTiendaPosConfiguration : IEntityTypeConfiguration<MapeoTiendaPos>
{
    public void Configure(EntityTypeBuilder<MapeoTiendaPos> builder)
    {
        builder.ToTable("mapeo_tienda_pos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TiendaOrigenId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PosInstanciaId).HasMaxLength(10).IsRequired();
        builder.HasIndex(x => x.TiendaOrigenId).IsUnique();
    }
}

public sealed class RegistroEnvioPosConfiguration : IEntityTypeConfiguration<RegistroEnvioPos>
{
    public void Configure(EntityTypeBuilder<RegistroEnvioPos> builder)
    {
        builder.ToTable("registro_envio_pos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventoId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrdenId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NumeroOrden).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TiendaId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PosInstanciaId).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TipoEvento).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EstadoEnvio).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Respuesta).HasColumnType("text");
        builder.HasIndex(x => x.EventoId);
        builder.HasIndex(x => x.OrdenId);
    }
}
