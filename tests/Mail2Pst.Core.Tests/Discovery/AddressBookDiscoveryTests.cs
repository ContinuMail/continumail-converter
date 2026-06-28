// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class AddressBookDiscoveryTests
{
    [Fact]
    public void Discover_FindsSqliteAndMabBooks()
    {
        string profile = Path.Combine(Path.GetTempPath(), $"m2p-prof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profile);
        try
        {
            File.WriteAllText(Path.Combine(profile, "abook.sqlite"), "");
            File.WriteAllText(Path.Combine(profile, "history.sqlite"), "");
            File.WriteAllText(Path.Combine(profile, "abook-1.mab"), "");

            var books = MailProfileDiscovery.DiscoverAddressBooks(profile).ToList();

            Assert.Contains(books, b => b.Path.EndsWith("abook.sqlite") && b.Format == "thunderbird-sqlite");
            Assert.Contains(books, b => b.Path.EndsWith("abook-1.mab") && b.Format == "thunderbird-mab");
        }
        finally { Directory.Delete(profile, true); }
    }
}
