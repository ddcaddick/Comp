namespace Comp.Scoring;

/// <summary>
/// One shooter's entry into the event. <see cref="LeagueId"/> is the snapshot taken at
/// entry time, not a live lookup — null only if the participant was never assigned to a
/// league, in which case they still appear in the overall table but never a league one.
/// </summary>
public readonly record struct ParticipantInput(Guid ParticipantId, Guid? LeagueId, IReadOnlyList<RunInput> Runs);
