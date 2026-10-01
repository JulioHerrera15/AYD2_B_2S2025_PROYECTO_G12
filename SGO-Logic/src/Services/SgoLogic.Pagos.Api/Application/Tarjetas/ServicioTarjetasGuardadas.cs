using Microsoft.Extensions.Logging;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tarjetas;

/// <summary>
/// Tarjetas guardadas de un cliente: listar, guardar (upsert), dar de baja y pedir a
/// PayKlik un token de un solo uso para pagar con una de ellas.
///
/// Toda operacion recibe el clienteId ya resuelto del JWT por el controlador: el
/// servicio nunca confia en un cliente que venga del cuerpo o de la ruta, y una
/// tarjeta de otro cliente se trata igual que una inexistente (no encontrada).
/// </summary>
public sealed class ServicioTarjetasGuardadas
{
    private readonly IRepositorioTarjetasGuardadas _tarjetas;
    private readonly IClienteBovedaPayKlik _boveda;
    private readonly ILogger<ServicioTarjetasGuardadas> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioTarjetasGuardadas(
        IRepositorioTarjetasGuardadas tarjetas,
        IClienteBovedaPayKlik boveda,
        ILogger<ServicioTarjetasGuardadas> logger)
        : this(tarjetas, boveda, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioTarjetasGuardadas(
        IRepositorioTarjetasGuardadas tarjetas,
        IClienteBovedaPayKlik boveda,
        ILogger<ServicioTarjetasGuardadas> logger,
        Func<DateTime> reloj)
    {
        _tarjetas = tarjetas;
        _boveda = boveda;
        _logger = logger;
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

        var marca = ReglasTarjeta.ParsearMarca(solicitud.Marca);
        var ultimos4 = (solicitud.Ultimos4 ?? string.Empty).Trim();
        var anio = ReglasTarjeta.NormalizarAnio(solicitud.AnioExp);
        var ahora = _reloj();

        var existente = await _tarjetas.BuscarActivaIgualAsync(clienteId, marca, ultimos4, solicitud.MesExp, anio, cancelacion);
        if (existente is not null)
        {
            existente.ActualizarReferencia(solicitud.ReferenciaTarjeta, solicitud.Titular);
            await _tarjetas.GuardarCambiosAsync(cancelacion);
            return (TarjetaGuardadaDto.Desde(existente, ahora), EstadoGuardarTarjeta.Actualizada);
        }

        var nueva = new TarjetaGuardada(
            clienteId,
            solicitud.ReferenciaTarjeta,
            marca,
            ultimos4,
            solicitud.MesExp,
            anio,
            solicitud.Titular,
            ahora);

        await _tarjetas.AgregarAsync(nueva, cancelacion);
        await _tarjetas.GuardarCambiosAsync(cancelacion);

        return (TarjetaGuardadaDto.Desde(nueva, ahora), EstadoGuardarTarjeta.Creada);
    }

    /// <summary>Baja logica. False si la tarjeta no existe, ya estaba inactiva o es de otro cliente.</summary>
    public async Task<bool> EliminarAsync(string clienteId, Guid id, CancellationToken cancelacion)
    {
        var tarjeta = await ObtenerDelClienteAsync(clienteId, id, cancelacion);
        if (tarjeta is null)
        {
            return false;
        }

        tarjeta.Desactivar();
        await _tarjetas.GuardarCambiosAsync(cancelacion);
        return true;
    }

    /// <summary>
    /// Pide a PayKlik un token de un solo uso para pagar con la tarjeta guardada. Si
    /// PayKlik ya no conoce la referencia (404), la tarjeta se desactiva: el cliente
    /// tiene que agregarla de nuevo.
    /// </summary>
    public async Task<ResultadoTokenTarjeta> ObtenerTokenAsync(
        string clienteId,
        Guid id,
        string? cvv,
        CancellationToken cancelacion)
    {
        var tarjeta = await ObtenerDelClienteAsync(clienteId, id, cancelacion);
        if (tarjeta is null)
        {
            return new ResultadoTokenTarjeta(EstadoTokenTarjeta.NoEncontrada, Mensaje: "La tarjeta no existe.");
        }

        var ahora = _reloj();
        if (tarjeta.EstaVencida(ahora))
        {
            return new ResultadoTokenTarjeta(EstadoTokenTarjeta.Vencida, Mensaje: "La tarjeta esta vencida.");
        }

        if (!ReglasTarjeta.CvvValido(tarjeta.Marca, cvv))
        {
            return new ResultadoTokenTarjeta(
                EstadoTokenTarjeta.CvvInvalido,
                Mensaje: $"El codigo de seguridad debe tener {ReglasTarjeta.LargoCvv(tarjeta.Marca)} digitos.");
        }

        var respuesta = await _boveda.GenerarTokenAsync(tarjeta.ReferenciaPayKlik, cvv!, cancelacion);

        switch (respuesta.Estado)
        {
            case EstadoTokenBoveda.Emitido:
                tarjeta.RegistrarUso(ahora);
                await _tarjetas.GuardarCambiosAsync(cancelacion);
                return new ResultadoTokenTarjeta(EstadoTokenTarjeta.Emitido, respuesta.Token);

            case EstadoTokenBoveda.ReferenciaNoExiste:
                tarjeta.Desactivar();
                await _tarjetas.GuardarCambiosAsync(cancelacion);

                _logger.LogWarning(
                    "PayKlik ya no reconoce la tarjeta guardada {TarjetaId} (terminada en {Ultimos4}); se desactivo.",
                    tarjeta.Id,
                    tarjeta.Ultimos4);

                return new ResultadoTokenTarjeta(
                    EstadoTokenTarjeta.YaNoValida,
                    Mensaje: "La tarjeta ya no es valida, agregala de nuevo");

            case EstadoTokenBoveda.Rechazado:
                return new ResultadoTokenTarjeta(
                    EstadoTokenTarjeta.RechazadaPorProveedor,
                    Mensaje: respuesta.Mensaje ?? "El procesador de pagos rechazo la tarjeta.");

            default:
                return new ResultadoTokenTarjeta(
                    EstadoTokenTarjeta.ProveedorNoDisponible,
                    Mensaje: "No pudimos comunicarnos con el procesador de pagos. Intenta de nuevo en unos momentos.");
        }
    }

    private async Task<TarjetaGuardada?> ObtenerDelClienteAsync(string clienteId, Guid id, CancellationToken cancelacion)
    {
        var tarjeta = await _tarjetas.ObtenerActivaAsync(id, cancelacion);

        // La tarjeta de otro cliente no se distingue de una inexistente: no se revela
        // que el id existe.
        return tarjeta is not null && string.Equals(tarjeta.ClienteId, clienteId, StringComparison.OrdinalIgnoreCase)
            ? tarjeta
            : null;
    }
}
