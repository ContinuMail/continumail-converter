// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Cli;

/// <summary>Allowlist classifier for "freshly-created profile/store not ready yet" COM-open errors.
/// Unknown errors are NON-transient by default. The transient HRESULT set is seeded from Task 0 evidence
/// (docs/research/2026-07-06-store-not-ready-hresults.md); until an HRESULT is confirmed there, this stays
/// conservative (returns false → no retry, readiness rests on create's clean-exit).</summary>
internal static class ComErrorClassifier
{
    internal static bool IsTransientOpen(Exception ex)
    {
        // Explicit do-not-retry classes first (fast-fail).
        if (ex is FormatException) return false;                              // corrupt CategoryList XML
        if (ex is UnauthorizedAccessException) return false;                  // access denied
        if (OutlookDetection.LooksLikeInteractiveLogonRequired(ex)) return false;

        // Transient allowlist — fill ONLY from Task 0 evidence, e.g.:
        // if (ex is System.Runtime.InteropServices.COMException c &&
        //     unchecked((uint)c.HResult) is 0x80040111 /* ClassFactory not available */ ...) return true;
        // If Task 0 records NO reproducible transient error, DO NOT invent one — leave this returning false
        // (retry disabled; readiness then rests entirely on create's clean-exit, Task 4). Unknown = non-retry.
        return false; // conservative default until Task 0 confirms an HRESULT
    }
}
