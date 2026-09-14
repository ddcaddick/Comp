using Comp.Contracts.Auth;

namespace Comp.Application.Abstractions;

/// <summary>
/// Outcome of a login or refresh attempt. <see cref="Failure"/> carries a message that is
/// safe to return to the client as-is — callers must never distinguish "no such account"
/// from "wrong password" in what they show, to avoid user enumeration.
/// </summary>
public abstract record AuthResult
{
    private AuthResult() { }

    public sealed record Success(TokenResponse Tokens) : AuthResult;

    public sealed record Failure(string Reason) : AuthResult;
}

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
}
