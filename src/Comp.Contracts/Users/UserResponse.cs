namespace Comp.Contracts.Users;

public record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    bool CanAmendPublished,
    bool IsLockedOut);
