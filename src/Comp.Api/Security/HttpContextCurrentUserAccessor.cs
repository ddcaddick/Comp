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
            var user = httpContextAccessor.HttpContext?.User;
            // JwtAccessTokenGenerator issues a plain "sub" claim. Program.cs sets
            // MapInboundClaims = false so it arrives under that exact name rather than
            // being silently remapped to ClaimTypes.NameIdentifier depending on handler
            // defaults; NameIdentifier is checked too for any other auth scheme.
            var idClaim = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idClaim, out var id) ? id : null;
        }
    }
}
