// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Contacts;
using Xunit;

namespace Mail2Pst.Core.Tests.Contacts;

public class MorkAddressBookReaderTests
{
    [Fact]
    public void Read_MabFixture_ReturnsContacts()
    {
        var book = new AddressBook
        {
            DisplayName = "Legacy",
            Path = "Contacts/fixtures/sample-abook.mab",
            Format = AddressBookFormat.ThunderbirdMab,
        };
        var results = new MorkAddressBookReader().Read(book).ToList();
        Assert.True(results.Count >= 2);
        Assert.Contains(results, r => r.Success && r.Contact!.Emails.Count > 0);
    }

    [Fact]
    public void Read_TruncatedMabThrowingMorkFormatException_ReturnsSingleFailedBook_DoesNotThrow()
    {
        // A .mab truncated mid-construct: the tokenizer throws MorkFormatException
        // ("Unterminated '<' construct at end of input"). Before the fix this escaped
        // Read() and aborted the whole conversion; now it must degrade to one failed book.
        string path = Path.Combine(Path.GetTempPath(), $"m2p-trunc-{Guid.NewGuid():N}.mab");
        File.WriteAllText(path, "< (80=ns:msg:db:row:scope:cards:all)(81=DisplayName");
        try
        {
            var book = new AddressBook
            {
                DisplayName = "abook.mab",
                Path = path,
                Format = AddressBookFormat.ThunderbirdMab,
            };

            var results = new MorkAddressBookReader().Read(book).ToList();

            var failed = Assert.Single(results);
            Assert.False(failed.Success);
            Assert.Contains("abook.mab", failed.Source);
            // A degraded book must carry a usable reason, not a blank error.
            Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        }
        finally { File.Delete(path); }
    }
}
