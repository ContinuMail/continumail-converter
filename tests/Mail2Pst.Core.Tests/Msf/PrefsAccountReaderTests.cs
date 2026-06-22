// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class PrefsAccountReaderTests
{
    private const string Prefs = @"
user_pref(""mail.server.server1.directory-rel"", ""[ProfD]ImapMail/imap.example.com"");
user_pref(""mail.server.server1.hostname"", ""imap.example.com"");
user_pref(""mail.server.server1.type"", ""imap"");
user_pref(""mail.server.server1.userName"", ""alice@example.com"");
user_pref(""mail.account.account1.server"", ""server1"");
user_pref(""mail.account.account1.identities"", ""id1"");
user_pref(""mail.identity.id1.useremail"", ""alice@example.com"");
user_pref(""mail.server.server2.directory-rel"", ""[ProfD]Mail/Local Folders"");
user_pref(""mail.server.server2.type"", ""none"");
";

    [Fact]
    public void ParseText_ResolvesIdentityEmail()
    {
        var map = PrefsAccountReader.ParseText(Prefs);
        var a = map["imapmail/imap.example.com"];
        Assert.Equal("alice@example.com", a.Email);
        Assert.Equal("imap.example.com", a.Host);
        Assert.Equal(AddressResolution.Identity, a.Resolution);
    }

    [Fact]
    public void ParseText_LocalFolders_IsLocalNotFailure()
    {
        var map = PrefsAccountReader.ParseText(Prefs);
        var a = map["mail/local folders"];
        Assert.Null(a.Email);
        Assert.Equal(AddressResolution.LocalFolders, a.Resolution);
    }

    [Fact]
    public void ParseText_AbsoluteDirectory_AndImapNoIdentity_IsNotFound()
    {
        var map = PrefsAccountReader.ParseText(
            "user_pref(\"mail.server.server1.directory\", \"C:\\\\x\\\\ImapMail\\\\imap.other.test\");\n" +
            "user_pref(\"mail.server.server1.type\", \"imap\");\n" +
            "user_pref(\"mail.server.server1.hostname\", \"imap.other.test\");\n");
        Assert.True(map.ContainsKey("imapmail/imap.other.test"));
        Assert.Equal(AddressResolution.NotFound, map["imapmail/imap.other.test"].Resolution); // imap, no identity
    }

    [Fact]
    public void ParseText_UnescapesIdentityEmail()
    {
        var map = PrefsAccountReader.ParseText(
            "user_pref(\"mail.server.server1.directory-rel\", \"[ProfD]ImapMail/x\");\n" +
            "user_pref(\"mail.server.server1.type\", \"imap\");\n" +
            "user_pref(\"mail.account.account1.server\", \"server1\");\n" +
            "user_pref(\"mail.account.account1.identities\", \"id1\");\n" +
            "user_pref(\"mail.identity.id1.useremail\", \"al\\u00e6ce@example.com\");\n");
        Assert.Equal("alæce@example.com", map["imapmail/x"].Email);
    }
}
