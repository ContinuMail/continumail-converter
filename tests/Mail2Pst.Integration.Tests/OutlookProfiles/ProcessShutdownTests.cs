// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class ProcessShutdownTests
{
    private sealed class FakeProc : IProcessHandle
    {
        public bool ExitsOnWait;
        public bool Killed;
        public bool CloseCalled;
        public bool StartExited;
        private bool _exited;
        public bool HasExited => _exited || StartExited;
        public void CloseMainWindow() { CloseCalled = true; }
        public bool WaitForExit(int ms) { if (ExitsOnWait) _exited = true; return HasExited; }
        public void Kill() { Killed = true; _exited = true; }
    }

    [Fact]
    public void WaitForCleanExit_ProcessExits_ReturnsTrue_NoKill_AfterClose()
    {
        var p = new FakeProc { ExitsOnWait = true };
        bool clean = ProcessShutdown.WaitForCleanExit(p, TimeSpan.FromSeconds(45));
        Assert.True(clean);
        Assert.False(p.Killed);
        Assert.True(p.CloseCalled); // signal-first: CloseMainWindow is attempted before the wait
    }

    [Fact]
    public void WaitForCleanExit_ProcessHangs_KillsAndReturnsFalse()
    {
        var p = new FakeProc { ExitsOnWait = false };
        bool clean = ProcessShutdown.WaitForCleanExit(p, TimeSpan.FromMilliseconds(1));
        Assert.False(clean);
        Assert.True(p.Killed);
        Assert.True(p.CloseCalled);
    }

    [Fact]
    public void WaitForCleanExit_AlreadyExited_ReturnsTrue_NoCloseNoKill()
    {
        var p = new FakeProc { StartExited = true };
        bool clean = ProcessShutdown.WaitForCleanExit(p, TimeSpan.FromSeconds(45));
        Assert.True(clean);
        Assert.False(p.CloseCalled);
        Assert.False(p.Killed);
    }

    [Fact]
    public void WaitUntilGone_AllDead_ReturnsTrue()
    {
        bool gone = ProcessCleanup.WaitUntilGone(new[] { 100, 200 }, _ => false, TimeSpan.FromSeconds(1), _ => { });
        Assert.True(gone);
    }

    [Fact]
    public void WaitUntilGone_StaysAlive_TimesOutFalse()
    {
        bool gone = ProcessCleanup.WaitUntilGone(new[] { 100 }, _ => true, TimeSpan.FromMilliseconds(1), _ => { });
        Assert.False(gone);
    }
}
