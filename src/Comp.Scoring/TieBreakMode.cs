namespace Comp.Scoring;

/// <summary>
/// The only tie-break rule currently specified (architecture doc decision D3): compare
/// the shooter's other run, faster wins; two valid runs beats one; still equal, share the
/// position. Kept as an enum on <see cref="EventRules"/>, rather than assumed implicitly,
/// so a future second mode has somewhere to attach without changing the method signature.
/// </summary>
public enum TieBreakMode
{
    OtherRunTime
}
