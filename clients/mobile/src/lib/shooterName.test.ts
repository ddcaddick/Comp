import { describe, expect, it } from "vitest";
import { preferredName, preferredNameSortKey } from "./shooterName";

describe("preferredNameSortKey", () => {
  it("sorts shooters by whatever preferredName actually displays, not always by surname", () => {
    // The exact worked example from the user's request: nicknamed shooters sort by
    // nickname, un-nicknamed ones by surname, all interleaved into one alphabetical list.
    const shooters = [
      { firstName: "Thomas", lastName: "Bridgewater", nickname: "Pierre" },
      { firstName: "Paul", lastName: "Hunt", nickname: null },
      { firstName: "Wayne", lastName: "Carless", nickname: null },
      { firstName: "Steve", lastName: "Bridgewater", nickname: "Brie" },
      { firstName: "Daniel", lastName: "Caddick", nickname: "Ba-Nana" },
      { firstName: "Aron", lastName: "Chatwin", nickname: "Azza" },
    ];

    const sorted = [...shooters].sort((a, b) => preferredNameSortKey(a).localeCompare(preferredNameSortKey(b)));

    expect(sorted.map(preferredName)).toEqual(["Azza", "Ba-Nana", "Brie", "Wayne Carless", "Paul Hunt", "Pierre"]);
  });
});
