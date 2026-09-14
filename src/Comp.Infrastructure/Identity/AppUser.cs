using Microsoft.AspNetCore.Identity;

namespace Comp.Infrastructure.Identity;

/// <summary>
/// Extends ASP.NET Core Identity's user with what the security model needs beyond login: a
/// display name for audit trails and result screens, the amend-published grant, and whether
/// the account is still usable. Domain entities never reference this type — they hold a
/// plain <see cref="Guid"/> user id, per <c>Comp.Domain</c>'s "references nothing" rule.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public required string DisplayName { get; set; }

    /// <summary>
    /// Grants the privilege to amend a finalised, published result. A per-user claim, not a
    /// role — only a super admin grants it, and granting it is itself audited. Read into the
    /// sign-in claims by <see cref="AppUserClaimsPrincipalFactory"/>.
    /// </summary>
    public bool CanAmendPublished { get; set; }

    /// <summary>Deactivated accounts keep their history but can no longer sign in.</summary>
    public bool IsActive { get; set; } = true;
}
