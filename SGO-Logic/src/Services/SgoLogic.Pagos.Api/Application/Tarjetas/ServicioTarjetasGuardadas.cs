using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tarjetas;

/// <summary>
/// Tarjetas guardadas de un cliente: listar, guardar (upsert) y dar de baja. El token
/// para pagar con una de ellas lo emite <see cref="Tokens.ServicioTokensPago" /> y la
/// tarjeta se guarda sola al cobrar con guardar=true (<see cref="Pagos.ServicioCobro" />).
///
/// Toda operacion recibe el clienteId ya resuelto del JWT por el controlador: el
/// servicio nunca confia en un cliente que venga del cuerpo o de la ruta, y una
/// tarjeta de otro cliente se trata igual que una inexistente (no encontrada).
/// </summary>
public sealed class ServicioTarjetasGuardadas
{
    private readonly IRepositorioTarjetasGuardadas _tarjetas;
    private readonly Func<DateTime> _reloj;

    public ServicioTarjetasGuardadas(IRepositorioTarjetasGuardadas tarjetas)
        : this(tarjetas, () => DateTime.UtcNow)
    {
    }

    public ServicioTarjetasGuardadas(IRepositorioTarjetasGuardadas tarjetas, Func<DateTime> reloj)
    {
        _tarjetas = tarjetas;
        _reloj = reloj;
    }

    public async Task<IReadOnlyList<TarjetaGuardadaDto>> ListarAsync(string clienteId, CancellationToken cancelacion)
    {
        var ahora = _reloj();
        var tarjetas = await _tarjetas.ListarActivasAsync(clienteId, cancelacion);
        return tarjetas.Select(tarjeta => TarjetaGuardadaDto.Desde(tarjeta, ahora)).ToList();
    }

    /// <summary>
    /// Guarda la tarjeta o, si el cliente ya tiene activa la misma (marca, ultimos
    /// cuatro y vencimiento), le actualiza la referencia de PayKlik y el titular.
    /// </summary>
    public async Task<(TarjetaGuardadaDto Tarjeta, EstadoGuardarTarjeta Estado)> GuardarAsync(
        string clienteId,
        GuardarTarjetaRequest solicitud,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (string.IsNullOrWhiteSpace(solicitud.ReferenciaTarjeta))
        {
            throw new ArgumentException("La referencia de la tarjeta es obligatoria.", nameof(solicitud));
        }

        var ahora = _reloj();
        var (tarjeta, estado) = await RegistrarAsync(
            _tarjetas,
            clienteId,
            solicitud.ReferenciaTarjeta,
            ReglasTarjeta.ParsearMarca(solicitud.Marca),
            (solicitud.Ultimos4 ?? string.Empty).Trim(),
            solicitud.MesExp,
            ReglasTarjeta.NormalizarAnio(solicitud.AnioExp),
            solicitud.Titular,
            ahora,
            cancelacion);

        await _tarjetas.GuardarCambiosAsync(cancelacion);
        return (TarjetaGuardadaDto.Desde(tarjeta, ahora), estado);
    }

    /// <summary>
    /// Upsert sin guardar cambios: lo comparten <see cref="GuardarAsync" /> y el cobro
    /// con guardar=true, que cierra la transaccion junto con el pago.
    /// </summary>
    public static async Task<(TarjetaGuardada Tarjeta, EstadoGuardarTarjeta Estado)> RegistrarAsync(
        IRepositorioTarjetasGuardadas tarjetas,
        string clienteId,
        string referenciaPayKlik,
        MarcaTarjeta marca,
        string ultimos4,
        int mesExp,
        int anioExp,
        string? titular,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var existente = await tarjetas.BuscarActivaIgualAsync(clienteId, marca, ultimos4, mesExp, anioExp, cancelacion);
        if (existente is not null)
        {
            existente.ActualizarReferencia(referenciaPayKlik, titular);
            return (existente, EstadoGuardarTarjeta.Actualizada);
        }

        var nueva = new TarjetaGuardada(clienteId, referenciaPayKlik, marca, ultimos4, mesExp, anioExp, titular, ahora);
        await tarjetas.AgregarAsync(nueva, cancelacion);
        return (nueva, EstadoGuardarTarjeta.Creada);
    }

    /// <summary>Baja logica. False si la tarjeta no existe, ya estaba inactiva o es de otro cliente.</summary>
    public async Task<bool> EliminarAsync(string clienteId, Guid id, CancellationToken cancelacion)
    {
        var tarjeta = await _tarjetas.ObtenerActivaAsync(id, cancelacion);

        // La tarjeta de otro cliente no se distingue de una inexistente: no se revela
        // que el id existe.
        if (tarjeta is null || !string.Equals(tarjeta.ClienteId, clienteId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        tarjeta.Desactivar();
        await _tarjetas.GuardarCambiosAsync(cancelacion);
        return true;
    }
}
