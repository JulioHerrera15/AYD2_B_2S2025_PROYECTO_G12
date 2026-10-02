using System.Security.Cryptography;

namespace SgoLogic.Pagos.Api.Domain;

/// <summary>Ciclo de vida de un <see cref="TokenPago" />. Se persiste como texto.</summary>
public enum EstadoTokenPago
{
    /// <summary>Vigente: se puede usar para cobrar, tambien en un reintento.</summary>
    ACTIVO,

    /// <summary>PayKlik dio una respuesta definitiva al cobro. Ya no tiene datos cifrados.</summary>
    CONSUMIDO,

    /// <summary>Paso su ExpiraEn sin consumirse. Ya no tiene datos cifrados.</summary>
    EXPIRADO
}

/// <summary>
/// Boveda transitoria de Pagos (A2): el token opaco que viaja por Ordenes en lugar
/// de los datos de la tarjeta. Guarda, cifrados con AES-GCM, el PAN y el CVV de una
/// tarjeta nueva, o solo el CVV si el token sale de una tarjeta guardada
/// (<see cref="ReferenciaTarjetaGuardadaId" />). Los datos en claro solo existen en
/// memoria mientras se cobra.
///
/// En cuanto PayKlik responde de forma definitiva el token pasa a CONSUMIDO, y al
/// vencer a EXPIRADO; en ambos casos se borran los datos cifrados. Mientras PayKlik
/// no responda sigue ACTIVO, para que el reintento cobre sin pedir datos otra vez.
///
/// <see cref="Version" /> es el token de concurrencia (bloqueo optimista): dos
/// cobros que intenten consumirlo a la vez no pueden ganar ambos.
/// </summary>
public sealed class TokenPago
{
    public const string Prefijo = "tok_";
    public const int LargoMaximoClienteId = 254;

    private TokenPago()
    {
        Id = string.Empty;
        Ultimos4 = string.Empty;
        Titular = string.Empty;
    }

