namespace Comp.Contracts.Users;

public record CreateUserRequest(string Email, string DisplayName, string Role, string Password);
