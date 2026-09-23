using System.Text;
using System.Text.Json;

namespace SgoLogic.Web.Services;

public static class PermisosHelper
{
    /// <summary>
    /// Extrae el rol oficial y criptográficamente firmado del token JWT emitido por Auth.Api.
    /// Utiliza decodificación nativa JSON/Base64Url de .NET 8 sin dependencias externas.
    /// </summary>
    public static string ObtenerRolDesdeJwt(string? jwtToken)
    {
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            return "INVITADO";
        }

        var parts = jwtToken.Split('.');
        if (parts.Length < 2)
        {
            return "INVITADO";
        }

        try
        {
            string payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            byte[] bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;

            if (root.TryGetProperty("rol", out var rolProp) ||
                root.TryGetProperty("role", out rolProp) ||
                root.TryGetProperty("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", out rolProp))
            {
                var val = rolProp.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val.Trim().ToUpperInvariant();
                }
            }
        }
        catch
        {
            // Token corrupto o invalido
        }

        return "CLIENTE";
    }

    /// <summary>
    /// Determina el rol del usuario utilizando prioritariamente el token JWT firmado de la API.
    /// </summary>
    public static string DeterminarRol(string? userEmail, string? jwtToken = null)
    {
        if (!string.IsNullOrWhiteSpace(jwtToken))
        {
            var rolJwt = ObtenerRolDesdeJwt(jwtToken);
            if (rolJwt != "INVITADO")
            {
                return rolJwt;
            }
        }

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return "INVITADO";
        }

        return "CLIENTE";
    }

    /// <summary>
    /// Valida si el usuario actual posee permisos de personal operativo o administrativo.
    /// Valida contra el token JWT criptográfico firmado por Auth.Api.
    /// </summary>
    public static bool EsPersonalAutorizado(string? userRole, string? userEmail, string? jwtToken = null)
    {
        // 1. Si hay JWT, su claim firmado es la maxima autoridad criptografica
        if (!string.IsNullOrWhiteSpace(jwtToken))
        {
            var rolJwt = ObtenerRolDesdeJwt(jwtToken);
            return rolJwt == "ADMIN" || rolJwt == "OPERADOR";
        }

        // 2. Si se proporciona userRole, verificar que sea ADMIN u OPERADOR
        if (!string.IsNullOrWhiteSpace(userRole))
        {
            var rol = userRole.Trim().ToUpperInvariant();
            if (rol == "ADMIN" || rol == "OPERADOR")
            {
                return true;
            }
        }

        return false;
    }

    public static bool EsAdministrador(string? jwtToken, string? userRole = null)
    {
        if (!string.IsNullOrWhiteSpace(jwtToken))
        {
            return ObtenerRolDesdeJwt(jwtToken) == "ADMIN";
        }

        return string.Equals(userRole, "ADMIN", StringComparison.OrdinalIgnoreCase);
    }
}

