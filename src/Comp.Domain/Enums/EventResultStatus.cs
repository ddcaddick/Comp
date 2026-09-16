namespace Comp.Domain.Enums;

/// <summary>Whether a participant produced a rankable time in the event.</summary>
public enum EventResultStatus
{
    Ranked,

    // Named in all caps deliberately -- ToString() on this value is what the API and every
    // client display directly (never "Dnf"), matching how DNF is written everywhere else in
    // the app (the results PDF, the mobile entry screen's DNF control).
    DNF
}