    private TokenPago(
        string id,
        string? clienteId,
        MarcaTarjeta marca,
        string ultimos4,
        int mesExp,
        int anioExp,
        string titular,
        DatosCifradosTarjeta datosCifrados,
        Guid? referenciaTarjetaGuardadaId,
        bool guardarAlCobrar,
        DateTime creadoEn,
        TimeSpan vigencia)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(Prefijo, StringComparison.Ordinal))
        {
            throw new ArgumentException("El identificador del token no es valido.", nameof(id));
        }

        if (vigencia <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(vigencia), "La vigencia del token debe ser positiva.");
        }

        if (ultimos4.Length != 4 || !ultimos4.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Los ultimos cuatro digitos deben ser 4 digitos.", nameof(ultimos4));
        }

        var cliente = string.IsNullOrWhiteSpace(clienteId) ? null : clienteId.Trim().ToLowerInvariant();
        if (cliente is { Length: > LargoMaximoClienteId })
        {
            throw new ArgumentException("El cliente del token no es valido.", nameof(clienteId));
        }

        Id = id;
        ClienteId = cliente;
        Marca = marca;
        Ultimos4 = ultimos4;
        MesExp = mesExp;
        AnioExp = anioExp;
        Titular = titular;
        DatosCifrados = datosCifrados.Cifrado;
        Nonce = datosCifrados.Nonce;
        Tag = datosCifrados.Tag;
        ReferenciaTarjetaGuardadaId = referenciaTarjetaGuardadaId;

        // Guardar la tarjeta solo tiene sentido para un cliente identificado y una
        // tarjeta que aun no esta guardada.
        GuardarAlCobrar = guardarAlCobrar && cliente is not null && referenciaTarjetaGuardadaId is null;
        CreadoEn = creadoEn.ToUniversalTime();
        ExpiraEn = CreadoEn + vigencia;
        Estado = EstadoTokenPago.ACTIVO;
    }

    /// <summary>
    /// Token de una tarjeta nueva: PAN y CVV cifrados. <paramref name="id" /> sale de
    /// <see cref="GenerarId" /> antes de cifrar, porque el cifrado lo usa como dato
    /// asociado: un blob copiado a otra fila no se puede descifrar.
    /// </summary>
    public static TokenPago DesdeTarjetaNueva(
        string id,
        string? clienteId,
        TarjetaValidada tarjeta,
        DatosCifradosTarjeta datosCifrados,
        bool guardarAlCobrar,
        DateTime creadoEn,
        TimeSpan vigencia)
    {
        ArgumentNullException.ThrowIfNull(tarjeta);

        return new TokenPago(
            id,
            clienteId,
            tarjeta.Marca,
            tarjeta.Ultimos4,
            tarjeta.MesExp,
            tarjeta.AnioExp,
            tarjeta.Titular,
            datosCifrados,
            referenciaTarjetaGuardadaId: null,
            guardarAlCobrar,
            creadoEn,
            vigencia);
    }

    /// <summary>Token de una tarjeta guardada: solo el CVV va cifrado.</summary>
    public static TokenPago DesdeTarjetaGuardada(
        string id,
        string? clienteId,
        TarjetaGuardada tarjeta,
        DatosCifradosTarjeta cvvCifrado,
        DateTime creadoEn,
        TimeSpan vigencia)
    {
        ArgumentNullException.ThrowIfNull(tarjeta);

        return new TokenPago(
            id,
            clienteId,
            tarjeta.Marca,
            tarjeta.Ultimos4,
            tarjeta.MesExp,
            tarjeta.AnioExp,
            tarjeta.Titular,
            cvvCifrado,
            tarjeta.Id,
            guardarAlCobrar: false,
            creadoEn,
            vigencia);
    }

    /// <summary>Opaco: tok_ seguido de 32 hex de bytes aleatorios. No deriva de la tarjeta.</summary>
    public string Id { get; private set; }

    /// <summary>Email del cliente autenticado que tokenizo; null para invitados.</summary>
    public string? ClienteId { get; private set; }

    public MarcaTarjeta Marca { get; private set; }

    public string Ultimos4 { get; private set; }

    public int MesExp { get; private set; }

    public int AnioExp { get; private set; }

    public string Titular { get; private set; }

    public byte[]? DatosCifrados { get; private set; }

    public byte[]? Nonce { get; private set; }

    public byte[]? Tag { get; private set; }

    public Guid? ReferenciaTarjetaGuardadaId { get; private set; }

    public bool GuardarAlCobrar { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ExpiraEn { get; private set; }

    public EstadoTokenPago Estado { get; private set; }

    public int Version { get; private set; }

    public bool EsDeTarjetaGuardada => ReferenciaTarjetaGuardadaId is not null;

    public bool TieneDatosCifrados => DatosCifrados is not null && Nonce is not null && Tag is not null;

    public bool EstaVencido(DateTime ahora) => ahora.ToUniversalTime() >= ExpiraEn;

    /// <summary>ACTIVO, sin vencer y con los datos cifrados todavia presentes.</summary>
    public bool EstaVigente(DateTime ahora) =>
        Estado == EstadoTokenPago.ACTIVO && !EstaVencido(ahora) && TieneDatosCifrados;

    /// <summary>
    /// Un token sin dueno (invitado) lo puede usar cualquier pago; uno con dueno solo
    /// un pago de ese mismo cliente.
    /// </summary>
    public bool PerteneceA(string? clienteId) =>
        ClienteId is null
        || string.Equals(ClienteId, clienteId?.Trim(), StringComparison.OrdinalIgnoreCase);

    public DatosCifradosTarjeta ObtenerDatosCifrados()
    {
        if (!TieneDatosCifrados)
        {
            throw new InvalidOperationException("El token ya no tiene datos de tarjeta.");
        }

        return new DatosCifradosTarjeta(DatosCifrados!, Nonce!, Tag!);
    }

    /// <summary>PayKlik respondio de forma definitiva: se borran los datos cifrados. Idempotente.</summary>
    public void Consumir()
    {
        if (Estado == EstadoTokenPago.CONSUMIDO)
        {
            return;
        }

        Estado = EstadoTokenPago.CONSUMIDO;
        BorrarDatos();
    }

    /// <summary>Vencio sin consumirse: se borran los datos cifrados. No toca un token ya consumido.</summary>
    public void Expirar()
    {
        if (Estado != EstadoTokenPago.ACTIVO)
        {
            return;
        }

        Estado = EstadoTokenPago.EXPIRADO;
        BorrarDatos();
    }

    private void BorrarDatos()
    {
        if (DatosCifrados is not null)
        {
            CryptographicOperations.ZeroMemory(DatosCifrados);
        }

        DatosCifrados = null;
        Nonce = null;
        Tag = null;
        Version++;
    }

    public static string GenerarId() =>
        Prefijo + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

/// <summary>Salida de AES-GCM: texto cifrado, nonce y tag de autenticacion.</summary>
public sealed record DatosCifradosTarjeta(byte[] Cifrado, byte[] Nonce, byte[] Tag)
{
    public override string ToString() => nameof(DatosCifradosTarjeta) + " { contenido omitido }";
}
