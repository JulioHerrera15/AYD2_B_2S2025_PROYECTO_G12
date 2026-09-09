using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Responde que medios de pago admite cada canal, a partir de la seccion
/// <see cref="MediosPagoPorCanalOptions" />. La usa el caso de uso de creacion
/// para validar la solicitud y el endpoint GET /api/ordenes/medios-pago para el
/// checkout.
/// </summary>
public sealed class PoliticaMediosPago
{
    private readonly MediosPagoPorCanalOptions _porCanal;

    public PoliticaMediosPago(IOptions<MediosPagoPorCanalOptions> opciones)
        : this(opciones.Value)
    {
    }

    public PoliticaMediosPago(MediosPagoPorCanalOptions porCanal)
    {
        _porCanal = porCanal;
    }

    public IReadOnlyList<MedioPago> MediosHabilitados(Canal canal) =>
        _porCanal.TryGetValue(canal.ToString(), out var medios)
            ? medios
            : Array.Empty<MedioPago>();

    public bool EstaHabilitado(Canal canal, MedioPago medioPago) =>
        MediosHabilitados(canal).Contains(medioPago);
}
