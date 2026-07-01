# PSTFileFormat — ContinuMail Modifications

This file tracks all changes made to the vendored `ROM-Knowledgeware/PSTFileFormat` library
(upstream unmaintained since 2019) by ContinuMail. The upstream code is LGPLv3; all
modifications remain under LGPLv3 per the license terms.

---

## 2026-07-01 — Bare RecurrencePattern prefix for PidLidTaskRecurrence (PR7b Task 1)

**File:** `Messaging/Messages/RecurrencePatternStructure/AppointmentRecurrencePatternStructure.cs`

**What:** Extracted the RecurrencePattern prefix bytes (ReaderVersion … EndDate, lines 169–197
of the original `GetBytes`) into a private `WriteRecurrencePattern(MemoryStream)` helper, and
added a public `byte[] GetRecurrencePatternBytes()` method that emits ONLY that prefix — the
bare MS-OXOCAL RecurrencePattern with no AppointmentRecurrencePattern tail. `GetBytes` now
delegates the prefix to `WriteRecurrencePattern` and is byte-identical to the previous
implementation for all appointment paths.

**Why:** `PidLidTaskRecurrence` (0x8116, PSETID_Task) stores a bare RecurrencePattern (not an
AppointmentRecurrencePattern), so the existing serializer needed to be splittable. The new
method is the vendor-side building block for the PR7b recurring-task writer (Task 3).

**Supporting changes:**
- `Messaging/Enums/PropertyLongID.cs`: added `PidLidTaskRecurrence = 0x00008116`.
- `Messaging/NamedProperties/PropertyNames.cs`: registered `PidLidTaskFRecurring` and
  `PidLidTaskRecurrence` under `PSETID_Task`.

**Test gate:** `tests/Mail2Pst.Core.Tests/Vendor/RecurringAppointmentBlobTests.cs` — 4 new tests:
- `GetRecurrencePatternBytes_is_the_prefix_of_GetBytes` (prefix-equality against full blob).
- `Bare_pattern_matches_task_GT_oracle` (Theory ×3: weekly 54 B / monthly 54 B / daily 50 B)
  asserted byte-for-byte against real Outlook-authored `PidLidTaskRecurrence` blobs.
- All 20 pre-existing appointment byte-gate tests still pass (split is a pure extraction).

---

## 2026 (prior to 2026-07-01) — CreateEmptyStore, OccurrenceCount fix, and other reworks

*(Earlier modifications predating this log file — see git history on the `vendor/` subtree.)*
