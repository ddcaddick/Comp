namespace Comp.Domain;

/// <summary>
/// One division within one competition, e.g. "Division A 2027". Carries its own scoring
/// configuration so leagues in the same competition can score differently. <see cref="Tier"/>
/// orders the divisions and drives promotion and relegation between competitions.
/// </summary>
public class League
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid CompetitionId { get; init; }
    public required string Name { get; set; }
    public required int Tier { get; set; }
    public int PointsForFirst { get; set; } = 50;
    public int PointsDecrement { get; set; } = 1;
    public int DropWorstCount { get; set; } = 0;
    public bool AbsencesCountAsZero { get; set; } = true;
}
