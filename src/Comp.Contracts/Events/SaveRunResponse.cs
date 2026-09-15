namespace Comp.Contracts.Events;

/// <summary>
/// Keeps live entry to one round trip per shooter: the app never has to ask separately
/// who comes next (architecture doc section G).
/// </summary>
public record SaveRunResponse(RunResponse SavedRun, RunnerParticipantResponse? NextOutstanding);
