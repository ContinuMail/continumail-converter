// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Discovery;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class MailProfileDiscoveryAccountsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m2p-accounts-" + Guid.NewGuid());
    public MailProfileDiscoveryAccountsTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Touch(string rel)
    {
        string p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, Array.Empty<byte>());
    }

    [Fact]
    public void Discover_Profile_BuildsAccountsWithEmail()
    {
        // ImapMail/imap.example.com/INBOX — one message so it is discovered
        Touch("ImapMail/imap.example.com/INBOX");

        // prefs.js at the profile root wires up server1 -> identity email alice@example.com
        File.WriteAllText(Path.Combine(_root, "prefs.js"), string.Join("\n", new[]
        {
            "user_pref(\"mail.server.server1.directory-rel\", \"[ProfD]ImapMail/imap.example.com\");",
            "user_pref(\"mail.server.server1.hostname\", \"imap.example.com\");",
            "user_pref(\"mail.server.server1.type\", \"imap\");",
            "user_pref(\"mail.account.account1.server\", \"server1\");",
            "user_pref(\"mail.account.account1.identities\", \"id1\");",
            "user_pref(\"mail.identity.id1.useremail\", \"alice@example.com\");",
        }));

        DiscoveryResult r = MailProfileDiscovery.Discover(_root);

        Account a = Assert.Single(r.Accounts);
        Assert.Equal("alice@example.com", a.Email);
        Assert.Equal("imap.example.com", a.FolderSegment);
        Assert.Equal(AddressResolution.Identity, a.AddressResolution);
    }

    [Fact]
    public void Discover_Profile_LocalFoldersWithoutPrefs_ResolvesToLocalFolders()
    {
        // Mail/Local Folders/Inbox — no prefs.js present
        Touch("Mail/Local Folders/Inbox");

        DiscoveryResult r = MailProfileDiscovery.Discover(_root);

        Account a = Assert.Single(r.Accounts);
        Assert.Equal("Local Folders", a.FolderSegment);
        Assert.Equal(AddressResolution.LocalFolders, a.AddressResolution);
        Assert.Null(a.Email);
    }
}
