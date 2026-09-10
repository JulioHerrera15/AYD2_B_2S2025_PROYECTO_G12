using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia;

/// <summary>
/// Fuerza que todo DateTime salga hacia PostgreSQL en UTC y vuelva marcado como
/// UTC. Las columnas son timestamptz; el dominio ya trabaja en UTC, esto es la red
/// de seguridad para cualquier valor con Kind distinto.
/// </summary>
internal sealed class ConvertidorFechaUtc : ValueConverter<DateTime, DateTime>
{
    public ConvertidorFechaUtc()
        : base(
            valor => valor.Kind == DateTimeKind.Utc ? valor : valor.ToUniversalTime(),
            valor => DateTime.SpecifyKind(valor, DateTimeKind.Utc))
    {
    }
}
