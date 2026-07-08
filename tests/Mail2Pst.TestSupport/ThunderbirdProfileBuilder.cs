// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace Mail2Pst.TestSupport;

/// <summary>Generates a synthetic Thunderbird profile on disk for tests: prefs.js account
/// wiring plus ImapMail mbox folder files (mboxrd, one file per mail folder, no extension —
/// the real TB layout). Generator, not fixtures: hostile shapes are built per-test.
/// All data is synthetic (example.com).</summary>
public sealed class ThunderbirdProfileBuilder
{
    private sealed record Account(string Email, string Hostname, string ServerType);
    private sealed record Folder(string Name, int MessageCount);

    private readonly List<Folder> _folders = new();
    private Account? _account;
    private int _deepRootChars;

    public ThunderbirdProfileBuilder WithDeepRoot(int approximatePrefixChars = 100)
    {
        _deepRootChars = approximatePrefixChars;
        return this;
    }

    public ThunderbirdProfileBuilder WithAccount(string email, string hostname, string serverType = "imap")
    {
        _account = new Account(email, hostname, serverType);
        return this;
    }

    public ThunderbirdProfileBuilder WithFolders(int count, string namePattern, int messagesEach)
    {
        for (int i = 1; i <= count; i++)
            _folders.Add(new Folder(string.Format(CultureInfo.InvariantCulture, namePattern, i), messagesEach));
        return this;
    }

    public ThunderbirdProfileBuilder WithFolder(string name, int messageCount)
    {
        _folders.Add(new Folder(name, messageCount));
        return this;
    }

    public GeneratedProfile Build()
    {
        if (_account is null)
            throw new InvalidOperationException("WithAccount is required before Build().");

        string deleteRoot = Path.Combine(Path.GetTempPath(), "m2p-profile-" + Guid.NewGuid().ToString("N"));

        // Deep root: pad with nested directories mimicking a real Windows profile location
        // (AppData\Roaming\Thunderbird\Profiles\...) until the prefix length is reached.
        string profileParent = deleteRoot;
        if (_deepRootChars > 0)
        {
            profileParent = Path.Combine(deleteRoot, "Users", "VeryLongWindowsUserName",
                "AppData", "Roaming", "Thunderbird", "Profiles");
            while (profileParent.Length < _deepRootChars)
                profileParent = Path.Combine(profileParent, "extra-depth-segment");
        }
        string root = Path.Combine(profileParent, "yzd8g42e.default");

        string mailDir = Path.Combine(root, "ImapMail", _account.Hostname);
        Directory.CreateDirectory(mailDir);

        var generated = new List<GeneratedFolder>(_folders.Count);
        int messageSeq = 0;
        foreach (Folder folder in _folders)
        {
            string path = Path.Combine(mailDir, folder.Name);
            var sb = new StringBuilder();
            for (int m = 0; m < folder.MessageCount; m++)
            {
                sb.Append("From sender@example.com Mon Jan 01 00:00:00 2024\r\n");
                sb.Append($"Message-ID: <gen-{++messageSeq}@example.com>\r\n");
                sb.Append($"Subject: Generated message {messageSeq}\r\n");
                sb.Append("From: sender@example.com\r\n");
                sb.Append("To: alice@example.com\r\n");
                sb.Append("Date: Mon, 1 Jan 2024 00:00:00 +0000\r\n");
                sb.Append("\r\n");
                sb.Append($"Synthetic body {messageSeq}.\r\n\r\n");
            }
            File.WriteAllText(path, sb.ToString());
            generated.Add(new GeneratedFolder(folder.Name, path, folder.MessageCount));
        }

        File.WriteAllText(Path.Combine(root, "prefs.js"), string.Join("\n", new[]
        {
            $"user_pref(\"mail.server.server1.directory-rel\", \"[ProfD]ImapMail/{_account.Hostname}\");",
            $"user_pref(\"mail.server.server1.hostname\", \"{_account.Hostname}\");",
            $"user_pref(\"mail.server.server1.type\", \"{_account.ServerType}\");",
            "user_pref(\"mail.account.account1.server\", \"server1\");",
            "user_pref(\"mail.account.account1.identities\", \"id1\");",
            $"user_pref(\"mail.identity.id1.useremail\", \"{_account.Email}\");",
        }));

        return new GeneratedProfile(root, deleteRoot, generated);
    }
}
