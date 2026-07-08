// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using Mail2Pst.TestSupport;
using Xunit;

namespace Mail2Pst.Integration.Tests.TestSupport;

// Tier 0: fast unit tests for the profile generator itself — every PR.
public class ThunderbirdProfileBuilderTests
{
    [Fact]
    public void Build_WritesPrefsJsWiringAccountToMailDir()
    {
        using GeneratedProfile p = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("INBOX", messageCount: 1)
            .Build();

        string prefs = File.ReadAllText(Path.Combine(p.RootPath, "prefs.js"));
        Assert.Contains("[ProfD]ImapMail/imap.example.com", prefs);
        Assert.Contains("alice@example.com", prefs);
        Assert.Contains("\"imap\"", prefs);
    }

    [Fact]
    public void Build_GeneratesRequestedFolderCountAndMessages()
    {
        using GeneratedProfile p = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolders(count: 5, namePattern: "Folder-{0:D3}", messagesEach: 2)
            .Build();

        Assert.Equal(5, p.Folders.Count);
        Assert.Equal(5, p.MailFilePaths.Count);
        foreach (GeneratedFolder folder in p.Folders)
        {
            Assert.Equal(2, folder.MessageCount);
            string content = File.ReadAllText(folder.FilePath);
            // mboxrd: every message begins with a "From " separator line; counting those
            // lines counts messages without taking a parser dependency.
            int separators = content.StartsWith("From ") ? 1 : 0;
            separators += content.Split("\nFrom ").Length - 1;
            Assert.Equal(2, separators);
        }
    }

    [Fact]
    public void Build_WithDeepRoot_ProducesRealisticallyLongProfilePath()
    {
        using GeneratedProfile p = new ThunderbirdProfileBuilder()
            .WithDeepRoot(approximatePrefixChars: 100)
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("INBOX", messageCount: 1)
            .Build();

        Assert.True(p.RootPath.Length >= 100, $"root not deep enough: {p.RootPath.Length}");
        Assert.EndsWith(".default", p.RootPath);
    }

    [Fact]
    public void Dispose_DeletesTheGeneratedTree()
    {
        GeneratedProfile p = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("INBOX", messageCount: 1)
            .Build();
        string root = p.RootPath;
        Assert.True(Directory.Exists(root));

        p.Dispose();

        Assert.False(Directory.Exists(root));
    }
}
