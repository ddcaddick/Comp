namespace Comp.Contracts.Leagues;

public record LeagueMemberResponse(Guid ShooterId, string FirstName, string LastName, DateTimeOffset AssignedAt);
