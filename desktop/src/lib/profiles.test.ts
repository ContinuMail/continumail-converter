import { describe, it, expect } from "vitest";
import { visibleProfiles, hiddenProfiles, hiddenNote, profilePrimaryLabel, profileSubtext, pickDefaultProfile } from "./profiles";
import type { ProfileEntry } from "./types";

const p = (o: Partial<ProfileEntry>): ProfileEntry =>
  ({ name: "default", path: "/p/default", isDefault: false, accounts: [], convertible: true, ...o });

describe("labels", () => {
  it("one email", () => expect(profilePrimaryLabel(p({ accounts: ["a@x.com"] }))).toBe("a@x.com"));
  it("+N more", () => expect(profilePrimaryLabel(p({ accounts: ["a@x.com", "b@y.com"] }))).toBe("a@x.com +1 more"));
  it("falls back to raw name", () => expect(profilePrimaryLabel(p({ name: "work", accounts: [] }))).toBe("work"));
  it("subtext is name · path", () => expect(profileSubtext(p({ name: "work", path: "/p/w" }))).toBe("work · /p/w"));
});

describe("filter + note", () => {
  const list = [p({ name: "a", path: "/a", accounts: ["a@x.com"], convertible: true }),
                p({ name: "default-esr", path: "/esr", convertible: false })];
  it("visible = convertible only", () => expect(visibleProfiles(list).map((x) => x.name)).toEqual(["a"]));
  it("hidden = non-convertible", () => expect(hiddenProfiles(list).map((x) => x.name)).toEqual(["default-esr"]));
  it("note (singular) uses raw name, no path", () => expect(hiddenNote(list)).toBe("“default-esr” was hidden — no convertible mail found in it."));
  it("note null when none hidden", () => expect(hiddenNote([list[0]])).toBeNull());
  it("note (plural)", () => expect(hiddenNote([p({ name: "x", convertible: false }), p({ name: "y", convertible: false })]))
    .toBe("2 profiles were hidden — no convertible mail found."));
});

describe("pickDefaultProfile", () => {
  it("returns null (no change) when a profile is already selected", () =>
    expect(pickDefaultProfile([p({ path: "/a", convertible: true })], "/already")).toBeNull());
  it("single convertible -> its path", () =>
    expect(pickDefaultProfile([p({ path: "/a", convertible: true })], null)).toBe("/a"));
  it("first isDefault convertible wins", () =>
    expect(pickDefaultProfile([p({ path: "/a", convertible: true }), p({ path: "/b", isDefault: true, convertible: true })], null)).toBe("/b"));
  it("never picks a non-convertible default", () =>
    expect(pickDefaultProfile([p({ path: "/esr", isDefault: true, convertible: false }), p({ path: "/a", convertible: true })], null)).toBe("/a"));
  it("multiple convertible defaults -> first in order", () =>
    expect(pickDefaultProfile([p({ path: "/a", isDefault: true, convertible: true }), p({ path: "/b", isDefault: true, convertible: true })], null)).toBe("/a"));
});
