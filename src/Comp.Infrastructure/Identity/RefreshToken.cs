namespace Comp.Infrastructure.Identity;

/// <summary>
/// One row per issued refresh token, hashed at rest — never the raw token. Rotating:
/// presenting a token issues a new one and marks this one revoked with
/// <see cref="ReplacedByTokenId"/> pointing at it, so reuse of an already-rotated token is
/// detectable and can revoke the whole chain.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
