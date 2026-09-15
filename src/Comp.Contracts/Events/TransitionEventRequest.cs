namespace Comp.Contracts.Events;

/// <summary>One of the <c>EventStatus</c> names, e.g. "Setup" or "InProgress".</summary>
public record TransitionEventRequest(string To);
