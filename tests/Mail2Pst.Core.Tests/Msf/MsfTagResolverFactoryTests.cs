// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class MsfTagResolverFactoryTests
{
    private static string NewDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-prof-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void NullPath_ReturnsDefaultResolver()
        => Assert.IsType<DefaultMsfTagResolver>(MsfTagResolverFactory.Create(null));

    [Fact]
    public void DirWithoutPrefsJs_ReturnsDefaultResolver()
    {
        string dir = NewDir();
        try { Assert.IsType<DefaultMsfTagResolver>(MsfTagResolverFactory.Create(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DirWithTagBearingPrefsJs_ReturnsPrefsResolver()
    {
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Critique\");\n");
        try
        {
            IMsfTagResolver r = MsfTagResolverFactory.Create(dir);
            Assert.IsType<PrefsJsTagResolver>(r);
            Assert.Equal(new[] { "Critique" }, r.Resolve(new[] { "$label1" }));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DirWithPrefsJsButNoTagNames_ReturnsDefaultResolver()
    {
        // prefs.js exists but has only a .color pref + unrelated prefs -> empty tag-name map -> fallback.
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n" +
            "user_pref(\"mail.server.server1.check_new_mail\", true);\n");
        try { Assert.IsType<DefaultMsfTagResolver>(MsfTagResolverFactory.Create(dir)); }
        finally { Directory.Delete(dir, true); }
    }
}
