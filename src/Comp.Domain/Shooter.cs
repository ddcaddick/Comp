namespace Comp.Domain;

/// <summary>
/// The permanent identity of a shooter, on a surrogate GUID so renaming or correcting
/// details never touches historical results. Never hard-deleted, only deactivated —
/// deactivation must remain visible in past results, so nothing in this layer filters
/// on <see cref="IsActive"/> automatically.
/// </summary>
public class Shooter
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Nickname { get; set; }
    public string? MembershipNo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required Guid CreatedByUserId { get; init; }
}
