using Microsoft.AspNetCore.Mvc;
using SgoLogic.Auth.Api.Application;

namespace SgoLogic.Auth.Api.Controllers;

/// <summary>
/// Endpoints de identidad: registro, verificacion de email y login.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>
    /// Registra un usuario nuevo. Devuelve el usuarioId y envia un codigo de
    /// verificacion de 6 digitos al correo (en demo, se loguea en consola).
    /// </summary>
    [HttpPost("registro")]
    [ProducesResponseType<RegistroResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Registro([FromBody] RegistroRequest request, CancellationToken ct)
    {
        try
        {
            var resultado = await _auth.RegistrarAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, resultado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Conflicto", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Reenvia un codigo de verificacion vigente al correo de un usuario no verificado.
    /// </summary>
    [HttpPost("reenviar-codigo")]
    [ProducesResponseType<RegistroResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReenviarCodigo([FromBody] ReenviarCodigoRequest request, CancellationToken ct)
    {
        try
        {
            var resultado = await _auth.ReenviarCodigoAsync(request.Email, ct);
            return Ok(resultado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "No encontrado", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Verifica el email con el codigo de 6 digitos recibido.
    /// Hasta que no se verifica, el login devuelve 401.
    /// </summary>
    [HttpPost("verificar")]
    [ProducesResponseType<VerificarEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verificar([FromBody] VerificarEmailRequest request, CancellationToken ct)
    {
        try
        {
            var resultado = await _auth.VerificarEmailAsync(request, ct);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "No encontrado", Detail = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ProblemDetails { Title = "No autorizado", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Inicia sesion y devuelve un JWT. Falla con 401 si el email no esta verificado.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try
        {
            var resultado = await _auth.LoginAsync(request, ct);
            return Ok(resultado);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ProblemDetails { Title = "No autorizado", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Asigna o actualiza el rol oficial del usuario (CLIENTE, OPERADOR, ADMIN).
    /// Rechaza cualquier rol ficticio o invalido.
    /// </summary>
    [HttpPost("asignar-rol")]
    [ProducesResponseType<AsignarRolResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AsignarRol([FromBody] AsignarRolRequest request, CancellationToken ct)
    {
        try
        {
            var resultado = await _auth.AsignarRolAsync(request, ct);
            return Ok(resultado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "No encontrado", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Consulta los datos del usuario registrado por correo electronico.
    /// </summary>
    [HttpGet("usuario")]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerUsuario([FromQuery] string email, CancellationToken ct)
    {
        try
        {
            var usuario = await _auth.ObtenerUsuarioPorEmailAsync(email, ct);
            return Ok(usuario);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Solicitud invalida", Detail = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "No encontrado", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Lista todos los usuarios registrados en el sistema.
    /// </summary>
    [HttpGet("usuarios")]
    [ProducesResponseType<List<UsuarioDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarUsuarios(CancellationToken ct)
    {
        var usuarios = await _auth.ListarUsuariosAsync(ct);
        return Ok(usuarios);
    }
}

