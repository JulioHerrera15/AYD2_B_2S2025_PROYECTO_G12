using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Despacho.Api.Application.Despacho;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioDespachos" />.</summary>
public sealed class RepositorioDespachosFalso : IRepositorioDespachos
{
    private readonly List<Despacho.Api.Domain.Despacho> _despachos = new();

    public IReadOnlyList<Despacho.Api.Domain.Despacho> Todos => _despachos;

    public void Precargar(Despacho.Api.Domain.Despacho despacho) => _despachos.Add(despacho);

    public Task<Despacho.Api.Domain.Despacho?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        Task.FromResult(_despachos.FirstOrDefault(d => d.OrdenId == ordenId));

    public Task<IReadOnlyList<Despacho.Api.Domain.Despacho>> ListarPorUbicacionOrigenAsync(
        string ubicacionOrigenId,
        EstadoDespacho? estado,
        CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Despacho.Api.Domain.Despacho>>(
            _despachos
                .Where(d => d.UbicacionOrigenId == ubicacionOrigenId)
                .Where(d => estado is null || d.Estado == estado)
                .ToList());

    public Task<IReadOnlyList<Despacho.Api.Domain.Despacho>> ListarAsync(
        string? ubicacionOrigenId,
        EstadoDespacho? estado,
        CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Despacho.Api.Domain.Despacho>>(
            _despachos
                .Where(d => ubicacionOrigenId is null || d.UbicacionOrigenId == ubicacionOrigenId)
                .Where(d => estado is null || d.Estado == estado)
                .OrderByDescending(d => d.CreadoEn)
                .ToList());

    public Task AgregarAsync(Despacho.Api.Domain.Despacho despacho, CancellationToken cancelacion)
    {
        _despachos.Add(despacho);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Doble de <see cref="IClienteInventario" />. Por defecto no sabe de ninguna
/// reserva, para que cada test diga explicitamente que le responde Inventario.
/// </summary>
public sealed class ClienteInventarioFalso : IClienteInventario
{
    private readonly Dictionary<string, UbicacionReservaDto> _reservas = new();

    public int Llamadas { get; private set; }

    public ClienteInventarioFalso Con(string ordenId, string ubicacionId, TipoUbicacion tipo)
    {
        _reservas[ordenId] = new UbicacionReservaDto(ubicacionId, tipo);
        return this;
    }

    public Task<UbicacionReservaDto?> ObtenerUbicacionDeReservaAsync(string ordenId, CancellationToken cancelacion)
    {
        Llamadas++;
        return Task.FromResult(_reservas.TryGetValue(ordenId, out var reserva) ? reserva : null);
    }
}

/// <summary>
/// Doble de <see cref="IClienteLogiExpress" />. Puede emitir guia, devolver null
/// (el proveedor contesto pero no emitio) o lanzar el timeout de un proveedor que
/// no responde.
/// </summary>
public sealed class ClienteLogiExpressFalso : IClienteLogiExpress
{
    public enum Comportamiento
    {
        Emite,
        NoEmite,
        AgotaTiempo
    }

    private int _emitidas;

    public Comportamiento Modo { get; set; } = Comportamiento.Emite;

    public int Solicitudes { get; private set; }

    public List<string> Anuladas { get; } = new();

    public Task<GuiaEmitidaDto?> SolicitarGuiaAsync(SolicitudGuia solicitud, CancellationToken cancelacion)
    {
        Solicitudes++;

        return Modo switch
        {
            Comportamiento.AgotaTiempo => throw new TaskCanceledException("timeout simulado"),
            Comportamiento.NoEmite => Task.FromResult<GuiaEmitidaDto?>(null),
            _ => Task.FromResult<GuiaEmitidaDto?>(
                new GuiaEmitidaDto($"LE-TEST-{++_emitidas:D3}", "https://rastreo/x", "LogiExpress"))
        };
    }

    public Task<bool> AnularGuiaAsync(string guiaId, CancellationToken cancelacion)
    {
        Anuladas.Add(guiaId);
        return Task.FromResult(true);
    }
}

/// <summary>Correlativo predecible para que los ids de los tests no dependan del azar.</summary>
public sealed class GeneradorCorrelativoDespachoFalso : IGeneradorCorrelativoDespacho
{
    private long _siguiente;

    public GeneradorCorrelativoDespachoFalso(long inicial = 1) => _siguiente = inicial;

    public Task<long> SiguienteAsync(CancellationToken cancelacion) => Task.FromResult(_siguiente++);
}

/// <summary>Doble de <see cref="IUnidadDeTrabajo" />; cuenta confirmaciones.</summary>
public sealed class UnidadDeTrabajoFalsa : IUnidadDeTrabajo
{
    public int Confirmaciones { get; private set; }

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        Confirmaciones++;
        return Task.CompletedTask;
    }
}
