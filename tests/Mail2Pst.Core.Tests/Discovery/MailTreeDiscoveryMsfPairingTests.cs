// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class MailTreeDiscoveryMsfPairingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "m2p-msf-" + Guid.NewGuid());
    public MailTreeDiscoveryMsfPairingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private void Touch(string rel) // creates an empty file (valid empty-folder mbox / never-parsed .msf)
    {
        string p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, Array.Empty<byte>());
    }

    [Fact]
    public void PairedMbox_GetsMsfPath_AndMsfNotCountedAsMetadata()
    {
        Touch("Inbox"); Touch("Inbox.msf");
        DiscoveryResult r = MailTreeDiscovery.Discover(_dir);
        DiscoveredSource s = r.Sources.Single();
        Assert.Equal(Path.Combine(_dir, "Inbox.msf"), s.MsfPath);
        Assert.Equal(1, r.Pairing.PairedMsfCount);
        Assert.Equal(0, r.Pairing.UnpairedMboxCount);
        Assert.Equal(0, r.Pairing.OrphanMsfCount);
        // paired .msf must NOT show up as a skipped metadata entry
        Assert.DoesNotContain(r.Skipped, k => k.Path.EndsWith("Inbox.msf", StringComparison.Ordinal));
    }

    [Fact]
    public void UnpairedMbox_HasNullMsfPath()
    {
        Touch("Sent"); // no Sent.msf
        DiscoveryResult r = MailTreeDiscovery.Discover(_dir);
        Assert.Null(r.Sources.Single().MsfPath);
        Assert.Equal(1, r.Pairing.UnpairedMboxCount);
        Assert.Equal(0, r.Pairing.PairedMsfCount);
    }

    [Fact]
    public void OrphanMsf_IsCounted_AndSkipped()
    {
        Touch("Ghost.msf"); // no sibling base file at all
        DiscoveryResult r = MailTreeDiscovery.Discover(_dir);
        Assert.Empty(r.Sources);
        Assert.Equal(1, r.Pairing.OrphanMsfCount);
        Assert.Contains(r.Skipped, s => s.Code == "orphan-msf" && s.Path.EndsWith("Ghost.msf", StringComparison.Ordinal));
    }

    [Fact]
    public void MsfBesideNonMboxBase_DoesNotPair_IsOrphan()
    {
        // "Foo" is NOT an accepted mbox (non-empty, no envelope postmark) -> Foo.msf must be an orphan,
        // not a pairing. (The core "accepted-mbox-only" rule lives here in MailTreeDiscovery.)
        File.WriteAllText(Path.Combine(_dir, "Foo"), "this is not an mbox postmark\n");
        Touch("Foo.msf");
        DiscoveryResult r = MailTreeDiscovery.Discover(_dir);
        Assert.Empty(r.Sources);                    // Foo is not-mbox
        Assert.Equal(0, r.Pairing.PairedMsfCount);
        Assert.Equal(1, r.Pairing.OrphanMsfCount);  // Foo.msf has no ACCEPTED mbox sibling
    }

    [Fact]
    public void Prefix_PrependsToTargetFolderPath()
    {
        Touch("Inbox"); Touch("Inbox.msf");
        DiscoveryResult r = MailTreeDiscovery.Discover(_dir, new[] { "Local Folders" });
        Assert.Equal(new[] { "Local Folders", "Inbox" }, r.Sources.Single().TargetFolderPath);
    }
}
