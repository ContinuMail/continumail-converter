// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Cli;

/// <summary>Cold, read-only, fresh-session verification that a colour-import batch actually persisted:
/// opens a BRAND NEW <see cref="OutlookComCategoryStore"/> (its own transient Outlook instance, torn down
/// on return — see KB-004) in read-only mode, re-reads the master category list FAI from disk, and diffs
/// it against what the apply step expected. Deliberately independent of the apply-time in-session
/// read-back (<see cref="OutlookComCategoryStore.Commit(IReadOnlyDictionary{string,int})"/>) — that one
/// can be fooled by a stale in-memory cache; this one proves the write is durable across a fresh logon.</summary>
[SupportedOSPlatform("windows")]
internal static class OutlookCategoryVerifier
{
    /// <summary>Opens a fresh, read-only <see cref="OutlookComCategoryStore"/> on an STA thread bounded by
    /// <paramref name="cap"/>, re-reads the persisted master category list, and diffs it against
    /// <paramref name="expected"/>. An empty <paramref name="expected"/> is trivially satisfied WITHOUT
    /// launching Outlook. Never calls <c>Commit</c>/<c>Save</c> — read-only, strict by-name logon (no
    /// default-profile fallback; <paramref name="profileName"/> must already be resolved).</summary>
    internal static ColdVerifyOutcome Verify(string profileName, IReadOnlyDictionary<string, int> expected, TimeSpan cap)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        ArgumentNullException.ThrowIfNull(expected);

        if (expected.Count == 0)
            return new ColdVerifyOutcome(AllPresent: true, Missing: Array.Empty<string>(), VerifierFailed: false);

        try
        {
            return Sta.Run(() =>
            {
                using var store = new OutlookComCategoryStore(profileName, readOnly: true);
                IReadOnlyDictionary<string, int> actual = store.ReadPersistedColours();
                IReadOnlyList<string> missing = CategoryVerify.Missing(expected, actual);
                return new ColdVerifyOutcome(AllPresent: missing.Count == 0, Missing: missing, VerifierFailed: false);
            }, cap);
        }
        catch (CategoryListMissingException)
        {
            return new ColdVerifyOutcome(AllPresent: false, Missing: expected.Keys.ToArray(), VerifierFailed: false);
        }
        catch (Exception)
        {
            // Any other operational failure (COM error, STA timeout, logon failure, ...) is
            // "we couldn't verify", not "we verified it's missing" — surfaced by the caller as
            // stage colour-verify-failed.
            return new ColdVerifyOutcome(AllPresent: false, Missing: Array.Empty<string>(), VerifierFailed: true);
        }
    }
}

/// <summary>Result of a cold, read-only, fresh-session verification of a colour-import batch. See
/// <see cref="OutlookCategoryVerifier.Verify"/> for the exact verdict rules.</summary>
internal sealed record ColdVerifyOutcome(bool AllPresent, IReadOnlyList<string> Missing, bool VerifierFailed);
