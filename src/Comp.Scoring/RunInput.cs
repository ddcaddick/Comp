namespace Comp.Scoring;

/// <summary>The raw facts of one run, as recorded on the night. Times are integer milliseconds.</summary>
public readonly record struct RunInput(int RunNumber, int? RawTimeMs, int PenaltyCount, bool IsDnf);
