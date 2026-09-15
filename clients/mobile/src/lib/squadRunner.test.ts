import { describe, expect, it } from "vitest";
import type { components } from "@comp/api-types";
import { findInitialOutstanding } from "./squadRunner";

type RunnerParticipant = components["schemas"]["RunnerParticipantResponse"];

function participant(
  id: string,
  positionInSquad: number,
  runStates: readonly [boolean, boolean],
): RunnerParticipant {
  return {
    participantId: id,
    shooterId: id,
    firstName: id,
    lastName: "Shooter",
    positionInSquad,
    runs: runStates.map((isRecorded, index) => ({
      runNumber: index + 1,
      isRecorded,
      rawTimeMs: isRecorded ? 100_000 : null,
      penaltyCount: 0,
      isDnf: false,
    })),
  };
}

describe("findInitialOutstanding", () => {
  it("returns null for an empty squad", () => {
    expect(findInitialOutstanding([])).toBeNull();
  });

  it("picks the first participant missing run 1 when nothing is recorded yet", () => {
    const squad = [participant("a", 1, [false, false]), participant("b", 2, [false, false])];

    expect(findInitialOutstanding(squad)).toEqual({ participant: squad[0], runNumber: 1 });
  });

  it("skips a participant whose run 1 is already recorded", () => {
    const squad = [participant("a", 1, [true, false]), participant("b", 2, [false, false])];

    expect(findInitialOutstanding(squad)).toEqual({ participant: squad[1], runNumber: 1 });
  });

  it("only advances to run 2 once every participant has a run 1", () => {
    const squad = [participant("a", 1, [true, false]), participant("b", 2, [true, false])];

    expect(findInitialOutstanding(squad)).toEqual({ participant: squad[0], runNumber: 2 });
  });

  it("returns null once every run for every participant is recorded", () => {
    const squad = [participant("a", 1, [true, true]), participant("b", 2, [true, true])];

    expect(findInitialOutstanding(squad)).toBeNull();
  });
});
