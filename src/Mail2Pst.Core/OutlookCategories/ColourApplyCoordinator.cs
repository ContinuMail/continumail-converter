// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.OutlookCategories;

public sealed record ColourApplyResult(bool Success, bool ShutdownClean, bool ColdVerifyAttempted, bool ColdVerified, int RetryCount, string? FailureStage);

/// <summary>Branch policy for verify-then-close colour apply. Pure: all Outlook/process work is behind
/// the three delegates. Clean self-exit ⇒ trusted (no cold verify). Forced kill ⇒ cold verify; on miss,
/// retry the whole apply once (a clean retry short-circuits without a second cold verify).</summary>
public static class ColourApplyCoordinator
{
    public static ColourApplyResult Run(Func<bool> attempt, Func<bool> coldVerify, Action ensureNoTrackedProcess)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(coldVerify);
        ArgumentNullException.ThrowIfNull(ensureNoTrackedProcess);

        bool coldAttempted = false;
        for (int retry = 0; retry <= 1; retry++)
        {
            ensureNoTrackedProcess();
            bool cleanExit = attempt(); // throws on fatal (store-not-ready / read-back) → propagate
            if (cleanExit)
                // Final attempt exited cleanly ⇒ ShutdownClean regardless of an earlier kill on attempt 1.
                return new ColourApplyResult(true, ShutdownClean: true, coldAttempted, ColdVerified: false, retry, null);

            // forced kill → persistence uncertain
            coldAttempted = true;
            ensureNoTrackedProcess();
            if (coldVerify())
                return new ColourApplyResult(true, ShutdownClean: false, ColdVerifyAttempted: true, ColdVerified: true, retry, null);
            // miss: loop retries once; after the retry's cold verify also misses, fail
        }
        return new ColourApplyResult(false, ShutdownClean: false, ColdVerifyAttempted: true, ColdVerified: false, RetryCount: 1, "colour-apply-unverified");
    }
}
