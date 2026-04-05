using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KiwiAuth.Data;
using KiwiAuth.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KiwiAuth.Services;

public class TokenService
{
    private readonly KiwiAuthOptions _options;

    public TokenService(IOptions<KiwiAuthOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (user.FullName is not null)
            claims.Add(new Claim(ClaimTypes.Name, user.FullName));

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _options.Jwt.Issuer,
            audience: _options.Jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.Jwt.AccessTokenMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string RawToken, string TokenHash) GenerateRefreshToken()
    {
        // 64 random bytes → base64 string (88 chars, URL-safe enough for a cookie value)
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var rawToken = Convert.ToBase64String(randomBytes);
        return (rawToken, HashToken(rawToken));
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Issues a short-lived token indicating password is verified but MFA is still required.
    /// This is NOT a valid access token — it only unlocks POST /auth/mfa/verify.
    /// </summary>
    public string GenerateMfaSessionToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim("mfa_pending", "true"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Jwt.Issuer,
            audience: _options.Jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.Mfa.SessionTokenMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Validates an MFA session token and returns the user ID if valid.
    /// Returns null if the token is invalid, expired, or not an MFA session token.
    /// </summary>
    public string? ValidateMfaSessionToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _options.Jwt.Issuer,
                ValidAudience = _options.Jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(_options.Jwt.SigningKey)),
                ClockSkew = TimeSpan.Zero,
            }, out _);

            // Must carry the mfa_pending claim — reject normal access tokens.
            if (principal.FindFirstValue("mfa_pending") != "true")
                return null;

            return principal.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? principal.FindFirstValue("sub");
        }
        catch
        {
            return null;
        }
    }
}
