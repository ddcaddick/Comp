using Comp.Contracts.Users;

namespace Comp.Application.Abstractions;

public abstract record UserResult
{
    private UserResult() { }

    public sealed record Success(UserResponse User) : UserResult;

    public sealed record NotFound : UserResult;

    public sealed record Conflict(string Reason) : UserResult;
}

/// <summary>
/// "Manage users" is Super Admin only per the security model's table -- stricter than
/// every other admin-CRUD in this app, which is usually Super Admin + Admin. Every
/// endpoint calling into this service is gated accordingly; this service does not
/// re-check it itself, matching the pattern the rest of the codebase already follows
/// (route-level RequireAuthorization is the enforcement, services trust the caller).
/// </summary>
public interface IUserService
{
    Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken);

    Task<UserResult> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    /// <summary>Updates display name and role together. Refuses to change the caller's own
    /// role (but not their own display name) so a Super Admin can never demote themselves
    /// with no one left to undo it.</summary>
    Task<UserResult> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);

    /// <summary>Refuses to deactivate the caller's own account, for the same reason.</summary>
    Task<UserResult> DeactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<UserResult> ReactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<UserResult> ResetPasswordAsync(Guid id, SetPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Clears an active lockout early. Idempotent -- unlocking an account that
    /// isn't locked out is a no-op success.</summary>
    Task<UserResult> UnlockAsync(Guid id, CancellationToken cancellationToken);

    Task<UserResult> SetAmendPublishedAsync(Guid id, SetAmendPublishedRequest request, CancellationToken cancellationToken);
}
