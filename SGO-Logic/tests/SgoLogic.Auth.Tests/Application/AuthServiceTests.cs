using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Auth.Api.Application;
using SgoLogic.Auth.Api.Domain;
using SgoLogic.Auth.Api.Infrastructure;
using Xunit;

namespace SgoLogic.Auth.Tests.Application;

public sealed class AuthServiceTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private AuthDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AuthDbContext(options);
    }

    private static IJwtService CrearJwtService()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Auth:JwtSecret"] = "clave_secreta_super_segura_de_al_menos_32_caracteres_12345",
            ["Auth:JwtExpiracionMinutos"] = "60"
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        return new JwtService(config);
    }

    [Fact]
    public async Task Registro_exitoso_crea_usuario_con_email_no_verificado_y_codigo_vigente()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        var req = new RegistroRequest("test@megastore.com", "Password123!", "Juan Perez");
        var resp = await sut.RegistrarAsync(req);

        Assert.NotNull(resp);
        Assert.Equal("test@megastore.com", resp.Email);

        var usuario = await ctx.Usuarios.FirstOrDefaultAsync(u => u.Email == "test@megastore.com");
        Assert.NotNull(usuario);
        Assert.False(usuario.EmailVerificado);
        Assert.Equal("Juan Perez", usuario.NombreCompleto);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password123!", usuario.HashContrasena));

        var codigo = await ctx.CodigosVerificacion.FirstOrDefaultAsync(c => c.UsuarioId == usuario.Id);
        Assert.NotNull(codigo);
        Assert.False(codigo.Usado);
        Assert.True(codigo.EstaVigente());
    }

    [Fact]
    public async Task Registro_con_email_duplicado_lanza_InvalidOperationException()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        var req = new RegistroRequest("duplicado@megastore.com", "Password123!", "Usuario Uno");
        await sut.RegistrarAsync(req);

        var req2 = new RegistroRequest("duplicado@megastore.com", "OtraPass123!", "Usuario Dos");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RegistrarAsync(req2));
    }

    [Fact]
    public async Task Login_con_email_no_verificado_lanza_UnauthorizedAccessException()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        await sut.RegistrarAsync(new RegistroRequest("noverificado@megastore.com", "Secret123!", "No Verificado"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.LoginAsync(new LoginRequest("noverificado@megastore.com", "Secret123!")));

        Assert.Contains("verificar tu correo", ex.Message);
    }

    [Fact]
    public async Task Login_con_contrasena_incorrecta_lanza_UnauthorizedAccessException()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        await sut.RegistrarAsync(new RegistroRequest("loginerr@megastore.com", "CorrectPassword1!", "Login Test"));

        // Verificar manualmente al usuario
        var user = await ctx.Usuarios.FirstAsync(u => u.Email == "loginerr@megastore.com");
        user.MarcarEmailVerificado();
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.LoginAsync(new LoginRequest("loginerr@megastore.com", "WrongPassword!")));
    }

    [Fact]
    public async Task Login_exitoso_retorna_token_valido()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        await sut.RegistrarAsync(new RegistroRequest("loginok@megastore.com", "GoodPassword123!", "Usuario Login OK"));

        var user = await ctx.Usuarios.FirstAsync(u => u.Email == "loginok@megastore.com");
        user.MarcarEmailVerificado();
        await ctx.SaveChangesAsync();

        var loginResp = await sut.LoginAsync(new LoginRequest("loginok@megastore.com", "GoodPassword123!"));

        Assert.NotNull(loginResp);
        Assert.NotEmpty(loginResp.Token);
        Assert.Equal("loginok@megastore.com", loginResp.Email);
        Assert.Equal("Usuario Login OK", loginResp.NombreCompleto);
        Assert.True(loginResp.ExpiraEn > DateTime.UtcNow);
    }

    [Fact]
    public async Task VerificarEmail_con_codigo_correcto_marca_email_verificado_y_codigo_usado()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        await sut.RegistrarAsync(new RegistroRequest("verificar@megastore.com", "Pass123!", "Verificar Test"));

        var user = await ctx.Usuarios.FirstAsync(u => u.Email == "verificar@megastore.com");
        var codigoEntidad = await ctx.CodigosVerificacion.FirstAsync(c => c.UsuarioId == user.Id);

        // Simulamos un codigo plano conocido
        const string codigoPlano = "123456";
        typeof(CodigoVerificacion)
            .GetProperty(nameof(CodigoVerificacion.HashCodigo))!
            .SetValue(codigoEntidad, BCrypt.Net.BCrypt.HashPassword(codigoPlano, 10));
        await ctx.SaveChangesAsync();

        var resp = await sut.VerificarEmailAsync(new VerificarEmailRequest("verificar@megastore.com", codigoPlano));

        Assert.NotNull(resp);
        Assert.Contains("exitosamente", resp.Mensaje);

        // Verificar estados en DB
        var userDb = await ctx.Usuarios.FirstAsync(u => u.Email == "verificar@megastore.com");
        var codigoDb = await ctx.CodigosVerificacion.FirstAsync(c => c.UsuarioId == user.Id);

        Assert.True(userDb.EmailVerificado);
        Assert.True(codigoDb.Usado);
    }

    [Fact]
    public async Task VerificarEmail_con_codigo_incorrecto_lanza_UnauthorizedAccessException()
    {
        using var ctx = CrearContexto();
        var sut = new AuthService(ctx, CrearJwtService(), NullLogger<AuthService>.Instance);

        await sut.RegistrarAsync(new RegistroRequest("badcode@megastore.com", "Pass123!", "Bad Code Test"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.VerificarEmailAsync(new VerificarEmailRequest("badcode@megastore.com", "000000")));
    }
}
