namespace Comp.Scoring;

/// <summary>
/// The event-night rules snapshot. <see cref="Version"/> is <c>ScoringRulesVersion</c> —
/// carried through so a caller can record which rules produced a result, not consulted by
/// this library itself (there is only one algorithm today; a future shape change adds a
/// new version rather than branching internally on this one).
/// </summary>
public readonly record struct EventRules(int Version, decimal PenaltySeconds, TieBreakMode TieBreak);
