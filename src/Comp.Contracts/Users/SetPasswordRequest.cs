namespace Comp.Contracts.Users;

/// <summary>An admin setting a colleague's password directly -- there is no email
/// infrastructure in this app for a self-service reset link.</summary>
public record SetPasswordRequest(string Password);
