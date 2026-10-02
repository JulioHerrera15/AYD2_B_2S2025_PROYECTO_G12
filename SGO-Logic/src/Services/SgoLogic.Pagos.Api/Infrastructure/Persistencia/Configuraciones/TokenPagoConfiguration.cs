using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class TokenPagoConfiguration : IEntityTypeConfiguration<TokenPago>
{
    public void Configure(EntityTypeBuilder<TokenPago> builder)
    {
        builder.ToTable("token_pago");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .HasColumnName("id")
            .HasMaxLength(40)
            .ValueGeneratedNever();

        builder.Property(token => token.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(TokenPago.LargoMaximoClienteId);

        builder.Property(token => token.Marca)
            .HasColumnName("marca")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Solo los ultimos cuatro en claro: la columna no admite un numero completo.
        builder.Property(token => token.Ultimos4)
            .HasColumnName("ultimos4")
            .HasMaxLength(4)
            .IsFixedLength()
            .IsRequired();

        builder.Property(token => token.MesExp)
            .HasColumnName("mes_exp")
            .IsRequired();

        builder.Property(token => token.AnioExp)
            .HasColumnName("anio_exp")
            .IsRequired();

        builder.Property(token => token.Titular)
            .HasColumnName("titular")
            .HasMaxLength(ValidadorTarjeta.LargoMaximoTitular)
            .IsRequired();

        // PAN + CVV (o solo CVV) cifrados con AES-GCM. Null una vez consumido o expirado.
        builder.Property(token => token.DatosCifrados)
            .HasColumnName("datos_cifrados")
            .HasColumnType("bytea");

        builder.Property(token => token.Nonce)
            .HasColumnName("nonce")
            .HasColumnType("bytea");

        builder.Property(token => token.Tag)
            .HasColumnName("tag")
            .HasColumnType("bytea");

        builder.Property(token => token.ReferenciaTarjetaGuardadaId)
            .HasColumnName("referencia_tarjeta_guardada_id");

        builder.Property(token => token.GuardarAlCobrar)
            .HasColumnName("guardar_al_cobrar")
            .IsRequired();

        builder.Property(token => token.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(token => token.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(token => token.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Bloqueo optimista: consumir o expirar incrementa la version.
        builder.Property(token => token.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        // El worker de expiracion barre los ACTIVO por fecha de vencimiento.
        builder.HasIndex(token => new { token.Estado, token.ExpiraEn })
            .HasDatabaseName("ix_token_pago_estado_expira_en");

        builder.Ignore(token => token.EsDeTarjetaGuardada);
        builder.Ignore(token => token.TieneDatosCifrados);
    }
}
