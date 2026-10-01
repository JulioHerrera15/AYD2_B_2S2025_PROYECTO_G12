namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Tarjeta que un cliente guardo para futuras compras. Pagos es el dueno del dato,
/// pero lo unico que guarda es la referencia opaca de la boveda de PayKlik y
/// metadatos para mostrarla: marca, ultimos cuatro, vencimiento y titular. Nunca el
/// numero completo ni el CVV: el cobro se hace pidiendo a PayKlik un token de un
/// solo uso desde la referencia, con el CVV que el cliente escribe en ese momento.
///
/// Hay a lo sumo una tarjeta activa por cliente con la misma marca, ultimos cuatro y
/// vencimiento: guardarla otra vez reemplaza la referencia en vez de duplicarla.
/// La baja es logica (<see cref="Activa" /> en false).
/// </summary>
public sealed class TarjetaGuardada
{
    public const int LargoMaximoClienteId = 254;
    public const int LargoMaximoReferencia = 128;
    public const int LargoMaximoTitular = 120;

    private TarjetaGuardada()
    {
        ClienteId = string.Empty;
        ReferenciaPayKlik = string.Empty;
        Ultimos4 = string.Empty;
        Titular = string.Empty;
    }

    public TarjetaGuardada(
        string clienteId,
        string referenciaPayKlik,
        MarcaTarjeta marca,
        string ultimos4,
        int mesExp,
        int anioExp,
        string? titular,
        DateTime creadaEn)
    {
        if (string.IsNullOrWhiteSpace(clienteId) || clienteId.Length > LargoMaximoClienteId)
        {
            throw new ArgumentException("El cliente de la tarjeta no es valido.", nameof(clienteId));
        }

        if (string.IsNullOrWhiteSpace(ultimos4) || ultimos4.Length != 4 || !ultimos4.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Los ultimos cuatro digitos deben ser 4 digitos.", nameof(ultimos4));
        }

        if (mesExp is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(mesExp), "El mes de vencimiento debe estar entre 1 y 12.");
        }

        var anio = ReglasTarjeta.NormalizarAnio(anioExp);
        if (anio is < 2000 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(anioExp), "El anio de vencimiento no es valido.");
        }

        Id = Guid.NewGuid();
        ReferenciaPayKlik = string.Empty;
        Titular = string.Empty;
        ClienteId = clienteId;
        Marca = marca;
        Ultimos4 = ultimos4;
        MesExp = mesExp;
        AnioExp = anio;
        Activa = true;
        CreadaEn = creadaEn.ToUniversalTime();
        ActualizarReferencia(referenciaPayKlik, titular);
    }

    public Guid Id { get; private set; }

    /// <summary>Email del cliente (claim email del JWT), el mismo identificador que usa Ordenes.</summary>
    public string ClienteId { get; private set; }

    /// <summary>Referencia opaca de la boveda de PayKlik (card_pk_...). No es un dato de tarjeta.</summary>
    public string ReferenciaPayKlik { get; private set; }

    public MarcaTarjeta Marca { get; private set; }

    public string Ultimos4 { get; private set; }

    public int MesExp { get; private set; }

    public int AnioExp { get; private set; }

    public string Titular { get; private set; }

    public bool Activa { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public DateTime? UltimoUsoEn { get; private set; }

    public string Enmascarado => ReglasTarjeta.Enmascarar(Ultimos4);

    public bool EstaVencida(DateTime ahora) => ReglasTarjeta.EstaVencida(MesExp, AnioExp, ahora);

    /// <summary>La misma tarjeta se guardo otra vez: la referencia nueva reemplaza a la anterior.</summary>
    public void ActualizarReferencia(string referenciaPayKlik, string? titular)
    {
        if (string.IsNullOrWhiteSpace(referenciaPayKlik) || referenciaPayKlik.Length > LargoMaximoReferencia)
        {
            throw new ArgumentException("La referencia de PayKlik no es valida.", nameof(referenciaPayKlik));
        }

        ReferenciaPayKlik = referenciaPayKlik.Trim();

        var titularLimpio = (titular ?? string.Empty).Trim();
        if (titularLimpio.Length > LargoMaximoTitular)
        {
            titularLimpio = titularLimpio[..LargoMaximoTitular];
        }

        if (titularLimpio.Length > 0 || string.IsNullOrEmpty(Titular))
        {
            Titular = titularLimpio;
        }
    }

    public void RegistrarUso(DateTime ahora) => UltimoUsoEn = ahora.ToUniversalTime();

    /// <summary>Baja logica. Idempotente.</summary>
    public void Desactivar() => Activa = false;
}
