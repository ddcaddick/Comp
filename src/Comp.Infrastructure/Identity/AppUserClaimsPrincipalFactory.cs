using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Comp.Infrastructure.Identity;

/// <summary>
/// Adds the amend-published grant to the sign-in claims as a plain claim, so a
/// <c>RequireClaim</c> authorisation policy stays declarative on the endpoint rather than
/// each one re-querying <see cref="AppUser.CanAmendPublished"/> itself.
/// </summary>
public class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    public const string AmendPublishedClaimType = "amend_published";

    public override async Task<ClaimsPrincipal> CreateAsync(AppUser user)
    {
        var principal = await base.CreateAsync(user);

        if (user.CanAmendPublished)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(AmendPublishedClaimType, "true"));
        }

        return principal;
    }
}
