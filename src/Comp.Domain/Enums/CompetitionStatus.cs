namespace Comp.Domain.Enums;

/// <summary>
/// Lifecycle of the annual competition. Closing freezes standings and enables promotion
/// and relegation into the following year's competition.
/// </summary>
public enum CompetitionStatus
{
    Planning,
    Active,
    Closed
}
