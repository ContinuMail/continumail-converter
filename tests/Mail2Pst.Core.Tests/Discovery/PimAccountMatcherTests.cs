// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Discovery;
using Mail2Pst.Core.Msf;
using Xunit;

public class PimAccountMatcherTests
{
    private static Account Acct(string id, string? email, string? host) =>
        new(id, id, id, null, email, host, AddressResolution.NotFound);

    private static readonly IReadOnlyList<Account> Accounts = new[]
    {
        Acct("/p/ImapMail/imap.example.com", "aksel@example.com", "imap.example.com"),
        Acct("/p/ImapMail/mail.example.org", "aksel@example.org", "mail.example.org"),
    };

    [Fact]
    public void Email_literal_in_url_matches()
    {
        var m = PimAccountMatcher.Match("https://caldav.example/aksel@example.org/cal", Accounts);
        Assert.Equal("/p/ImapMail/mail.example.org", m.AccountId);
        Assert.False(m.Ambiguous);
    }

    [Fact]
    public void Email_url_encoded_matches()
    {
        var m = PimAccountMatcher.Match("https://apidata.example.com/caldav/v2/aksel%40example.com/events", Accounts);
        Assert.Equal("/p/ImapMail/imap.example.com", m.AccountId);
    }

    [Fact]
    public void Host_exact_matches_when_no_email()
    {
        var noEmail = new[] { Acct("/p/A", null, "cloud.acme.test") };
        var m = PimAccountMatcher.Match("https://cloud.acme.test/dav/cal", noEmail);
        Assert.Equal("/p/A", m.AccountId);
    }

    [Fact]
    public void Host_subdomain_matches()
    {
        var noEmail = new[] { Acct("/p/A", null, "acme.test") };
        var m = PimAccountMatcher.Match("https://dav.acme.test/cal", noEmail);
        Assert.Equal("/p/A", m.AccountId);
    }

    [Fact]
    public void Host_substring_does_not_match()
    {
        var noEmail = new[] { Acct("/p/A", null, "acme.test") };
        var m = PimAccountMatcher.Match("https://acme.test.evil.example/cal", noEmail);
        Assert.Null(m.AccountId);          // "acme.test.evil.example" is NOT a subdomain of "acme.test"
        Assert.False(m.Ambiguous);
    }

    [Fact]
    public void Ambiguous_host_returns_null_and_flags()
    {
        var two = new[] { Acct("/p/A", null, "example.net"), Acct("/p/B", null, "example.net") };
        var m = PimAccountMatcher.Match("https://caldav.example.net/dav", two);
        Assert.Null(m.AccountId);
        Assert.True(m.Ambiguous);
    }

    [Fact]
    public void Ambiguous_email_returns_null_and_flags()
    {
        var two = new[] { Acct("/p/A", "shared@example.net", "a.example.net"), Acct("/p/B", "shared@example.net", "b.example.net") };
        var m = PimAccountMatcher.Match("https://dav/shared@example.net/cal", two);
        Assert.Null(m.AccountId);
        Assert.True(m.Ambiguous);
    }

    [Fact]
    public void Local_storage_uri_returns_null_not_ambiguous()
    {
        var m = PimAccountMatcher.Match("moz-storage-calendar://", Accounts);
        Assert.Null(m.AccountId);
        Assert.False(m.Ambiguous);
    }

    [Fact]
    public void Null_or_empty_returns_null()
    {
        Assert.Null(PimAccountMatcher.Match(null, Accounts).AccountId);
        Assert.Null(PimAccountMatcher.Match("", Accounts).AccountId);
    }

    [Fact]
    public void Email_substring_of_longer_localpart_does_not_match()
    {
        var accounts = new[] { Acct("/p/info", "info@example.com", "imap.example.com") };
        var m = PimAccountMatcher.Match("https://dav/myinfo@example.com/cal", accounts);
        Assert.Null(m.AccountId);          // "myinfo@example.com" is NOT account "info@example.com"
        Assert.False(m.Ambiguous);
    }

    [Fact]
    public void Email_bounded_match_still_works_encoded_and_raw()
    {
        var accounts = new[] { Acct("/p/info", "info@example.com", "imap.example.com") };
        var raw = PimAccountMatcher.Match("https://dav/info@example.com/cal", accounts);
        Assert.Equal("/p/info", raw.AccountId);
        Assert.False(raw.Ambiguous);
        var encoded = PimAccountMatcher.Match("https://dav/info%40example.com/cal", accounts);
        Assert.Equal("/p/info", encoded.AccountId);
        Assert.False(encoded.Ambiguous);
    }
}
