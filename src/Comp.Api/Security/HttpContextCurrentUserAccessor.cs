using System.Security.Claims;
using Comp.Application.Abstractions;

namespace Comp.Api.Security;

/// <summary>Reads the current user's id from the request's <see cref="ClaimsPrincipal"/>.</summary>
public class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public Guid? UserId
    {
        get
        {
            var idClaim = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idClaim, out var id) ? id : null;
        }
    }
}
