using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.IntegracionErp.Worker.Infrastructure;

public sealed class IntegracionErpDbContext : DbContext
{
    public IntegracionErpDbContext(DbContextOptions<IntegracionErpDbContext> options) : base(options) { }

    public DbSet<VentaErp> Ventas => Set<VentaErp>();
    public DbSet<LoteErp> Lotes => Set<LoteErp>();
    public DbSet<RegistroLoteErp> RegistrosLote => Set<RegistroLoteErp>();
    public DbSet<ConciliacionErp> Conciliaciones => Set<ConciliacionErp>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
        modelBuilder.ApplyConfiguration(new VentaErpConfiguration());
        modelBuilder.ApplyConfiguration(new LoteErpConfiguration());
        modelBuilder.ApplyConfiguration(new RegistroLoteErpConfiguration());
        modelBuilder.ApplyConfiguration(new ConciliacionErpConfiguration());
    }
}

public sealed class VentaErp
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string EventoId { get; set; } = string.Empty;
    public string OrdenId { get; set; } = string.Empty;
    public string NumeroOrden { get; set; } = string.Empty;
    public string? ClienteId { get; set; }
    public string Canal { get; set; } = string.Empty;
    public string Tienda { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateTime FechaVenta { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public string? DocumentoFiscalId { get; set; }
    public string? SerieFiscal { get; set; }
    public string? NumeroFiscal { get; set; }
    public string? DevolucionId { get; set; }
    public string? TipoDevolucion { get; set; }
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}

public sealed class LoteErp
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string LoteId { get; set; } = string.Empty;
    public long Secuencia { get; set; }
    public DateTime FechaGeneracion { get; set; }
    public string Formato { get; set; } = "CSV";
    public string Estado { get; set; } = "GENERADO";
    public int CantidadRegistros { get; set; }
    public decimal MontoTotal { get; set; }
    public string? Contenido { get; set; }
    public int Intentos { get; set; }
    public int? CodigoHttp { get; set; }
    public string? RespuestaErp { get; set; }
    public DateTime? EnviadoEn { get; set; }
    public DateTime? ConfirmadoEn { get; set; }
    public List<RegistroLoteErp> Registros { get; set; } = [];
}

public sealed class RegistroLoteErp
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid LoteErpId { get; set; }
    public LoteErp? Lote { get; set; }
    public string OrdenId { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public string Tienda { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateTime Fecha { get; set; }
}

public sealed class ConciliacionErp
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime Fecha { get; set; }
    public string LoteId { get; set; } = string.Empty;
    public int RegistrosSgo { get; set; }
    public decimal MontoSgo { get; set; }
    public int RegistrosErp { get; set; }
    public decimal MontoErp { get; set; }
    public bool Conciliada { get; set; }
    public string? Diferencias { get; set; }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;
}

public sealed class VentaErpConfiguration : IEntityTypeConfiguration<VentaErp>
{
    public void Configure(EntityTypeBuilder<VentaErp> b)
    {
        b.ToTable("ventas_erp"); b.HasKey(x => x.Id);
        b.Property(x => x.EventoId).HasMaxLength(200).IsRequired();
        b.Property(x => x.OrdenId).HasMaxLength(200).IsRequired();
        b.Property(x => x.NumeroOrden).HasMaxLength(200).IsRequired();
        b.Property(x => x.ClienteId).HasMaxLength(254);
        b.Property(x => x.Canal).HasMaxLength(40).IsRequired();
        b.Property(x => x.Tienda).HasMaxLength(80).IsRequired();
        b.Property(x => x.Monto).HasPrecision(18, 2);
        b.Property(x => x.Estado).HasMaxLength(30).IsRequired();
        b.Property(x => x.DevolucionId).HasMaxLength(200);
        b.Property(x => x.TipoDevolucion).HasMaxLength(30);
        b.HasIndex(x => x.EventoId).IsUnique(); b.HasIndex(x => x.OrdenId);
    }
}

public sealed class LoteErpConfiguration : IEntityTypeConfiguration<LoteErp>
{
    public void Configure(EntityTypeBuilder<LoteErp> b)
    {
        b.ToTable("lotes_erp"); b.HasKey(x => x.Id);
        b.Property(x => x.LoteId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Formato).HasMaxLength(10).IsRequired();
        b.Property(x => x.Estado).HasMaxLength(30).IsRequired();
        b.Property(x => x.MontoTotal).HasPrecision(18, 2);
        b.Property(x => x.Contenido).HasColumnType("text"); b.Property(x => x.RespuestaErp).HasColumnType("text");
        b.HasIndex(x => x.LoteId).IsUnique(); b.HasIndex(x => x.Secuencia).IsUnique();
    }
}

public sealed class RegistroLoteErpConfiguration : IEntityTypeConfiguration<RegistroLoteErp>
{
    public void Configure(EntityTypeBuilder<RegistroLoteErp> b)
    {
        b.ToTable("registros_lote_erp"); b.HasKey(x => x.Id);
        b.Property(x => x.OrdenId).HasMaxLength(200).IsRequired(); b.Property(x => x.Canal).HasMaxLength(40).IsRequired();
        b.Property(x => x.Tienda).HasMaxLength(80).IsRequired(); b.Property(x => x.Monto).HasPrecision(18, 2);
        b.HasOne(x => x.Lote).WithMany(x => x.Registros).HasForeignKey(x => x.LoteErpId);
    }
}

public sealed class ConciliacionErpConfiguration : IEntityTypeConfiguration<ConciliacionErp>
{
    public void Configure(EntityTypeBuilder<ConciliacionErp> b)
    {
        b.ToTable("conciliaciones_erp"); b.HasKey(x => x.Id); b.Property(x => x.LoteId).HasMaxLength(100).IsRequired();
        b.Property(x => x.MontoSgo).HasPrecision(18, 2); b.Property(x => x.MontoErp).HasPrecision(18, 2);
        b.Property(x => x.Diferencias).HasColumnType("text"); b.HasIndex(x => new { x.Fecha, x.LoteId });
    }
}
