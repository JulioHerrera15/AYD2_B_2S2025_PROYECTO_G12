using SgoLogic.Inventario.Api.Application.Inventario;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IUnidadDeTrabajo" />. Cuenta las confirmaciones y, si se le
/// arma, lanza <see cref="ExcepcionConflictoConcurrencia" /> para simular el
/// choque del bloqueo optimista, sea siempre o solo en una confirmacion puntual
/// (util para el barrido de expiracion, que confirma una vez por reserva).
/// </summary>
public sealed class UnidadDeTrabajoFalsa : IUnidadDeTrabajo
{
    private int _intentos;

    public int Confirmaciones { get; private set; }

    public int Descartes { get; private set; }

    public bool LanzarConflicto { get; set; }

    /// <summary>Numero de intento (base 1) en el que se simula el conflicto. Null para no fallar nunca.</summary>
    public int? LanzarConflictoEnIntento { get; set; }

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        _intentos++;

        if (LanzarConflicto || _intentos == LanzarConflictoEnIntento)
        {
            throw new ExcepcionConflictoConcurrencia("conflicto simulado", new InvalidOperationException());
        }

        Confirmaciones++;
        return Task.CompletedTask;
    }

    public void DescartarCambios() => Descartes++;
}
