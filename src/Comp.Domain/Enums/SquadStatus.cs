namespace Comp.Domain.Enums;

/// <summary>
/// Whether a squad's roster is still being built or has been locked in. Set once, manually,
/// by whoever is running sign-on — nothing transitions this automatically. A shooter can only
/// be assigned into a <see cref="Pending"/> squad; an <see cref="Allocated"/> one is closed to
/// further additions until an official explicitly reopens it.
/// </summary>
public enum SquadStatus
{
    Pending,
    Allocated
}
