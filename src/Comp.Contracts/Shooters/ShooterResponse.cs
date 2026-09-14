namespace Comp.Contracts.Shooters;

public record ShooterResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? Nickname,
    string? MembershipNo,
    bool IsActive,
    DateTimeOffset CreatedAt);
