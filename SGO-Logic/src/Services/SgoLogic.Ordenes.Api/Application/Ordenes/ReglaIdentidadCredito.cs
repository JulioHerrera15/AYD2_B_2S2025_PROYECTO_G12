using System.Security.Claims;
using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

public enum ResultadoIdentidadCredito
{
    /// <summary>La orden no es a credito: se comporta igual que siempre.</summary>
    NoAplica,

    /// <summary>Orden a credito con JWT valido cuyo email coincide con ClienteId.</summary>
    Permitida,

    /// <summary>Orden a credito sin JWT valido: 401 credito_requiere_sesion.</summary>
    RequiereSesion,

    /// <summary>El email del JWT no es el ClienteId de la orden: 403 cliente_no_coincide.</summary>
    ClienteNoCoincide
}

/// <summary>
/// Credito usa la linea del cliente: una orden a credito solo la puede crear el
/// propio cliente con sesion. Solo aplica cuando el medio de pago es CREDITO; las
/// demas ordenes (invitados, POS, scripts) siguen sin exigir JWT.
/// </summary>
public static class ReglaIdentidadCredito
{
    public static ResultadoIdentidadCredito Verificar(string? medioPago, ClaimsPrincipal? usuario, string? clienteId)
    {
        if (!Enum.TryParse<MedioPago>(medioPago?.Trim(), ignoreCase: true, out var medio) || medio != MedioPago.CREDITO)
        {
            return ResultadoIdentidadCredito.NoAplica;
        }

        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return ResultadoIdentidadCredito.RequiereSesion;
        }

        var email = usuario.FindFirst(ClaimTypes.Email)?.Value ?? usuario.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return ResultadoIdentidadCredito.RequiereSesion;
        }

        return string.Equals(email.Trim(), clienteId?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ResultadoIdentidadCredito.Permitida
            : ResultadoIdentidadCredito.ClienteNoCoincide;
    }
}
