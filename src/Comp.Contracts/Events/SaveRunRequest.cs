namespace Comp.Contracts.Events;

public record SaveRunRequest(int? RawTimeMs, int PenaltyCount, bool IsDnf);
