import { describe, it, expect } from "vitest";
import { groupByAccount, sanitizePstName } from "./accounts";
import type { Account, ProfileSourceRow } from "./types";

const acct = (id: string, email: string | null, seg: string): Account =>
  ({ id, folderSegment: seg, accountPath: id, store: "ImapMail",
     email, host: seg, addressResolution: email ? "identity" : "not-found" });
const row = (id: string, accountId: string | null, tfp: string[], msgs: number, bytes: number): ProfileSourceRow =>
  ({ id, path: id, accountId, targetFolderPath: tfp, displayName: tfp.join(" / "),
     messages: msgs, bytes, sourceBytes: bytes * 2, msfPath: null } as any);

describe("groupByAccount", () => {
  it("groups rows by accountId and aggregates estimated bytes", () => {
    const groups = groupByAccount(
      [row("a", "A", ["imap.example.com", "Inbox"], 10, 100),
       row("b", "A", ["imap.example.com", "Sent"], 5, 50),
       row("c", "B", ["Office365", "Inbox"], 3, 30)],
      [acct("A", "alice@example.com", "imap.example.com"), acct("B", "alice@example.test", "Office365")]);
    expect(groups).toHaveLength(2);
    const a = groups.find((g) => g.key === "A")!;
    expect(a.folderCount).toBe(2);
    expect(a.messageCount).toBe(15);
    expect(a.estimatedBytes).toBe(150);          // sum of `bytes`, NOT sourceBytes
    expect(a.defaultPstName).toBe("alice@example.com");
  });

  it("defaults PST name to folderSegment when no email", () => {
    const groups = groupByAccount([row("a", "A", ["Local Folders", "Notes"], 1, 1)],
      [acct("A", null, "Local Folders")]);
    expect(groups[0].defaultPstName).toBe("Local Folders");
  });

  it("falls back to targetFolderPath[0] when accountId is null", () => {
    const groups = groupByAccount([row("a", null, ["imap.example.com", "Inbox"], 1, 1)], []);
    expect(groups[0].key).toBe("imap.example.com");
  });
});

describe("sanitizePstName", () => {
  it("strips illegal filename + control chars, keeps @ and .", () => {
    expect(sanitizePstName('a/b:c*?"<>|@x.com')).toBe("abc@x.com");
  });
  it("trims whitespace and leading/trailing periods", () => {
    expect(sanitizePstName("  ...Mail...  ")).toBe("Mail");
  });
  it("falls back to 'Account' when empty after sanitizing", () => {
    expect(sanitizePstName("...")).toBe("Account");
  });
  it("escapes reserved Windows device names", () => {
    expect(sanitizePstName("CON")).toBe("CON-mail");
    expect(sanitizePstName("LPT1.backup")).toBe("LPT1.backup-mail"); // stem LPT1 is reserved
  });
});
