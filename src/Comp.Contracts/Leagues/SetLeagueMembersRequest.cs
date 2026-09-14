namespace Comp.Contracts.Leagues;

/// <summary>Full-replace semantics: the league's roster becomes exactly this list.</summary>
public record SetLeagueMembersRequest(IReadOnlyList<Guid> ShooterIds);
