using Microsoft.AspNetCore.Mvc;
using SgoLogic.Sample.Api.Application;

namespace SgoLogic.Sample.Api.Controllers;

/// <summary>
/// CRUD deliberadamente trivial que solo demuestra el flujo de la plantilla.
/// </summary>
[ApiController]
[Route("api/items-ejemplo")]
public sealed class ItemsEjemploController : ControllerBase
{
    private readonly IItemEjemploService _servicio;

    public ItemsEjemploController(IItemEjemploService servicio)
    {
        _servicio = servicio;
    }

    [HttpPost]
    public async Task<ActionResult<ItemEjemploRespuesta>> Crear(
        CrearItemEjemploComando comando,
        CancellationToken cancelacion)
    {
        var creado = await _servicio.CrearAsync(comando, cancelacion);
        return CreatedAtAction(nameof(ObtenerTodos), new { }, creado);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ItemEjemploRespuesta>>> ObtenerTodos(CancellationToken cancelacion)
    {
        return Ok(await _servicio.ObtenerTodosAsync(cancelacion));
    }
}