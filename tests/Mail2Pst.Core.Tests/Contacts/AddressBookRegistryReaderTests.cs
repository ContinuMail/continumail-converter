// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Contacts;
using Xunit;

public class AddressBookRegistryReaderTests
{
    [Fact]
    public void Maps_filename_to_carddav_url()
    {
        string prefs =
            "user_pref(\"ldap_2.servers.foo.filename\", \"abook-1.sqlite\");\n" +
            "user_pref(\"ldap_2.servers.foo.carddav.url\", \"https://dav/aksel%40example.com/\");\n";
        var map = AddressBookRegistryReader.ParseText(prefs);
        Assert.Equal("https://dav/aksel%40example.com/", map["abook-1.sqlite"]);
    }

    [Fact]
    public void Server_without_carddav_url_is_not_mapped()
    {
        string prefs = "user_pref(\"ldap_2.servers.pab.filename\", \"abook.sqlite\");\n";
        var map = AddressBookRegistryReader.ParseText(prefs);
        Assert.False(map.ContainsKey("abook.sqlite"));
    }

    [Fact]
    public void Tolerates_whitespace_and_ignores_unrelated_keys()
    {
        string prefs =
            "  user_pref( \"ldap_2.servers.bar.filename\" , \"abook-2.sqlite\" ) ;\n" +
            "user_pref(\"ldap_2.servers.bar.carddav.url\",\"https://dav/x/\");\n" +
            "user_pref(\"ldap_2.servers.bar.description\", \"Work\");\n" +
            "user_pref(\"mail.something.else\", \"nope\");\n";
        var map = AddressBookRegistryReader.ParseText(prefs);
        Assert.Single(map);
        Assert.Equal("https://dav/x/", map["abook-2.sqlite"]);
    }

    [Fact]
    public void Filename_is_reduced_to_basename()
    {
        string prefs =
            "user_pref(\"ldap_2.servers.baz.filename\", \"sub/dir/abook-3.sqlite\");\n" +
            "user_pref(\"ldap_2.servers.baz.carddav.url\", \"https://dav/y/\");\n";
        var map = AddressBookRegistryReader.ParseText(prefs);
        Assert.Equal("https://dav/y/", map["abook-3.sqlite"]);
    }
}
