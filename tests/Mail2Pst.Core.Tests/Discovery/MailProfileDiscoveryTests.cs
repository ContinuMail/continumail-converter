// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class MailProfileDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m2p-prof-" + Guid.NewGuid());
    public MailProfileDiscoveryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private void Touch(string rel)
    {
        string p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, Array.Empty<byte>());
    }
    private string[] Path2(DiscoveredSource s) => s.TargetFolderPath.ToArray();

    [Fact]
    public void ProfileRoot_WalksAccounts_PrefixesAccountName_AndPairs()
    {
        Touch("Mail/Local Folders/Inbox");  Touch("Mail/Local Folders/Inbox.msf");
        Touch("Mail/Local Folders/Inbox.sbd/Work"); Touch("Mail/Local Folders/Inbox.sbd/Work.msf");
        Touch("ImapMail/imap.x/INBOX");     Touch("ImapMail/imap.x/INBOX.msf");

        DiscoveryResult r = MailProfileDiscovery.Discover(_root);

        Assert.Equal("thunderbird-profile", r.Layout);
        Assert.Contains(r.Sources, s => Path2(s).SequenceEqual(new[] { "Local Folders", "Inbox" }) && s.MsfPath != null);
        // Inbox.sbd/Work is a CHILD of Inbox (the .sbd rule), so the path nests under Inbox.
        Assert.Contains(r.Sources, s => Path2(s).SequenceEqual(new[] { "Local Folders", "Inbox", "Work" }) && s.MsfPath != null);
        Assert.Contains(r.Sources, s => Path2(s).SequenceEqual(new[] { "imap.x", "INBOX" }) && s.MsfPath != null);
        Assert.Equal(3, r.Pairing.PairedMsfCount);
    }

    [Fact]
    public void StoreRootInput_NamedMail_EnumeratesAccounts()
    {
        Touch("Mail/Local Folders/Inbox");
        DiscoveryResult r = MailProfileDiscovery.Discover(Path.Combine(_root, "Mail"));
        Assert.Equal("thunderbird-store-root", r.Layout);
        Assert.Contains(r.Sources, s => Path2(s).SequenceEqual(new[] { "Local Folders", "Inbox" }));
    }

    [Fact]
    public void SingleAccountDir_NoPrefix_TodaysBehaviour()
    {
        Touch("Inbox"); Touch("Inbox.msf");
        DiscoveryResult r = MailProfileDiscovery.Discover(_root);
        Assert.Equal(new[] { "Inbox" }, Path2(r.Sources.Single())); // no account prefix
    }

    [Fact]
    public void LooseFileUnderStore_IsSkippedWithWarning()
    {
        Touch("Mail/Local Folders/Inbox");
        Touch("Mail/loose.txt"); // a file directly under Mail/
        DiscoveryResult r = MailProfileDiscovery.Discover(_root);
        Assert.Contains(r.Skipped, k => k.Path.EndsWith("loose.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossAccountDuplicate_EmitsOneWarning()
    {
        // Same account dir name "Dup" under both Mail/ and ImapMail/ -> same prefixed path ["Dup","Inbox"].
        Touch("Mail/Dup/Inbox");
        Touch("ImapMail/Dup/Inbox");
        DiscoveryResult r = MailProfileDiscovery.Discover(_root);
        Assert.Single(r.Warnings, w => w.Code == "duplicate-target-folder-path");
    }
}
