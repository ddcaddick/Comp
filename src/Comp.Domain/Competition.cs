using Comp.Domain.Enums;

namespace Comp.Domain;

/// <summary>
/// The annual top-level container. Owns leagues, events and memberships. Closing it
/// freezes standings and enables promotion and relegation into the following year's
/// competition. There is no separate <c>Season</c> entity — the competition is annual
/// and carries the year itself, so a second competition in the same year needs no
/// schema change.
/// </summary>
public class Competition
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required int Year { get; set; }
    public required DateOnly StartsOn { get; set; }
    public required DateOnly EndsOn { get; set; }
    public CompetitionStatus Status { get; set; } = CompetitionStatus.Planning;

    /// <summary>
    /// Null until multi-club support arrives. Never assume in code that only one
    /// organisation exists.
    /// </summary>
    public Guid? OrganisationId { get; set; }
}
