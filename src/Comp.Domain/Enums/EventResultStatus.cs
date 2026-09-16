namespace Comp.Domain.Enums;

/// <summary>Whether a participant produced a rankable time in the event.</summary>
public enum EventResultStatus
{
    Ranked,

    // Named in all caps deliberately -- ToString() on this value is what the API and every
    // client display directly (never "Dnf"), matching how DNF is written everywhere else in
    // the app (the results PDF, the mobile entry screen's DNF control). Reserved for a
    // participant who was actually marked DNF on the entry screen -- see NotRun below for
    // "hasn't been up yet".
    DNF,

    /// <summary>No runs recorded at all yet -- distinct from <see cref="DNF"/>, which means
    /// at least one run was actually marked DNF. Displayed as "Not Run".</summary>
    NotRun
}
