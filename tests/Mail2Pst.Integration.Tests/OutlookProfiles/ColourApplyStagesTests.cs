// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class ColourApplyStagesTests
{
    [Fact] public void StoreNotReady() => Assert.Equal("outlook-store-not-ready", ColourApplyStages.FromException(new OutlookStoreNotReadyException()));
    [Fact] public void Readback() => Assert.Equal("colour-apply-readback-failed", ColourApplyStages.FromException(new ColourReadbackException()));
    [Fact] public void VerifierFailed() => Assert.Equal("colour-verify-failed", ColourApplyStages.FromException(new ColourVerifierFailedException()));
    [Fact] public void CleanupTimeout() => Assert.Equal("outlook-process-cleanup-timeout", ColourApplyStages.FromException(new OutlookProcessCleanupTimeoutException()));
    [Fact] public void Unknown() => Assert.Equal("import-colours", ColourApplyStages.FromException(new Exception("x")));
}
