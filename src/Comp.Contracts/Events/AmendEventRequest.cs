namespace Comp.Contracts.Events;

/// <summary>Unlocks a finalised event for correction. Requires the amend-published privilege.</summary>
public record AmendEventRequest(string Reason);
