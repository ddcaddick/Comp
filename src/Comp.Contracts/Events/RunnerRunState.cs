namespace Comp.Contracts.Events;

public record RunnerRunState(int RunNumber, bool IsRecorded, int? RawTimeMs, int PenaltyCount, bool IsDnf);
