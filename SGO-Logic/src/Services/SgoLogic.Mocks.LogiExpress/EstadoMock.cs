namespace SgoLogic.Mocks.LogiExpress;

/// <summary>Como se comporta el mock ante la siguiente solicitud de guia.</summary>
public enum ModoMock
{
    /// <summary>Emite guia, salvo la que le toca fallar por la cuota de timeout.</summary>
    NORMAL,

    /// <summary>Emite guia pero tardando <c>LatenciaMs</c>.</summary>
    LENTO,

    /// <summary>Nunca emite: toda solicitud agota el tiempo. Para probar el camino de guia pendiente.</summary>
    CAIDO
}

public sealed record ConfiguracionMock(ModoMock Modo, int LatenciaMs, int UnoDeCada);

/// <summary>
/// Estado en memoria del mock: configuracion, guias emitidas e intentos recibidos.
/// Singleton, todo bajo candado porque las peticiones concurrentes lo tocan a la vez.
///
/// El timeout del criterio de aceptacion (~2% de las solicitudes) se simula con un
/// contador y no con azar: la solicitud numero <c>UnoDeCada</c> falla, la siguiente
/// tanda vuelve a contar. Deterministico a proposito — una demo y una prueba
/// necesitan poder decir "esta es la que falla", cosa que un dado no permite.
/// </summary>
public sealed class EstadoMock
{
    private readonly object _bloqueo = new();
    private readonly List<GuiaRegistrada> _guias = new();
    private readonly List<IntentoRegistrado> _intentos = new();
    private ConfiguracionMock _configuracion = new(ModoMock.NORMAL, LatenciaMs: 5000, UnoDeCada: 50);
    private int _solicitudes;

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

    public IReadOnlyList<GuiaRegistrada> Guias
    {
        get
        {
            lock (_bloqueo)
            {
                return _guias.ToArray();
            }
        }
    }

    public IReadOnlyList<IntentoRegistrado> Intentos
    {
        get
        {
            lock (_bloqueo)
            {
                return _intentos.ToArray();
            }
        }
    }

    public ConfiguracionMock Configurar(ModoMock modo, int latenciaMs, int unoDeCada)
    {
        lock (_bloqueo)
        {
            _configuracion = new ConfiguracionMock(modo, latenciaMs, unoDeCada);
            return _configuracion;
        }
    }

    /// <summary>
    /// Cuenta la solicitud y dice si a esta le toca agotar el tiempo. En modo CAIDO
    /// siempre le toca; en NORMAL y LENTO, solo a la multiplo de <c>UnoDeCada</c>.
    /// </summary>
    public bool LeTocaTimeout()
    {
        lock (_bloqueo)
        {
            if (_configuracion.Modo == ModoMock.CAIDO)
            {
                return true;
            }

            _solicitudes++;

            return _configuracion.UnoDeCada > 0 && _solicitudes % _configuracion.UnoDeCada == 0;
        }
    }

    public void RegistrarGuia(GuiaRegistrada guia)
    {
        lock (_bloqueo)
        {
            _guias.Add(guia);
        }
    }

    public void RegistrarIntento(IntentoRegistrado intento)
    {
        lock (_bloqueo)
        {
            _intentos.Add(intento);
        }
    }

    public GuiaRegistrada? BuscarGuia(string guiaId)
    {
        lock (_bloqueo)
        {
            return _guias.FirstOrDefault(g => g.GuiaId == guiaId);
        }
    }

    /// <summary>Avanza el estado de rastreo de una guia. Devuelve null si no existe.</summary>
    public GuiaRegistrada? CambiarEstado(string guiaId, string estado)
    {
        lock (_bloqueo)
        {
            var indice = _guias.FindIndex(g => g.GuiaId == guiaId);
            if (indice < 0)
            {
                return null;
            }

            _guias[indice] = _guias[indice] with { Estado = estado };
            return _guias[indice];
        }
    }
}
