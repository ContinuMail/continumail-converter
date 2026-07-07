// Screenshot harness — curated demo data (fictional, PII-free).
// Shapes match src/lib/types.ts exactly.
import type {
  DiscoverResult, ProfileEntry, FileStat, Account,
  DiscoveredSource, DiscoveredCalendar, DiscoveredAddressBook,
} from "../src/lib/types";
import type { ScanResult } from "../src/lib/parse";

const PROFILE_ROOT = "C:\\Users\\Owner\\AppData\\Roaming\\Thunderbird\\Profiles\\x7k2p9qr.default-release";
const MAIL = `${PROFILE_ROOT}\\Mail`;
const IMAP = `${PROFILE_ROOT}\\ImapMail`;

export const demoProfiles: ProfileEntry[] = [
  {
    name: "default-release",
    path: PROFILE_ROOT,
    isDefault: true,
    accounts: ["continumail@gmail.com", "contact@continumail.com"],
    convertible: true,
  },
];

export const demoOutputDir = "C:\\Users\\Owner\\Documents\\ContinuMail";
export const demoOutputPst = "C:\\Users\\Owner\\Documents\\ContinuMail\\Archive.pst";

const accounts: Account[] = [
  {
    id: "account1",
    folderSegment: "continumail@gmail.com",
    accountPath: `${IMAP}\\imap.gmail.com`,
    store: "imap",
    email: "continumail@gmail.com",
    host: "imap.gmail.com",
    addressResolution: "identity",
  },
  {
    id: "account2",
    folderSegment: "contact@continumail.com",
    accountPath: `${IMAP}\\mail.continumail.com`,
    store: "imap",
    email: "contact@continumail.com",
    host: "mail.continumail.com",
    addressResolution: "identity",
  },
  {
    id: "localFolders",
    folderSegment: "Local Folders",
    accountPath: `${MAIL}\\Local Folders`,
    store: "none",
    email: null,
    host: null,
    addressResolution: "local-folders",
  },
];

// path, folder path, display, bytes, msf?, account, messages, from, to
type Row = [string, string[], string, number, boolean, string | null, number, string | null, string | null];
const rows: Row[] = [
  [`${IMAP}\\imap.gmail.com\\INBOX`, ["continumail@gmail.com", "Inbox"], "Inbox", 1_863_224_320, true, "account1", 14_382, "2011-03-14T09:12:00Z", "2026-07-05T16:41:00Z"],
  [`${IMAP}\\imap.gmail.com\\[Gmail].sbd\\Sent Mail`, ["continumail@gmail.com", "Sent Mail"], "Sent Mail", 512_882_688, true, "account1", 6_931, "2011-03-14T09:30:00Z", "2026-07-04T11:02:00Z"],
  [`${IMAP}\\imap.gmail.com\\[Gmail].sbd\\All Mail`, ["continumail@gmail.com", "Archive"], "Archive", 3_247_439_872, true, "account1", 27_554, "2009-08-02T07:55:00Z", "2026-07-05T16:41:00Z"],
  [`${IMAP}\\imap.gmail.com\\Receipts`, ["continumail@gmail.com", "Receipts"], "Receipts", 88_604_672, true, "account1", 1_206, "2013-01-09T18:20:00Z", "2026-06-28T08:15:00Z"],
  [`${IMAP}\\imap.gmail.com\\Newsletters`, ["continumail@gmail.com", "Newsletters"], "Newsletters", 214_958_080, true, "account1", 4_812, "2015-05-21T06:00:00Z", "2026-07-06T05:30:00Z"],
  [`${IMAP}\\mail.continumail.com\\INBOX`, ["contact@continumail.com", "Inbox"], "Inbox", 934_281_216, true, "account2", 8_193, "2016-02-01T08:05:00Z", "2026-07-05T14:22:00Z"],
  [`${IMAP}\\mail.continumail.com\\INBOX.sbd\\Clients`, ["contact@continumail.com", "Inbox", "Clients"], "Clients", 1_204_855_808, true, "account2", 5_874, "2016-03-11T10:14:00Z", "2026-07-03T09:47:00Z"],
  [`${IMAP}\\mail.continumail.com\\INBOX.sbd\\Clients.sbd\\Northwind`, ["contact@continumail.com", "Inbox", "Clients", "Northwind"], "Northwind", 402_653_184, true, "account2", 2_310, "2019-09-30T12:00:00Z", "2026-06-30T15:18:00Z"],
  [`${IMAP}\\mail.continumail.com\\Sent`, ["contact@continumail.com", "Sent"], "Sent", 356_515_840, true, "account2", 4_468, "2016-02-01T08:31:00Z", "2026-07-05T13:58:00Z"],
  [`${MAIL}\\Local Folders\\Archive 2012-2015`, ["Local Folders", "Archive 2012-2015"], "Archive 2012-2015", 689_963_008, false, "localFolders", 7_725, "2012-01-02T09:00:00Z", "2015-12-30T17:44:00Z"],
  [`${MAIL}\\Local Folders\\Drafts`, ["Local Folders", "Drafts"], "Drafts", 3_145_728, false, "localFolders", 14, "2024-04-16T20:01:00Z", "2026-06-12T22:37:00Z"],
];

