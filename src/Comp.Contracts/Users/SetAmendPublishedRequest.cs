namespace Comp.Contracts.Users;

/// <summary>Grants or revokes the amend-published privilege. A reason is mandatory either
/// way -- granting it is audited per the security model, and so is taking it away.</summary>
public record SetAmendPublishedRequest(bool Grant, string Reason);
