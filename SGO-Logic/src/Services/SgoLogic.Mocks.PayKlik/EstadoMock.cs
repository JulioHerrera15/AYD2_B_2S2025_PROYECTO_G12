namespace SgoLogic.Mocks.PayKlik;

/// <summary>
/// Estado en memoria del mock: la configuracion de comportamiento y las listas de
/// cargos y reembolsos recibidos en esta ejecucion. Singleton. Todo acceso va bajo
/// un candado porque las peticiones HTTP concurrentes lo tocan a la vez.
/// </summary>
public sealed class EstadoMock
{
    private readonly object _bloqueo = new();
    private readonly List<CargoRegistrado> _cargos = new();
    private readonly List<ReembolsoRegistrado> _reembolsos = new();
    private ConfiguracionMock _configuracion = new(ModoMock.NORMAL, LatenciaMs: 2000, ProbabilidadFallo: 0.5);

    /// <summary>Almacen de idempotencia para cargos: clave del header Idempotency-Key -> respuesta ya calculada.</summary>
    public AlmacenIdempotencia Idempotencia { get; } = new();

    /// <summary>Almacen de idempotencia para reembolsos: clave del header Idempotency-Key -> respuesta ya calculada.</summary>
    public AlmacenIdempotenciaReembolso IdempotenciaReembolso { get; } = new();

    /// <summary>Tokens de tarjeta emitidos por POST /v1/tokens. Solo en memoria: nunca se persiste.</summary>
    public AlmacenTokens Tokens { get; } = new();

    public ConfiguracionMock Configuracion
    {
        get
        {
            lock (_bloqueo)
            {
                return _configuracion;
            }
        }
    }

    public IReadOnlyList<CargoRegistrado> Cargos
    {
        get
        {
            lock (_bloqueo)
            {
                return _cargos.ToArray();
            }
        }
    }

    public IReadOnlyList<ReembolsoRegistrado> Reembolsos
    {
        get
        {
            lock (_bloqueo)
            {
                return _reembolsos.ToArray();
            }
        }
    }

    public ConfiguracionMock Configurar(ModoMock modo, int latenciaMs, double probabilidadFallo)
    {
        lock (_bloqueo)
        {
            _configuracion = new ConfiguracionMock(modo, latenciaMs, probabilidadFallo);
            return _configuracion;
        }
    }

    public void Registrar(CargoRegistrado cargo)
    {
        lock (_bloqueo)
        {
            _cargos.Add(cargo);
        }
    }

    public void RegistrarReembolso(ReembolsoRegistrado reembolso)
    {
        lock (_bloqueo)
        {
            _reembolsos.Add(reembolso);
        }
    }
}
