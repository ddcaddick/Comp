namespace Comp.Scoring;

/// <summary>Whether a participant produced a rankable time in the event.</summary>
public enum ParticipantScoringStatus
{
    Ranked,

    /// <summary>At least one run was explicitly recorded as DNF and none produced a
    /// valid time -- distinct from <see cref="NotRun"/>, which is nothing recorded at
    /// all yet.</summary>
    Dnf,

    /// <summary>No runs recorded for this participant at all -- they simply haven't
    /// been up yet, not the same as having actually DNF'd.</summary>
    NotRun
}
