using System.Globalization;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>Texto para el cliente: que pasa y que puede hacer.</summary>
public sealed record MensajeCredito(string Titulo, string Mensaje, string Recomendacion);

/// <summary>
/// Unico lugar con los textos de credito que ve el cliente. Lenguaje llano, sin
/// codigos internos, y sin hablar de puntajes, sistemas de origen ni sospechas
/// sobre la cuenta.
///
/// Los montos se escriben a mano (Q1,500.00) y las fechas con meses en espanol:
/// la imagen alpine corre en modo de globalizacion invariante y no trae la cultura
/// es-GT.
/// </summary>
public static class CatalogoMensajesCredito
{
    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };

    private const string RevisionPeriodica =
        "Revisamos tu historial periódicamente y esta opción se habilitará sola cuando mejore. Mientras tanto, puedes pagar con tarjeta.";

    public static MensajeCredito Disponible(decimal disponible, decimal limite, int plazoDias, decimal? monto, DateTime? fechaLimitePago)
    {
        var mensaje = $"Tienes {Monto(disponible)} disponibles de un límite de {Monto(limite)}. "
            + $"Puedes comprar ahora y pagar en {plazoDias} días, sin recargo.";

        if (monto is not null && fechaLimitePago is not null)
        {
            mensaje += $" Si pagas esta compra de {Monto(monto.Value)} con crédito, la fecha límite de pago será el {Fecha(fechaLimitePago.Value)}.";
        }

        return new MensajeCredito(
            "Tienes crédito disponible",
            mensaje,
            $"Elige crédito al pagar y cancela el total desde Mi crédito dentro de los {plazoDias} días.");
    }

    public static MensajeCredito CupoInsuficiente(decimal disponible, decimal monto) =>
        new(
            "Tu crédito disponible no alcanza para esta compra",
            $"Tu crédito disponible es de {Monto(disponible)} y esta compra suma {Monto(monto)}.",
            "Puedes pagar con tarjeta o reducir el total de tu compra para usar tu crédito.");

    public static MensajeCredito SinHistorial() =>
        new(
            "Aún no podemos ofrecerte crédito",
            "Todavía no tenemos suficiente historial de tus compras para ofrecerte crédito.",
            "Sigue comprando con nosotros. Revisamos tu historial periódicamente y la opción aparecerá aquí cuando esté disponible.");

    public static MensajeCredito LineaBloqueada() =>
        new(
            "Tu crédito está en pausa",
            "Tienes una compra a crédito vencida, por eso por ahora no puedes usar tu crédito en nuevas compras.",
            "Paga la compra vencida desde Mi crédito y tu crédito se reactivará de inmediato. No cobramos intereses ni recargos, y tus compras con tarjeta funcionan con normalidad.");

    // ── Cobranza (bloque 3) ────────────────────────────────────────────────────

    public static MensajeCredito AbonoEnProceso(decimal monto) =>
        new(
            "Estamos procesando tu pago",
            $"Estamos procesando tu pago de {Monto(monto)}. Toma solo unos segundos.",
            "No cierres esta página.");

    public static MensajeCredito AbonoAplicadoParcial(decimal monto, decimal saldoRestante) =>
        new(
            "Recibimos tu pago",
            $"Recibimos tu pago de {Monto(monto)}. Te quedan {Monto(saldoRestante)} por pagar de esta compra.",
            "Puedes pagar el resto cuando quieras antes de la fecha límite.");

    public static MensajeCredito AbonoAplicadoTotal(decimal monto) =>
        new(
            "Tu compra quedó pagada",
            $"Recibimos tu pago de {Monto(monto)} y esta compra quedó pagada. Tu crédito disponible aumentó.",
            "Puedes volver a usar tu crédito en tu próxima compra.");

    public static MensajeCredito AbonoRechazado() =>
        new(
            "Tu banco no autorizó el pago",
            "Tu banco no autorizó el pago y no se hizo ningún cargo.",
            "Revisa los datos de tu tarjeta o intenta con otra tarjeta.");

    public static MensajeCredito AbonoPendiente() =>
        new(
            "Estamos confirmando tu pago",
            "Estamos confirmando tu pago con el banco. No pagues de nuevo todavía.",
            "En unos minutos podrás reintentar el pago sin riesgo de que se te cobre dos veces.");

    public static MensajeCredito AbonoReembolsoPendiente() =>
        new(
            "Recibimos tu pago",
            "Esta compra se canceló antes de que se aplicara tu pago. Te devolveremos ese monto.",
            "Si tienes dudas, comunícate con atención al cliente.");

    public static MensajeCredito CompraPorVencer(string numeroOrden, decimal saldo, DateTime venceEn, int diasRestantes) =>
        new(
            "Tienes una compra que vence pronto",
            $"Tu compra {numeroOrden} tiene {Monto(saldo)} por pagar y vence el {Fecha(venceEn)}"
                + (diasRestantes <= 0 ? ", hoy." : diasRestantes == 1 ? " (falta 1 día)." : $" (faltan {diasRestantes} días)."),
            "Págala desde Mi crédito antes de esa fecha para mantener tu crédito activo.");

    public static MensajeCredito CompraVencida(string numeroOrden, decimal saldo, DateTime venceEn) =>
        new(
            "Tienes una compra vencida",
            $"Tu compra {numeroOrden} venció el {Fecha(venceEn)} y aún tiene {Monto(saldo)} por pagar.",
            "Págala desde Mi crédito. No cobramos intereses ni recargos, y tu crédito se reactiva en cuanto la pagues.");

    // Errores al crear un abono: texto para el cliente.
    public const string CuentaNoEncontrada = "No encontramos esa compra a crédito.";
    public const string AbonoYaEnCurso = "Ya hay un pago en proceso para esta compra. Espera a que termine antes de pagar de nuevo.";
    public const string CuentaNoPagable = "Esta compra no tiene saldo por pagar.";
    public const string AbonoNoEncontrado = "No encontramos ese pago.";
    public const string AbonoNoReintentable = "Este pago no se puede reintentar en este momento.";

    public static string MontoInvalido(decimal saldo, decimal minimo) =>
        saldo < minimo
            ? $"Como te quedan {Monto(saldo)} por pagar, debes pagar el saldo completo."
            : $"El monto debe ser de al menos {Monto(minimo)} y no mayor que tu saldo de {Monto(saldo)}.";

    /// <summary>Etiqueta legible del estado de un abono.</summary>
    public static string EtiquetaEstadoAbono(EstadoAbonoCredito estado) => estado switch
    {
        EstadoAbonoCredito.APLICADO => "Aplicado",
        EstadoAbonoCredito.RECHAZADO => "No autorizado",
        EstadoAbonoCredito.PENDIENTE => "En confirmación",
        EstadoAbonoCredito.SOLICITADO => "En proceso",
        EstadoAbonoCredito.REEMBOLSADO => "Reembolsado",
        _ => "Por reembolsar"
    };

    public const string ReembolsoEnProceso = "Reembolso a tu tarjeta en proceso";
    public const string ReembolsoAplicado = "Reembolsado a tu tarjeta";
    public const string ReembolsoFallido = "No pudimos completar el reembolso; contáctanos";

    /// <summary>
    /// Estado legible del reembolso de un pago: no se pudo (requiere atencion), en proceso
    /// (pedido y aun sin confirmar) o reembolsado. Null si de ese pago no se devuelve nada.
    /// </summary>
    public static string? EstadoReembolsoAbono(AbonoCredito abono)
    {
        if (abono.ReembolsoFallidoEn is not null && abono.Estado != EstadoAbonoCredito.REEMBOLSADO)
        {
            return ReembolsoFallido;
        }

        if (abono.Estado == EstadoAbonoCredito.REEMBOLSO_PENDIENTE || abono.MontoReembolsoSolicitado > abono.MontoReembolsado)
        {
            return ReembolsoEnProceso;
        }

        return abono.MontoReembolsado > 0m ? ReembolsoAplicado : null;
    }

    /// <summary>"Devolviste productos por Q500.00. ..." con lo que bajo la deuda y lo que se reembolsa.</summary>
    public static string DescripcionDevolucion(decimal monto, decimal descontadoDeuda, decimal aReembolsar)
    {
        var texto = $"Devolviste productos por {Monto(monto)}.";
        if (descontadoDeuda > 0m)
        {
            texto += $" Tu deuda bajó {Monto(descontadoDeuda)}.";
        }

        if (aReembolsar > 0m)
        {
            texto += $" Te devolveremos {Monto(aReembolsar)} a tu tarjeta.";
        }

        return texto;
    }

    /// <summary>"Visa terminada en 4242", o null si no se conoce la tarjeta.</summary>
    public static string? Tarjeta(string? marca, string? ultimosCuatro)
    {
        if (string.IsNullOrWhiteSpace(ultimosCuatro))
        {
            return null;
        }

        var nombre = marca?.ToUpperInvariant() switch
        {
            "VISA" => "Visa",
            "MASTERCARD" => "Mastercard",
            "AMEX" => "American Express",
            null or "" => "Tarjeta",
            _ => "Tarjeta"
        };
        return $"{nombre} terminada en {ultimosCuatro}";
    }

    /// <summary>
    /// Mensaje para un cliente no elegible segun el motivo del descarte.
    /// <paramref name="diasFaltantes" /> solo aplica a ANTIGUEDAD_INSUFICIENTE y
    /// <paramref name="factorMasDebil" /> a PUNTAJE_INSUFICIENTE.
    /// </summary>
    public static MensajeCredito NoElegible(string? codigoMotivo, int antiguedadMinimaDias, int? diasFaltantes, FactorCliente? factorMasDebil)
    {
        switch (codigoMotivo)
        {
            case CodigosMotivoCredito.SinHistorial:
                return SinHistorial();

            case CodigosMotivoCredito.AntiguedadInsuficiente:
            {
                var mensaje = $"El crédito se habilita cuando cumples {antiguedadMinimaDias} días como cliente.";
                if (diasFaltantes is > 0)
                {
                    mensaje += diasFaltantes == 1 ? " Te falta 1 día." : $" Te faltan {diasFaltantes} días.";
                }

                return new MensajeCredito(
                    "Tu cuenta es muy reciente",
                    mensaje,
                    "No necesitas hacer nada. Revisamos tu historial periódicamente y la opción aparecerá aquí cuando se cumpla el plazo.");
            }

            case CodigosMotivoCredito.DevolucionesExcesivas:
                return new MensajeCredito(
                    "Por ahora no podemos ofrecerte crédito",
                    "Una parte alta de tus compras recientes terminó en devolución.",
                    RevisionPeriodica);

            case CodigosMotivoCredito.DeudaVencida:
                return LineaBloqueada();

            case CodigosMotivoCredito.IncidenteGrave:
                return new MensajeCredito(
                    "Necesitamos revisar tu cuenta",
                    "Hay un caso pendiente asociado a tu cuenta que debe resolverse antes de habilitar el crédito.",
                    "Comunícate con atención al cliente para resolverlo. Tus compras con tarjeta funcionan con normalidad.");

            default:
                return new MensajeCredito(
                    "Por ahora no podemos ofrecerte crédito",
                    MotivoPorFactor(factorMasDebil),
                    RevisionPeriodica);
        }
    }

    /// <summary>La orden se cancelo antes de que pudieramos confirmar el credito.</summary>
    public static MensajeCredito OrdenCancelada() =>
        new(
            "Tu pedido fue cancelado",
            "Tu pedido se canceló antes de confirmar el crédito. No se realizó ningún cargo.",
            "Si todavía quieres estos productos, puedes hacer un pedido nuevo.");

    /// <summary>
    /// Texto unico que viaja en CreditoRechazado.mensajeCliente y que Ordenes muestra
    /// como detalle del rechazo: que paso y que puede hacer el cliente.
    /// </summary>
    public static string MensajeRechazo(MensajeCredito mensaje) => $"{mensaje.Mensaje} {mensaje.Recomendacion}";

    /// <summary>Etiqueta legible del estado de una compra a credito.</summary>
    public static string EtiquetaEstadoCompra(EstadoCuentaPorCobrar estado, bool vencePronto = false, bool devuelta = false) => estado switch
    {
        EstadoCuentaPorCobrar.VIGENTE => vencePronto ? "Vence pronto" : "Por pagar",
        EstadoCuentaPorCobrar.PAGADA => "Pagada",
        EstadoCuentaPorCobrar.VENCIDA => "Vencida",
        _ => devuelta ? "Devuelta" : "Anulada"
    };

    public static string NombreFactor(FactorCliente factor) => factor switch
    {
        FactorCliente.PagosATiempo => "Pagos a tiempo",
        FactorCliente.MontoCompras => "Monto de tus compras",
        FactorCliente.TiempoYFrecuencia => "Tiempo y frecuencia como cliente",
        _ => "Devoluciones y reclamos"
    };

    public static string DescripcionFactor(FactorCliente factor, string valoracion) => (factor, valoracion) switch
    {
        (FactorCliente.PagosATiempo, ValoracionesFactor.Favorable) => "Pagas tus compras a tiempo.",
        (FactorCliente.PagosATiempo, ValoracionesFactor.Aceptable) => "La mayoría de tus pagos llegan a tiempo, pero algunos se atrasaron.",
        (FactorCliente.PagosATiempo, _) => "Varios de tus pagos se hicieron después de la fecha acordada.",

        (FactorCliente.MontoCompras, ValoracionesFactor.Favorable) => "El monto de tus compras del último año es alto.",
        (FactorCliente.MontoCompras, ValoracionesFactor.Aceptable) => "El monto de tus compras del último año es moderado.",
        (FactorCliente.MontoCompras, _) => "El monto de tus compras del último año todavía es bajo.",

        (FactorCliente.TiempoYFrecuencia, ValoracionesFactor.Favorable) => "Llevas buen tiempo como cliente y compras con frecuencia.",
        (FactorCliente.TiempoYFrecuencia, ValoracionesFactor.Aceptable) => "Tu tiempo como cliente y tu frecuencia de compra van creciendo.",
        (FactorCliente.TiempoYFrecuencia, _) => "Aún llevas poco tiempo como cliente o compras con poca frecuencia.",

        (_, ValoracionesFactor.Favorable) => "Casi no tienes devoluciones ni reclamos.",
        (_, ValoracionesFactor.Aceptable) => "Tienes algunas devoluciones o reclamos.",
        _ => "Una parte importante de tus compras terminó en devolución o reclamo."
    };

    /// <summary>Etiqueta del nivel para mostrar; nunca el nombre del enum.</summary>
    public static string EtiquetaNivel(NivelCredito nivel) => nivel switch
    {
        NivelCredito.EXCELENTE => "Excelente",
        NivelCredito.BUENO => "Bueno",
        NivelCredito.REGULAR => "Regular",
        _ => "Sin crédito por ahora"
    };

    public static string Monto(decimal monto) =>
        "Q" + monto.ToString("#,##0.00", CultureInfo.InvariantCulture);

    public static string Fecha(DateTime fecha) =>
        $"{fecha.Day} de {Meses[fecha.Month - 1]} de {fecha.Year}";

    private static string MotivoPorFactor(FactorCliente? factor) => factor switch
    {
        FactorCliente.PagosATiempo => "Algunos de tus pagos recientes se hicieron después de la fecha acordada.",
        FactorCliente.DevolucionesYReclamos => "Una parte de tus compras recientes terminó en devolución o reclamo.",
        _ => "Tu historial de compras con nosotros todavía es corto para ofrecerte crédito."
    };
}
