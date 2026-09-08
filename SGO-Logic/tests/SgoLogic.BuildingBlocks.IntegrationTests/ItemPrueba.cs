using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

/// <summary>Entidad minima solo para los tests de integracion, sin significado de negocio.</summary>
public sealed class ItemPrueba
{
    private ItemPrueba()
    {
    }

    public ItemPrueba(string nombre)
    {
        Id = Guid.NewGuid();
        Nombre = nombre;
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
}

public sealed class ItemPruebaConfiguration : IEntityTypeConfiguration<ItemPrueba>
{
    public void Configure(EntityTypeBuilder<ItemPrueba> builder)
    {
        builder.ToTable("items_prueba");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Nombre).IsRequired();
    }
}