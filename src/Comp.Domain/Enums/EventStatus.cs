namespace Comp.Domain.Enums;

/// <summary>
/// Lifecycle of one weekly event. <see cref="Finalised"/> is locked: the service layer
/// refuses writes to its runs, and a database trigger backs that guarantee.
/// </summary>
public enum EventStatus
{
    Draft,
    Setup,
    InProgress,
    Review,
    Finalised
}
