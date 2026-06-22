// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class MailProfileDiscoveryAccountIdTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m2p-acctid-" + Guid.NewGuid());
    public MailProfileDiscoveryAccountIdTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Touch(string rel)
    {
        string p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, Array.Empty<byte>());
    }

    [Fact]
    public void Discover_StoreRoot_SetsAccountIdToAccountDir()
    {
        // ImapMail/<account>/INBOX — store-root layout
        Touch("ImapMail/imap.example.com/INBOX");
        string acctDir = Path.Combine(_root, "ImapMail", "imap.example.com");

        DiscoveryResult result = MailProfileDiscovery.Discover(Path.Combine(_root, "ImapMail"));

        Assert.NotEmpty(result.Sources);
        Assert.All(result.Sources, s => Assert.Equal(acctDir, s.AccountId));
    }

    [Fact]
    public void Discover_ProfileRoot_SetsAccountIdPerAccountDir()
    {
        // Profile layout: Mail/<acct1>/Inbox, ImapMail/<acct2>/INBOX
        Touch("Mail/Local Folders/Inbox");
        Touch("ImapMail/imap.example.com/INBOX");
        string acct1Dir = Path.Combine(_root, "Mail", "Local Folders");
        string acct2Dir = Path.Combine(_root, "ImapMail", "imap.example.com");

        DiscoveryResult result = MailProfileDiscovery.Discover(_root);

        var acct1Sources = result.Sources.Where(s => s.AccountId == acct1Dir).ToList();
        var acct2Sources = result.Sources.Where(s => s.AccountId == acct2Dir).ToList();
        Assert.NotEmpty(acct1Sources);
        Assert.NotEmpty(acct2Sources);
    }

    [Fact]
    public void Discover_SingleTree_AccountIdIsNull()
    {
        // Single-tree input (no Mail/ImapMail child) — AccountId must remain null
        Touch("Inbox");

        DiscoveryResult result = MailProfileDiscovery.Discover(_root);

        Assert.All(result.Sources, s => Assert.Null(s.AccountId));
    }
}
