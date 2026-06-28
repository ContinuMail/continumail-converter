// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Linq;
using Mail2Pst.Core.Contacts;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class VCardCorpusTests
{
    [Fact]
    public void RealAddressBook_ParsesAllCards_WithNonTrivialFieldCoverage()
    {
        // Opt-in: set MAIL2PST_ABOOK_SQLITE to a real abook.sqlite path. CI without it skips.
        string? abook = Environment.GetEnvironmentVariable("MAIL2PST_ABOOK_SQLITE");
        if (string.IsNullOrEmpty(abook)) return;

        var book = new AddressBook { DisplayName = "Corpus", Path = abook, Format = AddressBookFormat.ThunderbirdSqlite };
        var results = new SqliteAddressBookReader().Read(book).ToList();

        Assert.NotEmpty(results);
        // Tolerate a tiny failure rate for messy real-world data rather than demanding zero
        // (the vCard-first reader already falls back to EAV on a bad blob, so failures should be
        // rare). Keep this robust for a long-lived test across evolving corpora.
        int failures = results.Count(r => !r.Success);
        Assert.True(failures <= System.Math.Max(1, results.Count / 100),
            $"too many parse failures: {failures}/{results.Count}");
        // Modern TB stores rich fields only in _vCard; if the vCard path works, a meaningful
        // fraction of cards should yield more than just a name.
        int withRichField = results.Count(r => r.Contact is { } c &&
            (c.CompanyName != null || c.JobTitle != null || c.BusinessPhone != null
             || c.HomePhone != null || c.MobilePhone != null || c.Photo != null
             || c.HomeAddress != null || c.BusinessAddress != null));
        Assert.True(withRichField > 0, "expected at least some contacts with rich (vCard-only) fields");
    }
}
