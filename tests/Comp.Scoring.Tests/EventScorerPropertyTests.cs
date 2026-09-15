using FsCheck;
using FsCheck.Xunit;

namespace Comp.Scoring.Tests;

public class EventScorerPropertyTests
{
    private static readonly EventRules Rules = new(Version: 1, PenaltySeconds: 5.00m, TieBreak: TieBreakMode.OtherRunTime);

    [Property]
    public bool Event_time_is_never_slower_than_the_best_adjusted_run(
        NonNegativeInt rawTimeMs1, NonNegativeInt penaltyCount1,
        NonNegativeInt rawTimeMs2, NonNegativeInt penaltyCount2)
    {
        var run1 = new RunInput(1, rawTimeMs1.Get % 600_000, penaltyCount1.Get % 21, false);
        var run2 = new RunInput(2, rawTimeMs2.Get % 600_000, penaltyCount2.Get % 21, false);
        var participant = new ParticipantInput(Guid.NewGuid(), null, [run1, run2]);

        var score = EventScorer.Score([participant], Rules, new Dictionary<Guid, LeagueRules>()).Participants[0];

        var adjusted1 = run1.RawTimeMs!.Value + (int)(run1.PenaltyCount * Rules.PenaltySeconds * 1000m);
        var adjusted2 = run2.RawTimeMs!.Value + (int)(run2.PenaltyCount * Rules.PenaltySeconds * 1000m);

        return score.EventTimeMs <= Math.Min(adjusted1, adjusted2);
    }

    [Property]
    public bool Adding_a_penalty_never_improves_a_position(NonNegativeInt rawTimeMsB, NonNegativeInt penaltyCountB)
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };

        var a = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, 100_000, 0, false)]);

        var bId = Guid.NewGuid();
        var bRawTime = rawTimeMsB.Get % 600_000;
        var bPenalty = penaltyCountB.Get % 15;
        var bLow = new ParticipantInput(bId, leagueId, [new RunInput(1, bRawTime, bPenalty, false)]);
        var bHigh = new ParticipantInput(bId, leagueId, [new RunInput(1, bRawTime, bPenalty + 1, false)]);

        var positionLow = EventScorer.Score([a, bLow], Rules, leagueRules)
            .Participants.Single(p => p.ParticipantId == bId).LeaguePosition!.Value;
        var positionHigh = EventScorer.Score([a, bHigh], Rules, leagueRules)
            .Participants.Single(p => p.ParticipantId == bId).LeaguePosition!.Value;

        // A larger position number is worse; adding a penalty can only match or worsen it.
        return positionHigh >= positionLow;
    }
}
