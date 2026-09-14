using Comp.Application.Abstractions;
using Comp.Contracts.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Comp.Infrastructure.Identity;

public class AuthService(
    UserManager<AppUser> userManager,
    CompDbContext dbContext,
    IOptions<JwtOptions> jwtOptions,
    JwtAccessTokenGenerator accessTokenGenerator) : IAuthService
{
    // Deliberately identical for "no such account", "account deactivated" and "wrong
    // password" — distinguishing them would let a caller enumerate valid email addresses.
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            return new AuthResult.Failure(InvalidCredentialsMessage);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return new AuthResult.Failure("This account is temporarily locked. Try again later.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // An anonymous request has no ClaimsPrincipal yet, but the actor for the
            // account's own lockout counters is unambiguous: the account itself.
            dbContext.PendingActorOverride = user.Id;
            await userManager.AccessFailedAsync(user);
            return new AuthResult.Failure(InvalidCredentialsMessage);
        }

        if (user.AccessFailedCount > 0)
        {
            dbContext.PendingActorOverride = user.Id;
            await userManager.ResetAccessFailedCountAsync(user);
        }

        var (result, _) = await IssueTokensAsync(user, cancellationToken);
        return result;
    }

    public async Task<AuthResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenGenerator.ComputeHash(request.RefreshToken);
        var existing = await dbContext.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return new AuthResult.Failure("Invalid refresh token.");
        }

        if (existing.RevokedAt is not null)
        {
            // This token was already rotated once; someone is presenting a used token,
            // which means it (or a token issued after it) has leaked. Revoke the whole
            // chain for this user rather than trust anything currently active.
            var activeTokens = await dbContext.RefreshTokens
                .Where(t => t.UserId == existing.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in activeTokens)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthResult.Failure("Refresh token has already been used.");
        }

        if (existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return new AuthResult.Failure("Refresh token has expired.");
        }

        var user = await userManager.FindByIdAsync(existing.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            return new AuthResult.Failure("Account is no longer active.");
        }

        var (result, newToken) = await IssueTokensAsync(user, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newToken.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<(AuthResult.Success Result, RefreshToken Token)> IssueTokensAsync(
        AppUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, accessExpiresAt) = accessTokenGenerator.Generate(user, roles);

        var (rawRefreshToken, refreshHash) = RefreshTokenGenerator.Generate();
        var refreshExpiresAt = DateTimeOffset.UtcNow.Add(jwtOptions.Value.RefreshTokenLifetime);

        var tokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt
        };
        dbContext.RefreshTokens.Add(tokenEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new TokenResponse(accessToken, accessExpiresAt, rawRefreshToken, refreshExpiresAt);
        return (new AuthResult.Success(response), tokenEntity);
    }
}
