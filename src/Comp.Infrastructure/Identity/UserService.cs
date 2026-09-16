using System.Text.Json;
using Comp.Application.Abstractions;
using Comp.Contracts.Users;
using Comp.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Identity;

public class UserService(CompDbContext dbContext, UserManager<AppUser> userManager, ICurrentUserAccessor currentUser) : IUserService
{
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken)
    {
        // Flat lists joined in memory rather than a per-user role lookup -- the established
        // pattern in this codebase (see ResultsService's own doc comment) once a role isn't
        // a plain column but lives in Identity's join tables.
        var users = await dbContext.Users.OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        var roleByUserId = await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            select new { userRole.UserId, role.Name }
        ).ToDictionaryAsync(x => x.UserId, x => x.Name!, cancellationToken);

        return users.Select(u => ToResponse(u, roleByUserId.GetValueOrDefault(u.Id, ""))).ToList();
    }

    public async Task<UserResult> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!Roles.All.Contains(request.Role))
        {
            return new UserResult.Conflict($"'{request.Role}' is not a valid role.");
        }

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName,
            IsActive = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return new UserResult.Conflict(DescribeErrors(createResult));
        }

        RecordRoleChangeAudit(user.Id, before: null, after: request.Role);
        await userManager.AddToRoleAsync(user, request.Role);

        return new UserResult.Success(ToResponse(user, request.Role));
    }

    public async Task<UserResult> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        if (!Roles.All.Contains(request.Role))
        {
            return new UserResult.Conflict($"'{request.Role}' is not a valid role.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentRole = currentRoles.SingleOrDefault() ?? "";

        if (id == RequireActorId() && request.Role != currentRole)
        {
            return new UserResult.Conflict("You cannot change your own role.");
        }

        user.DisplayName = request.DisplayName;

        if (request.Role != currentRole)
        {
            RecordRoleChangeAudit(user.Id, before: currentRole, after: request.Role);
            if (currentRoles.Count > 0)
            {
                await userManager.RemoveFromRolesAsync(user, currentRoles);
            }
            await userManager.AddToRoleAsync(user, request.Role);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UserResult.Success(ToResponse(user, request.Role));
    }

    public Task<UserResult> DeactivateAsync(Guid id, CancellationToken cancellationToken) =>
        id == RequireActorId()
            ? Task.FromResult<UserResult>(new UserResult.Conflict("You cannot deactivate your own account."))
            : SetActiveAsync(id, isActive: false, cancellationToken);

    public Task<UserResult> ReactivateAsync(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: true, cancellationToken);

    public async Task<UserResult> ResetPasswordAsync(Guid id, SetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
        {
            return new UserResult.Conflict(DescribeErrors(result));
        }

        var role = (await userManager.GetRolesAsync(user)).SingleOrDefault() ?? "";
        return new UserResult.Success(ToResponse(user, role));
    }

    public async Task<UserResult> UnlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);
        }

        var role = (await userManager.GetRolesAsync(user)).SingleOrDefault() ?? "";
        return new UserResult.Success(ToResponse(user, role));
    }

    public async Task<UserResult> SetAmendPublishedAsync(Guid id, SetAmendPublishedRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        if (user.CanAmendPublished != request.Grant)
        {
            user.CanAmendPublished = request.Grant;
            dbContext.PendingAuditReason = request.Reason;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var role = (await userManager.GetRolesAsync(user)).SingleOrDefault() ?? "";
        return new UserResult.Success(ToResponse(user, role));
    }

    private async Task<UserResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        // Idempotent: reactivating an already-active user (or vice versa) is a no-op, not
        // an error, and shouldn't add a no-op row to the audit trail.
        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var role = (await userManager.GetRolesAsync(user)).SingleOrDefault() ?? "";
        return new UserResult.Success(ToResponse(user, role));
    }

    // Role assignment lives in Identity's join tables, which the audit interceptor
    // deliberately excludes (see AuditSaveChangesInterceptor's comment) since there's no
    // single row there to attribute a before/after diff to -- so a role change is audited
    // explicitly here instead, at the one call site that ever changes one. AuditLog itself
    // is also excluded from the interceptor, so adding this row directly doesn't loop back
    // through it a second time.
    private void RecordRoleChangeAudit(Guid userId, string? before, string after)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(AppUser),
            EntityId = userId,
            Action = "Updated",
            ActorUserId = RequireActorId(),
            Before = before is null ? null : JsonSerializer.Serialize(new { Role = before }),
            After = JsonSerializer.Serialize(new { Role = after })
        });
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");

    private static string DescribeErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private static UserResponse ToResponse(AppUser user, string role) => new(
        user.Id,
        user.Email!,
        user.DisplayName,
        role,
        user.IsActive,
        user.CanAmendPublished,
        user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow);
}
