namespace SgoLogic.Mocks.SAT;

/// <summary>
/// Estado en memoria del mock: la configuracion de comportamiento y la lista de
/// certificaciones recibidas en esta ejecucion. Singleton. Todo acceso va bajo un
/// candado porque las peticiones HTTP concurrentes lo tocan a la vez.
/// </summary>
public sealed class EstadoMock
{
    private readonly object _bloqueo = new();
    private readonly List<CertificacionRegistrada> _certificaciones = new();
    private ConfiguracionMock _configuracion = new(ModoMock.NORMAL, LatenciaMs: 2000, ProbabilidadFallo: 0.0);

    /// <summary>Almacen de idempotencia: clave del header Idempotency-Key -> respuesta ya calculada.</summary>
    public AlmacenIdempotencia Idempotencia { get; } = new();

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

    public IReadOnlyList<CertificacionRegistrada> Certificaciones
    {
        get
        {
            lock (_bloqueo)
            {
                return _certificaciones.ToArray();
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

    public void Registrar(CertificacionRegistrada certificacion)
    {
        lock (_bloqueo)
        {
            _certificaciones.Add(certificacion);
        }
    }
}
