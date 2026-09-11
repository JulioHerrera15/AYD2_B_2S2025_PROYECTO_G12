using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.ConsultaEstado.Api.Domain;

namespace SgoLogic.ConsultaEstado.Api.Infrastructure;

public sealed class ConsultaEstadoDbContext : DbContext
{
    public ConsultaEstadoDbContext(DbContextOptions<ConsultaEstadoDbContext> options) : base(options) { }

    public DbSet<OrdenVista> OrdenesVista => Set<OrdenVista>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("consulta_estado");

        modelBuilder.Entity<OrdenVista>(b =>
        {
            b.ToTable("ordenes_vista");
            b.HasKey(o => o.OrdenId);
            b.Property(o => o.OrdenId).HasColumnName("orden_id").HasMaxLength(64);
            b.Property(o => o.NumeroOrden).HasColumnName("numero_orden").HasMaxLength(40);
            b.Property(o => o.ClienteId).HasColumnName("cliente_id").HasMaxLength(64);
            b.Property(o => o.Estado).HasColumnName("estado").HasMaxLength(50);
            b.Property(o => o.EstadoAnterior).HasColumnName("estado_anterior").HasMaxLength(50);
            b.Property(o => o.Canal).HasColumnName("canal").HasMaxLength(20);
            b.Property(o => o.MetodoEntrega).HasColumnName("metodo_entrega").HasMaxLength(20);
            b.Property(o => o.UbicacionSolicitadaId).HasColumnName("ubicacion_solicitada_id").HasMaxLength(64);
            b.Property(o => o.Moneda).HasColumnName("moneda").HasMaxLength(10);
            b.Property(o => o.Subtotal).HasColumnName("subtotal").HasPrecision(18, 2);
            b.Property(o => o.Impuestos).HasColumnName("impuestos").HasPrecision(18, 2);
            b.Property(o => o.Total).HasColumnName("total").HasPrecision(18, 2);
            b.Property(o => o.PagoId).HasColumnName("pago_id").HasMaxLength(64);
            b.Property(o => o.TiendaDestinoId).HasColumnName("tienda_destino_id").HasMaxLength(64);
            b.Property(o => o.DocumentoFiscalId).HasColumnName("documento_fiscal_id").HasMaxLength(64);
            b.Property(o => o.SerieDocumentoFiscal).HasColumnName("serie_documento_fiscal").HasMaxLength(20);
            b.Property(o => o.NumeroDocumentoFiscal).HasColumnName("numero_documento_fiscal").HasMaxLength(40);
            b.Property(o => o.MotivoUltimoCambio).HasColumnName("motivo_ultimo_cambio");
            b.Property(o => o.RequiereReembolso).HasColumnName("requiere_reembolso");
            b.Property(o => o.MontoReembolso).HasColumnName("monto_reembolso").HasPrecision(18, 2);
            b.Property(o => o.UltimoSequence).HasColumnName("ultimo_sequence");
            b.Property(o => o.CreadaEn).HasColumnName("creada_en").HasColumnType("timestamptz");
            b.Property(o => o.ActualizadaEn).HasColumnName("actualizada_en").HasColumnType("timestamptz");

            b.HasIndex(o => o.ClienteId).HasDatabaseName("ix_ordenes_vista_cliente_id");
        });

        modelBuilder.Entity<Incidencia>(b =>
        {
            b.ToTable("incidencias");
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).HasColumnName("id");
            b.Property(i => i.OrdenId).HasColumnName("orden_id").HasMaxLength(64).IsRequired();
            b.Property(i => i.Descripcion).HasColumnName("descripcion").IsRequired();
            b.Property(i => i.Estado).HasColumnName("estado").HasMaxLength(30).IsRequired();
            b.Property(i => i.Resolucion).HasColumnName("resolucion");
            b.Property(i => i.CreadaEn).HasColumnName("creada_en").HasColumnType("timestamptz");
            b.Property(i => i.ActualizadaEn).HasColumnName("actualizada_en").HasColumnType("timestamptz");
            b.HasIndex(i => i.OrdenId).HasDatabaseName("ix_incidencias_orden_id");
        });

        // Tabla de idempotencia del consumidor
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
    }
}
