import { describe, expect, it } from "vitest";
import fc from "fast-check";
import { digitsToMillis, formatMillis, parseTime } from "./time";

function pad2(value: number): string {
  return value.toString().padStart(2, "0");
}

describe("formatMillis", () => {
  it("formats zero", () => {
    expect(formatMillis(0)).toBe("0:00.00");
  });

  it("matches the architecture doc's own worked example (24256 -> 2:42.56)", () => {
    expect(formatMillis(162_560)).toBe("2:42.56");
  });

  it("rolls seconds into minutes exactly at the 60-second boundary", () => {
    expect(formatMillis(59_990)).toBe("0:59.99");
    expect(formatMillis(60_000)).toBe("1:00.00");
  });

  it("does not roll minutes into hours — there is no hours component", () => {
    expect(formatMillis(3_599_990)).toBe("59:59.99");
    expect(formatMillis(3_600_000)).toBe("60:00.00");
  });

  it("rounds to the nearest centisecond when ms isn't an exact multiple of 10", () => {
    expect(formatMillis(4)).toBe("0:00.00");
    expect(formatMillis(5)).toBe("0:00.01");
    expect(formatMillis(14)).toBe("0:00.01");
  });

  it("rejects non-integers and negative numbers", () => {
    expect(() => formatMillis(-1)).toThrow(RangeError);
    expect(() => formatMillis(1.5)).toThrow(RangeError);
    expect(() => formatMillis(Number.NaN)).toThrow(RangeError);
  });
});

describe("parseTime", () => {
  it("parses the architecture doc's own worked example", () => {
    expect(parseTime("2:42.56")).toBe(162_560);
  });

  it("parses the boundaries", () => {
    expect(parseTime("0:00.00")).toBe(0);
    expect(parseTime("59:59.99")).toBe(3_599_990);
  });

  it("accepts a leading zero on minutes, unlike what formatMillis produces", () => {
    expect(parseTime("02:42.56")).toBe(162_560);
  });

  it("trims surrounding whitespace", () => {
    expect(parseTime("  2:42.56  ")).toBe(162_560);
  });

  it.each([
    "2:60.00", // seconds out of range
    "2:5.00", // seconds must be exactly two digits
    "2:42.5", // centiseconds must be exactly two digits
    "2:42.567", // too many centisecond digits
    "-1:00.00", // no sign allowed
    "2:42",
    "abc",
    "",
  ])("returns null, never throws, for %j", (text) => {
    expect(parseTime(text)).toBeNull();
  });
});

describe("digitsToMillis", () => {
  it("matches the architecture doc's own worked example (24256 -> 2:42.56)", () => {
    expect(digitsToMillis("24256")).toBe(162_560);
  });

  it("treats an empty or non-digit input as zero", () => {
    expect(digitsToMillis("")).toBe(0);
    expect(digitsToMillis("abc")).toBe(0);
  });

  it("fills right to left as more digits are typed, one at a time", () => {
    expect(digitsToMillis("2")).toBe(20); // 0:00.02
    expect(digitsToMillis("02")).toBe(20); // same — a leading zero changes nothing
    expect(digitsToMillis("24")).toBe(240); // 0:00.24
    expect(digitsToMillis("242")).toBe(2_420); // 0:02.42
    expect(digitsToMillis("2425")).toBe(24_250); // 0:24.25
    expect(digitsToMillis("24256")).toBe(162_560); // 2:42.56
  });

  it("ignores non-digit characters wherever they appear", () => {
    expect(digitsToMillis("2a4b2c5d6")).toBe(digitsToMillis("24256"));
  });
});

describe("property-based: formatMillis and parseTime are inverses", () => {
  it("round-trips any exact time built from valid minutes/seconds/centiseconds", () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 0, max: 999 }),
        fc.integer({ min: 0, max: 59 }),
        fc.integer({ min: 0, max: 99 }),
        (minutes, seconds, centiseconds) => {
          const ms = (minutes * 60 + seconds) * 1000 + centiseconds * 10;

          expect(formatMillis(ms)).toBe(`${minutes}:${pad2(seconds)}.${pad2(centiseconds)}`);
          expect(parseTime(formatMillis(ms))).toBe(ms);
        },
      ),
    );
  });

  it("rounds any non-negative integer ms to the nearest exact centisecond", () => {
    fc.assert(
      fc.property(fc.integer({ min: 0, max: 60 * 60_000 }), (ms) => {
        const roundTripped = parseTime(formatMillis(ms));
        expect(roundTripped).toBe(Math.round(ms / 10) * 10);
      }),
    );
  });

  it("never produces a string formatMillis/parseTime disagree on the shape of", () => {
    fc.assert(
      fc.property(fc.integer({ min: 0, max: 60 * 60_000 }), (ms) => {
        expect(formatMillis(ms)).toMatch(/^\d+:[0-5]\d\.\d{2}$/);
      }),
    );
  });
});

describe("property-based: digitsToMillis inverts formatMillis's own digits", () => {
  it("recovers the exact ms from the digits of its own formatted string", () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 0, max: 999 }),
        fc.integer({ min: 0, max: 59 }),
        fc.integer({ min: 0, max: 99 }),
        (minutes, seconds, centiseconds) => {
          const ms = (minutes * 60 + seconds) * 1000 + centiseconds * 10;
          const digits = formatMillis(ms).replace(/\D/g, "");

          expect(digitsToMillis(digits)).toBe(ms);
        },
      ),
    );
  });

  it("never throws and never returns a negative or non-integer value for any input", () => {
    fc.assert(
      fc.property(fc.string(), (input) => {
        const result = digitsToMillis(input);
        expect(Number.isInteger(result)).toBe(true);
        expect(result).toBeGreaterThanOrEqual(0);
      }),
    );
  });
});
