using SgoLogic.Pagos.Api.Application.Tarjetas;

namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IClienteBovedaPayKlik" />. Responde lo que se le arme y
/// recuerda con que referencias lo llamaron (nunca el CVV).
/// </summary>
public sealed class ClienteBovedaPayKlikFalso : IClienteBovedaPayKlik
{
    public EstadoTokenBoveda Responder { get; set; } = EstadoTokenBoveda.Emitido;

    public List<string> ReferenciasConsultadas { get; } = new();

    public Task<ResultadoTokenBoveda> GenerarTokenAsync(string referenciaTarjeta, string cvv, CancellationToken cancelacion)
    {
        ReferenciasConsultadas.Add(referenciaTarjeta);

        var ahora = DateTime.UtcNow;
        return Task.FromResult(Responder switch
        {
            EstadoTokenBoveda.Emitido => new ResultadoTokenBoveda(
                EstadoTokenBoveda.Emitido,
                new TokenTarjetaDto("tok_pk_0123456789abcdef01234567", "1111", "VISA", 12, 2030, ahora, ahora.AddMinutes(15), 900, true)),
            EstadoTokenBoveda.Rechazado => new ResultadoTokenBoveda(EstadoTokenBoveda.Rechazado, Mensaje: "El codigo de seguridad no es valido para esta tarjeta."),
            _ => new ResultadoTokenBoveda(Responder)
        });
    }
}
