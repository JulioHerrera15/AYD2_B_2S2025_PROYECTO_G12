using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Application.Despacho;

/// <summary>
/// Interpreta el parametro opcional <c>estado</c> de los listados de preparacion.
/// Compartido por los dos endpoints para que validen exactamente igual.
/// </summary>
public static class FiltroEstadoDespacho
{
    /// <summary>
    /// Vacio significa sin filtro. Devuelve false si el texto no se reconoce como
    /// <see cref="EstadoDespacho" />.
    /// </summary>
    public static bool IntentarParsear(string? texto, out EstadoDespacho? estado)
    {
        estado = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (!Enum.TryParse<EstadoDespacho>(texto, ignoreCase: true, out var parseado))
        {
            return false;
        }

        estado = parseado;
        return true;
    }
}
