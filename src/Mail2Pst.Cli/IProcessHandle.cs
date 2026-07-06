// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Cli;

/// <summary>Testable seam over the parts of System.Diagnostics.Process the shutdown decision uses.</summary>
internal interface IProcessHandle
{
    bool HasExited { get; }
    void CloseMainWindow();
    bool WaitForExit(int ms);
    void Kill();
}

/// <summary>Blocks until every tracked pid is gone (or the cap expires). The KB-004 guard: never start a
/// cold verify or a retry while an Outlook instance we started is still alive. <c>isAlive</c> is injected
/// so the wait is unit-testable without real processes.</summary>
internal static class ProcessCleanup
{
    // delay(iteration) is called once per poll and MUST actually sleep in production (a no-op delay with a
    // non-tiny cap would busy-spin). isAlive must be exception-safe (a missing PID ⇒ false, never throw).
    internal static bool WaitUntilGone(IReadOnlyList<int> pids, Func<int, bool> isAlive, TimeSpan cap, Action<int> delay)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; ; i++)
        {
            bool anyAlive = false;
            foreach (int pid in pids) if (isAlive(pid)) { anyAlive = true; break; }
            if (!anyAlive) return true;
            if (sw.Elapsed >= cap) return false;
            delay(i);
        }
    }
}

/// <summary>Wait-for-clean-self-exit-then-kill. The cap is deadlock protection, not a success timer:
/// a return of true means Outlook exited on its own (the strongest practical flush signal); false means
/// we had to force-kill (persistence uncertain).</summary>
internal static class ProcessShutdown
{
    internal static bool WaitForCleanExit(IProcessHandle p, TimeSpan cap)
    {
        if (p.HasExited) return true; // already gone on its own ⇒ clean, nothing to close/kill
        try { p.CloseMainWindow(); } catch { }
        try
        {
            if (p.WaitForExit((int)cap.TotalMilliseconds)) return true;
            if (!p.HasExited) p.Kill();
        }
        catch { }
        // Best-effort: let the kill land before returning, so the next cleanup guard is less likely to see a
        // still-exiting process. Not the guarantee (that's ProcessCleanup.WaitUntilGone) — just courtesy.
        try { p.WaitForExit(5000); } catch { }
        return false;
    }
}
