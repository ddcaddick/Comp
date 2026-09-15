namespace Comp.Scoring;

/// <summary>Results in the same order as the participants passed to <see cref="EventScorer.Score"/>.</summary>
public readonly record struct EventScoringResult(IReadOnlyList<ParticipantScore> Participants);