export const demoDiscover: DiscoverResult = {
  root: PROFILE_ROOT,
  layout: "thunderbird",
  sources: rows.map(([path, targetFolderPath, displayName, sourceBytes, msf, accountId]): DiscoveredSource => ({
    path, type: "mbox", targetFolderPath, displayName, sourceBytes,
    msfPath: msf ? `${path}.msf` : null, accountId,
  })),
  warnings: [],
  skipped: rows.filter(([, , , , msf]) => msf).map(([path]) => ({
    code: "msf-index", path: `${path}.msf`, reason: "Thunderbird index file (paired for flag/tag fidelity)",
  })),
  pairing: { pairedMsfCount: rows.filter(([, , , , m]) => m).length, unpairedMboxCount: 2, orphanMsfCount: 0 },
  accounts,
  calendars: demoCalendars(),
  addressBooks: demoAddressBooks(),
  schemaVersion: 1,
};

function demoCalendars(): DiscoveredCalendar[] {
  return [
    {
      calId: "cal-home", displayName: "Home", storeKind: "local",
      storePath: `${PROFILE_ROOT}\\calendar-data\\local.sqlite`,
      calendarType: "both", isVisibleInThunderbird: true,
      eventCount: 412, taskCount: 37,
      defaultCalendarFolderPath: ["Calendar", "Home"], defaultTaskFolderPath: ["Tasks", "Home"],
      accountId: null,
    },
    {
      calId: "cal-work", displayName: "Work", storeKind: "cache",
      storePath: `${PROFILE_ROOT}\\calendar-data\\cache.sqlite`,
      calendarType: "both", isVisibleInThunderbird: true,
      eventCount: 1_286, taskCount: 92,
      defaultCalendarFolderPath: ["Calendar", "Work"], defaultTaskFolderPath: ["Tasks", "Work"],
      accountId: "account2",
    },
  ];
}

function demoAddressBooks(): DiscoveredAddressBook[] {
  return [
    { displayName: "Personal Address Book", path: `${PROFILE_ROOT}\\abook.sqlite`, format: "thunderbird-sqlite", contactCount: 348, accountId: null },
    { displayName: "Collected Addresses", path: `${PROFILE_ROOT}\\history.sqlite`, format: "thunderbird-sqlite", contactCount: 1_027, accountId: null },
  ];
}

export function demoScanResult(paths: string[]): ScanResult {
  const byPath = new Map(rows.map((r) => [r[0], r]));
  const sources = paths.map((p, i) => {
    const r = byPath.get(p);
    if (r) {
      const [path, , displayName, sourceBytes, , , messages, dateFrom, dateTo] = r;
      return {
        id: path, path, displayName, messages,
        bytes: Math.round(sourceBytes * 0.86), sourceBytes,
        dateFrom, dateTo, warnings: 0, skipped: 0,
      };
    }
    // .mbox files mode: derive from filename
    const base = p.split(/[\\/]/).pop() ?? p;
    const stem = base.replace(/\.mbox$/i, "");
    const stats = demoMboxStats[base] ?? { messages: 1_000 + i, sourceBytes: 100_000_000 };
    return {
      id: p, path: p, displayName: stem, messages: stats.messages,
      bytes: Math.round(stats.sourceBytes * 0.84), sourceBytes: stats.sourceBytes,
      dateFrom: "2012-06-01T08:00:00Z", dateTo: "2026-07-01T18:30:00Z",
      warnings: 0, skipped: 0,
    };
  });
  const totals = sources.reduce(
    (t, s) => ({ messages: t.messages + s.messages, bytes: t.bytes + s.bytes, sourceBytes: t.sourceBytes + s.sourceBytes, sources: t.sources + 1 }),
    { messages: 0, bytes: 0, sourceBytes: 0, sources: 0 },
  );
  return { kind: "scan", schemaVersion: 1, totals, sources, skipped: [], warnings: [] } as unknown as ScanResult;
}

export const demoTakeoutDir = "C:\\Users\\Owner\\Downloads\\Takeout\\Mail";
const demoMboxStats: Record<string, { messages: number; sourceBytes: number }> = {
  "Inbox.mbox": { messages: 21_407, sourceBytes: 2_684_354_560 },
  "Sent.mbox": { messages: 9_882, sourceBytes: 734_003_200 },
  "Starred.mbox": { messages: 512, sourceBytes: 58_720_256 },
  "Work.mbox": { messages: 6_654, sourceBytes: 891_289_600 },
};
export const demoMboxFiles: FileStat[] = Object.entries(demoMboxStats).map(([base, s]) => ({
  path: `${demoTakeoutDir}\\${base}`, size: s.sourceBytes,
}));

export const totalMessages = rows.reduce((n, r) => n + r[6], 0);
