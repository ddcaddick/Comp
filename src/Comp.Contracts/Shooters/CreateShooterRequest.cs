namespace Comp.Contracts.Shooters;

public record CreateShooterRequest(string FirstName, string LastName, string? Nickname, string? MembershipNo);
