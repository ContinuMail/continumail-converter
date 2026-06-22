// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text.Json;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests;

[Collection("ConsoleCapture")]
public class DiscoverCommandAccountsTests
{
    [Fact]
    public void Discover_Profile_EmitsAccountsAndAccountId()
    {
        string root = Path.Combine(Path.GetTempPath(), "m2p-disc-acct-" + Guid.NewGuid());
        try
        {
            // ImapMail/imap.example.com/INBOX — must be non-empty to be discovered
            string inboxPath = Path.Combine(root, "ImapMail", "imap.example.com", "INBOX");
            Directory.CreateDirectory(Path.GetDirectoryName(inboxPath)!);
            File.WriteAllText(inboxPath,
                "From sender@example.com Mon Jan 01 00:00:00 2024\r\n" +
                "Message-ID: <test-acct-1@example.com>\r\n" +
                "Subject: Test\r\n" +
                "\r\n" +
                "Body.\r\n");

            // prefs.js: wire server1 -> account1 -> identity id1 -> alice@example.com
            File.WriteAllText(Path.Combine(root, "prefs.js"), string.Join("\n", new[]
            {
                "user_pref(\"mail.server.server1.directory-rel\", \"[ProfD]ImapMail/imap.example.com\");",
                "user_pref(\"mail.server.server1.hostname\", \"imap.example.com\");",
                "user_pref(\"mail.server.server1.type\", \"imap\");",
                "user_pref(\"mail.account.account1.server\", \"server1\");",
                "user_pref(\"mail.account.account1.identities\", \"id1\");",
                "user_pref(\"mail.identity.id1.useremail\", \"alice@example.com\");",
            }));

            var sw = new StringWriter();
            TextWriter original = Console.Out;
            Console.SetOut(sw);
            try
            {
                int exit = DiscoverCommand.Run(new[] { "--input", root });
                Assert.Equal(0, exit);
            }
            finally
            {
                Console.SetOut(original);
            }

            string stdout = sw.ToString();
            using JsonDocument doc = JsonDocument.Parse(stdout);
            JsonElement rootEl = doc.RootElement;

            // accounts[] must be non-empty and carry email + addressResolution
            Assert.NotEqual(0, rootEl.GetProperty("accounts").GetArrayLength());
            JsonElement acct = rootEl.GetProperty("accounts")[0];
            Assert.Equal("alice@example.com", acct.GetProperty("email").GetString());
            Assert.Equal("identity", acct.GetProperty("addressResolution").GetString());

            // each source must carry a non-null accountId
            Assert.NotEqual(0, rootEl.GetProperty("sources").GetArrayLength());
            JsonElement src = rootEl.GetProperty("sources")[0];
            Assert.False(string.IsNullOrEmpty(src.GetProperty("accountId").GetString()));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Discover_NonProfile_EmitsEmptyAccountsAndNullAccountId()
    {
        string root = Path.Combine(Path.GetTempPath(), "m2p-disc-noprofile-" + Guid.NewGuid());
        try
        {
            // A plain mbox file (non-profile layout) — no prefs.js, no ImapMail structure
            Directory.CreateDirectory(root);
            string mboxPath = Path.Combine(root, "Inbox");
            File.WriteAllText(mboxPath,
                "From sender@example.com Mon Jan 01 00:00:00 2024\r\n" +
                "Message-ID: <test-np-1@example.com>\r\n" +
                "Subject: Test\r\n" +
                "\r\n" +
                "Body.\r\n");

            var sw = new StringWriter();
            TextWriter original = Console.Out;
            Console.SetOut(sw);
            try
            {
                int exit = DiscoverCommand.Run(new[] { "--input", root });
                Assert.Equal(0, exit);
            }
            finally
            {
                Console.SetOut(original);
            }

            string stdout = sw.ToString();
            using JsonDocument doc = JsonDocument.Parse(stdout);
            JsonElement rootEl = doc.RootElement;

            // non-profile: accounts[] must be empty
            Assert.Equal(0, rootEl.GetProperty("accounts").GetArrayLength());

            // non-profile: accountId on each source must be null
            foreach (JsonElement src in rootEl.GetProperty("sources").EnumerateArray())
            {
                JsonElement accountIdEl = src.GetProperty("accountId");
                Assert.True(accountIdEl.ValueKind == JsonValueKind.Null,
                    $"Expected accountId null but got: {accountIdEl}");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
