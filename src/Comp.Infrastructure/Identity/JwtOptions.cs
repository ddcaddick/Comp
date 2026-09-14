namespace Comp.Infrastructure.Identity;

/// <summary>Bound from the "Jwt" configuration section — shared by token issuance here and
/// by the JWT bearer validation set up in Comp.Api's Program.cs.</summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(90);
}
