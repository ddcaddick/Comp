using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Comp.Infrastructure.Identity;

/// <summary>Signs the short-lived (15 minute) access token handed out alongside a refresh token.</summary>
public class JwtAccessTokenGenerator(IOptions<JwtOptions> options)
{
    public (string Token, DateTimeOffset ExpiresAt) Generate(AppUser user, IEnumerable<string> roles)
    {
        var opts = options.Value;
        var expiresAt = DateTimeOffset.UtcNow.Add(opts.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("display_name", user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        if (user.CanAmendPublished)
        {
            claims.Add(new Claim(AppUserClaimsPrincipalFactory.AmendPublishedClaimType, "true"));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
