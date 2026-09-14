namespace Comp.Application.Abstractions;

/// <summary>
/// The user attributed to the current unit of work. The audit interceptor depends on this
/// rather than on ASP.NET Core's <c>ClaimsPrincipal</c> directly, so <c>Comp.Infrastructure</c>
/// stays free of a web framework dependency and the interceptor is trivial to test.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Null when there is no authenticated user for the current operation.</summary>
    Guid? UserId { get; }
}
