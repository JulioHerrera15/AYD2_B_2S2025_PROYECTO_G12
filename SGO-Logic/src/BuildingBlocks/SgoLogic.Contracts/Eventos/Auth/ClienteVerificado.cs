namespace SgoLogic.Contracts.Eventos.Auth;

/// <summary>Un cliente verifico su correo y puede iniciar sesion en el sistema.</summary>
public static class ClienteVerificado
{
    public const string EventType = "ClienteVerificado";
    public const string RoutingKey = "auth.cliente.verificado";
    public const string AggregateType = "Cliente";
}

public sealed record ClienteVerificadoData(string ClienteId, DateTime ClienteDesde);