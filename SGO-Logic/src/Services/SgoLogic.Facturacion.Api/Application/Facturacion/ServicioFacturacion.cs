using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Facturacion.Api.Domain;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

namespace SgoLogic.Facturacion.Api.Application.Facturacion;

/// <summary>
/// Nucleo de la primera mitad de HU-005. Genera el documento fiscal (XML con CUI y
/// serie autorizada), lo persiste con serie y correlativo asignados UNA sola vez, y
/// gestiona el envio asincrono a la SAT con reintentos por backoff persistido en
/// <c>proximo_intento_en</c> (ADR-011).
///
/// No llama a <c>SaveChangesAsync</c>: la transaccion la cierra quien llama
/// (<c>EventConsumerBase</c> para la certificacion solicitada; el enviador y el
/// worker de reintentos para el envio a la SAT).
/// </summary>
public sealed class ServicioFacturacion
{
    private readonly FacturacionDbContext _contexto;
    private readonly IClienteSat _sat;
    private readonly IOutboxWriter _outbox;
    private readonly IGeneradorCorrelativoFiscal _correlativo;
    private readonly GeneradorXmlFiscal _generadorXml;
    private readonly SatOptions _opciones;
    private readonly ILogger<ServicioFacturacion> _logger;

    public ServicioFacturacion(
        FacturacionDbContext contexto,
        IClienteSat sat,
        IOutboxWriter outbox,
        IGeneradorCorrelativoFiscal correlativo,
        GeneradorXmlFiscal generadorXml,
        IOptions<SatOptions> opciones,
        ILogger<ServicioFacturacion> logger)
    {
        _contexto = contexto;
        _sat = sat;
        _outbox = outbox;
        _correlativo = correlativo;
        _generadorXml = generadorXml;
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <summary>
    /// Crea el documento fiscal de una orden a partir de la certificacion
    /// solicitada. Idempotente por orden: si ya existe un documento para esa orden,
    /// lo devuelve sin crear otro ni consumir un nuevo correlativo. Encolar el envio
    /// a la SAT es trabajo del worker de reintentos, no de aca: este metodo no
    /// bloquea el flujo de la orden.
    /// </summary>
    public async Task<DocumentoFiscal> CrearDocumentoAsync(
        CertificacionFiscalSolicitadaData datos,
        string transactionId,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var existente = await _contexto.DocumentosFiscales
            .Include(d => d.Lineas)
            .FirstOrDefaultAsync(d => d.OrdenId == datos.OrdenId, cancelacion);

        if (existente is not null)
        {
            _logger.LogInformation(
                "La orden {OrdenId} ya tiene el documento fiscal {DocumentoId}; no se crea otro.",
                datos.OrdenId, existente.Id);
            return existente;
        }

        var correlativo = await _correlativo.SiguienteAsync(cancelacion);
        var numeroDocumento = correlativo.ToString("D8", CultureInfo.InvariantCulture);

        var lineasXml = datos.Lineas
            .Select((linea, indice) => new DatosXmlLinea(
                indice + 1, linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, linea.Impuesto))
            .ToList();

        var xml = _generadorXml.Generar(new DatosXmlFiscal(
            datos.OrdenId,
            datos.TipoDocumento,
            _opciones.Serie,
            numeroDocumento,
            datos.Receptor.Cui,
            datos.Receptor.Nombre,
            datos.Receptor.Direccion,
            datos.Receptor.Pais,
            datos.Moneda,
            datos.Subtotal,
            datos.Impuestos,
            datos.Total,
            ahora,
            lineasXml));

        var lineas = datos.Lineas
            .Select((linea, indice) => new DocumentoFiscalLinea(
                indice + 1, linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, linea.Impuesto))
            .ToList();

        var documento = new DocumentoFiscal(
            datos.OrdenId,
            transactionId,
            datos.TipoDocumento,
            datos.DocumentoOrigenId,
            datos.Receptor.Cui,
            datos.Receptor.Nombre,
            datos.Receptor.Direccion,
            datos.Receptor.Pais,
            datos.Moneda,
            datos.Subtotal,
            datos.Impuestos,
            datos.Total,
            _opciones.Serie,
            correlativo,
            xml,
            lineas,
            ahora);

        _contexto.DocumentosFiscales.Add(documento);

        _logger.LogInformation(
            "Documento fiscal {DocumentoId} creado para la orden {OrdenId}, serie {Serie} numero {Numero}.",
            documento.Id, documento.OrdenId, documento.Serie, documento.NumeroDocumento);

        return documento;
    }

    /// <summary>Documentos vencidos que el worker de reintentos debe reencolar hacia la SAT.</summary>
    public Task<List<DocumentoFiscal>> TomarDocumentosVencidosAsync(int lote, DateTime ahora, CancellationToken cancelacion) =>
        _contexto.DocumentosFiscales
            .Where(d => d.Estado == EstadoDocumentoFiscal.PENDIENTE
                && d.ProximoIntentoEn != null
                && d.ProximoIntentoEn <= ahora)
            .OrderBy(d => d.ProximoIntentoEn)
            .Take(lote)
            .ToListAsync(cancelacion);

    /// <summary>
    /// El worker reencola un documento vencido hacia la SAT: reserva la siguiente
    /// secuencia, encola <see cref="SolicitudEnvioSat" /> por Outbox sobre
    /// <c>sgo.sat</c> y empuja <c>proximo_intento_en</c> para no volver a tomarlo
    /// mientras el envio esta en curso.
    /// </summary>
    public void ReencolarEnvio(DocumentoFiscal documento, DateTime ahora)
    {
        var intento = documento.Intentos + 1;
        var secuencia = documento.SiguienteSecuencia();

        _outbox.Encolar(
            SolicitudEnvioSat.EventType,
            SolicitudEnvioSat.AggregateType,
            documento.Id.ToString(),
            secuencia,
            TopologiaMensajeria.ExchangeSat,
            SolicitudEnvioSat.RoutingKey,
            new SolicitudEnvioSatData(documento.Id.ToString(), documento.OrdenId, intento));

        documento.MarcarReencolado(ahora, _opciones.MargenReencolado);
    }

    /// <summary>
    /// Envia el documento a la SAT y aplica el desenlace: certificado (folio y
    /// autorizacion, se encola <see cref="DocumentoFiscalCertificado" />), o fallo
    /// (rechazo, timeout o 5xx: se programa el reintento con backoff; al agotar
    /// MaxIntentos el documento pasa a ALERTA y se encola
    /// <see cref="CertificacionSatFallida" />). No lanza por un desenlace de
    /// negocio; solo suben las excepciones tecnicas del propio lado.
    /// </summary>
    /// <param name="intento">Numero de intento que representa el mensaje que dispara este envio.</param>
    public async Task ProcesarEnvioAsync(
        Guid documentoFiscalId,
        int intento,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var documento = await _contexto.DocumentosFiscales
            .Include(d => d.Lineas)
            .FirstOrDefaultAsync(d => d.Id == documentoFiscalId, cancelacion);

        if (documento is null)
        {
            throw new InvalidOperationException($"No existe el documento fiscal {documentoFiscalId}.");
        }

        if (documento.Estado != EstadoDocumentoFiscal.PENDIENTE)
        {
            _logger.LogInformation(
                "El documento fiscal {DocumentoId} ya esta en estado {Estado}; se ignora el envio.",
                documento.Id, documento.Estado);
            return;
        }

        // Un reencolado duplicado trae un Intento que ya quedo atras: se descarta
        // para no contar dos veces contra MaxIntentos.
        if (intento <= documento.Intentos)
        {
            _logger.LogInformation(
                "Solicitud de envio obsoleta para {DocumentoId} (intento {Intento}, ya van {Intentos}); se ignora.",
                documento.Id, intento, documento.Intentos);
            return;
        }

        var peticion = new PeticionCertificacionSat(
            documento.Serie,
            documento.NumeroDocumento,
            documento.CuiReceptor,
            documento.Xml,
            documento.Total.ToString("0.00", CultureInfo.InvariantCulture),
            documento.Moneda);

        ResultadoCertificacionSat? resultado = null;
        var codigoFalla = string.Empty;
        var motivoFalla = string.Empty;

        try
        {
            // La clave de idempotencia hacia la SAT es el id del DOCUMENTO, no del
            // intento: un reintento no genera una certificacion duplicada.
            resultado = await _sat.CertificarAsync(peticion, documento.Id.ToString(), cancelacion);
        }
        catch (SatNoDisponibleException ex)
        {
            (codigoFalla, motivoFalla) = ("SAT_NO_DISPONIBLE", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            (codigoFalla, motivoFalla) = ("RED", $"Error de red invocando a la SAT: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
        {
            (codigoFalla, motivoFalla) = ("TIMEOUT", "La SAT no respondio dentro del timeout configurado.");
        }

        if (resultado is { Estado: EstadoCertificacionSat.CERTIFICADO })
        {
            documento.MarcarCertificado(
                resultado.Folio ?? string.Empty,
                resultado.NumeroAutorizacion ?? string.Empty,
                ahora);

            EncolarCertificado(documento, ahora);

            _logger.LogInformation(
                "Documento fiscal {DocumentoId} certificado por la SAT: autorizacion {Autorizacion}.",
                documento.Id, documento.NumeroAutorizacion);
            return;
        }

        var (codigo, motivo) = resultado is { Estado: EstadoCertificacionSat.RECHAZADO }
            ? (resultado.CodigoRespuesta, resultado.Mensaje)
            : (codigoFalla, motivoFalla);

        documento.RegistrarUltimoError(codigo, motivo);
        documento.ProgramarReintento(_opciones.MaxIntentos, _opciones.BackoffBaseMs, ahora, Random.Shared.NextDouble);

        if (documento.Estado == EstadoDocumentoFiscal.ALERTA)
        {
            EncolarFallida(documento, ahora);
            _logger.LogError(
                "Documento fiscal {DocumentoId} agoto {MaxIntentos} intentos ante la SAT; pasa a ALERTA.",
                documento.Id, _opciones.MaxIntentos);
        }
        else
        {
            _logger.LogWarning(
                "Documento fiscal {DocumentoId}: intento {Intentos} fallo ({Codigo}); proximo intento en {Proximo:o}.",
                documento.Id, documento.Intentos, codigo, documento.ProximoIntentoEn);
        }
    }

    private void EncolarCertificado(DocumentoFiscal documento, DateTime ahora)
    {
        var secuencia = documento.SiguienteSecuencia();

        _outbox.Encolar(
            DocumentoFiscalCertificado.EventType,
            DocumentoFiscalCertificado.AggregateType,
            documento.Id.ToString(),
            secuencia,
            TopologiaMensajeria.ExchangeEventos,
            DocumentoFiscalCertificado.RoutingKey,
            new DocumentoFiscalCertificadoData(
                documento.Id.ToString(),
                documento.OrdenId,
                documento.TipoDocumento,
                documento.Serie,
                documento.NumeroDocumento,
                documento.NumeroAutorizacion ?? string.Empty,
                documento.Folio ?? string.Empty,
                documento.Moneda,
                documento.Total,
                documento.CertificadoEn ?? ahora));
    }

    private void EncolarFallida(DocumentoFiscal documento, DateTime ahora)
    {
        var secuencia = documento.SiguienteSecuencia();

        _outbox.Encolar(
            CertificacionSatFallida.EventType,
            CertificacionSatFallida.AggregateType,
            documento.Id.ToString(),
            secuencia,
            TopologiaMensajeria.ExchangeEventos,
            CertificacionSatFallida.RoutingKey,
            new CertificacionSatFallidaData(
                documento.Id.ToString(),
                documento.OrdenId,
                documento.TipoDocumento,
                documento.Serie,
                documento.NumeroDocumento,
                documento.Intentos,
                documento.UltimoCodigo ?? string.Empty,
                documento.UltimoMotivo ?? string.Empty,
                ahora.ToUniversalTime()));
    }
}
