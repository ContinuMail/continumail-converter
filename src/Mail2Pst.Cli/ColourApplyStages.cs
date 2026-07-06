// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Cli;

/// <summary>Thrown when opening the transient <see cref="OutlookComCategoryStore"/> for an apply attempt
/// fails after exhausting <see cref="TransientComRetry"/>'s retries — the store never became ready.</summary>
internal sealed class OutlookStoreNotReadyException : Exception
{
    internal OutlookStoreNotReadyException() { }
    internal OutlookStoreNotReadyException(string message) : base(message) { }
    internal OutlookStoreNotReadyException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when the cold, fresh-session <see cref="OutlookCategoryVerifier"/> itself failed to run
/// (COM error / STA timeout / logon failure), as opposed to running and finding categories missing.</summary>
internal sealed class ColourVerifierFailedException : Exception
{
    internal ColourVerifierFailedException() { }
    internal ColourVerifierFailedException(string message) : base(message) { }
    internal ColourVerifierFailedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown by the KB-004 guard (<see cref="ProcessCleanup.WaitUntilGone"/>) when a tracked,
/// self-started Outlook instance is still alive after its wait budget — we refuse to start a cold verify
/// or a retry attempt against it.</summary>
internal sealed class OutlookProcessCleanupTimeoutException : Exception
{
    internal OutlookProcessCleanupTimeoutException() { }
    internal OutlookProcessCleanupTimeoutException(string message) : base(message) { }
    internal OutlookProcessCleanupTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Maps the typed exceptions the colour-apply orchestration can throw to a stable CLI error
/// <c>stage</c> string. Pure — no COM/Outlook types touched — so it gets direct unit coverage despite the
/// rest of <see cref="ImportColoursCommand"/>'s apply path being COM-heavy and hard to test headlessly.</summary>
internal static class ColourApplyStages
{
    internal static string FromException(Exception ex) => ex switch
    {
        OutlookStoreNotReadyException => "outlook-store-not-ready",
        ColourReadbackException => "colour-apply-readback-failed",
        ColourVerifierFailedException => "colour-verify-failed",
        OutlookProcessCleanupTimeoutException => "outlook-process-cleanup-timeout",
        _ when OutlookDetection.LooksLikeInteractiveLogonRequired(ex) => "outlook-profile-logon-failed",
        _ => "import-colours",
    };
}
