import type { components } from "@comp/api-types";

type RunnerParticipant = components["schemas"]["RunnerParticipantResponse"];

export interface OutstandingEntry {
  participant: RunnerParticipant;
  runNumber: number;
}

/**
 * Finds who the entry screen should default to before any run has been saved this
 * session: the earliest run number that isn't recorded for every participant yet, and
 * within that, the first participant still missing it, in the squad position order the
 * API already returns them in. This mirrors the server's own round-robin rule
 * (RunService.FindNextOutstandingAsync) for the one case that rule needs a "just saved"
 * participant to pivot from and there isn't one yet — after any save, use the server's
 * own `nextOutstanding` from the save response instead of recomputing this client-side.
 */
export function findInitialOutstanding(participants: readonly RunnerParticipant[]): OutstandingEntry | null {
  const runsPerShooter = participants[0]?.runs.length ?? 0;

  for (let runNumber = 1; runNumber <= runsPerShooter; runNumber++) {
    const participant = participants.find((p) => p.runs[runNumber - 1]?.isRecorded === false);
    if (participant) {
      return { participant, runNumber };
    }
  }

  return null;
}
