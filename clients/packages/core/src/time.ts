/**
 * Times are always integer milliseconds — never floating point anywhere near a result
 * (see the project's non-negotiable rules). This is the only place time parsing and
 * formatting exists; nowhere else, client or server, should reimplement it.
 *
 * Display and entry format is `m:ss.cc` (architecture doc decision D2): minutes with no
 * zero-padding and no upper bound, a colon, two-digit zero-padded seconds, a dot, and
 * two-digit zero-padded centiseconds (hundredths of a second). E.g. 162560ms formats as
 * "2:42.56".
 */

const TIME_STRING_PATTERN = /^(\d+):([0-5]\d)\.(\d{2})$/;

/**
 * Formats a non-negative integer millisecond count as "m:ss.cc". Throws if `ms` is not a
 * non-negative integer — this takes a value the caller already knows is a valid time
 * (e.g. from the database), not untrusted input; use {@link parseTime} for that.
 */
export function formatMillis(ms: number): string {
  if (!Number.isInteger(ms) || ms < 0) {
    throw new RangeError(`formatMillis expects a non-negative integer number of milliseconds, got ${ms}`);
  }

  // Rounds to the nearest centisecond: display precision is hundredths of a second, so a
  // millisecond count that isn't an exact multiple of 10 (unusual, but not impossible
  // depending on where a value originated) still formats sensibly.
  const totalCentiseconds = Math.round(ms / 10);
  const centiseconds = totalCentiseconds % 100;
  const totalSeconds = Math.floor(totalCentiseconds / 100);
  const seconds = totalSeconds % 60;
  const minutes = Math.floor(totalSeconds / 60);

  return `${minutes}:${pad2(seconds)}.${pad2(centiseconds)}`;
}

/**
 * Parses a "m:ss.cc" string into an integer millisecond count. Returns null — never
 * throws — for anything not in that exact shape: seconds must be exactly two digits from
 * 00-59, centiseconds exactly two digits from 00-99. Leading/trailing whitespace is
 * ignored; a leading zero on minutes (e.g. "02:42.56") is accepted even though
 * {@link formatMillis} never produces one, since this is the boundary for untrusted
 * input (typed or pasted), not just round-tripping our own output.
 */
export function parseTime(text: string): number | null {
  const match = TIME_STRING_PATTERN.exec(text.trim());
  if (!match) {
    return null;
  }

  const [, minutesText, secondsText, centisecondsText] = match;
  const minutes = Number(minutesText);
  const seconds = Number(secondsText);
  const centiseconds = Number(centisecondsText);

  return (minutes * 60 + seconds) * 1000 + centiseconds * 10;
}

/**
 * Converts the raw digit string typed on the custom numeric keypad (see the mobile
 * strategy section of the architecture doc) into an integer millisecond count, filling
 * right to left into the m:ss.cc template — so typing "24256" produces the same result
 * as parsing "2:42.56". Non-digit characters are stripped first; an empty (or
 * all-non-digit) input is zero. There is no upper bound on how many digits form the
 * minutes portion.
 */
export function digitsToMillis(digits: string): number {
  const clean = digits.replace(/\D/g, "");
  if (clean === "") {
    return 0;
  }

  const padded = clean.padStart(4, "0");
  const centiseconds = Number(padded.slice(-2));
  const rest = padded.slice(0, -2);
  const seconds = Number(rest.slice(-2));
  const minutes = Number(rest.slice(0, -2) || "0");

  return (minutes * 60 + seconds) * 1000 + centiseconds * 10;
}

function pad2(value: number): string {
  return value.toString().padStart(2, "0");
}
