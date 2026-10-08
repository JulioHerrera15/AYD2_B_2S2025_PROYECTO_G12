namespace SgoLogic.Credito.Api.Application;

/// <summary>Opciones generales del servicio (seccion Credito). La politica va en Credito:Politica.</summary>
public sealed class CreditoOptions
{
    public const string Seccion = "Credito";

    /// <summary>Al arrancar, si historial_cliente esta vacia, aplica Datos/historial-demo.json.</summary>
    public bool SembrarDemo { get; set; }

    /// <summary>Habilita POST /interno/credito/historiales (demo y pruebas). Falso por defecto.</summary>
    public bool EndpointsInternos { get; set; }
}

/// <summary>Cobranza (seccion Credito:Cobranza): abonos, recordatorios y vencimientos.</summary>
public sealed class CobranzaOptions
{
    public const string Seccion = "Credito:Cobranza";

    /// <summary>Abono minimo; si el saldo es menor, solo se acepta el saldo completo.</summary>
    public decimal MontoMinimoAbono { get; set; } = 10m;

    /// <summary>Cada cuanto corre el worker de vencimientos.</summary>
    public int IntervaloSegundos { get; set; } = 60;

    /// <summary>Dias antes del vencimiento en que se emite el recordatorio (una sola vez).</summary>
    public int DiasRecordatorio { get; set; } = 3;

    /// <summary>Cada cuanto se piden los reembolsos de abonos en REEMBOLSO_PENDIENTE sin solicitud.</summary>
    public int IntervaloReembolsosSegundos { get; set; } = 60;
}
