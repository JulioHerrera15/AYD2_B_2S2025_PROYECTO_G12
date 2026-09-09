using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Direccion de entrega a domicilio. Misma forma en OrdenCreada, DespachoSolicitado
/// y MetodoEntregaModificado. Es null cuando el metodo de entrega es recogida en
/// tienda.
/// </summary>
public sealed record DireccionEntregaData(
    Pais Pais,
    string Departamento,
    string Municipio,
    string Direccion,
    string? Referencia);
