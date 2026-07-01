// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerCalendarRootTests
{
    [Fact]
    public void Explicit_profile_path_is_used()
    {
        string prof = Path.Combine(Path.GetTempPath(), "prof");
        var cfg = new ConversionConfig { ProfilePath = prof };
        var src = new CalendarSourceConfig { StorePath = Path.Combine("other", "calendar-data", "local.sqlite") };
        Assert.Equal(prof, ConversionRunner.ResolveCalendarAttachmentRoot(src, cfg));
    }

    [Fact]
    public void Store_under_calendar_data_yields_profile_root()
    {
        string prof = Path.Combine(Path.GetTempPath(), "prof");
        var cfg = new ConversionConfig();  // no ProfilePath
        var src = new CalendarSourceConfig { StorePath = Path.Combine(prof, "calendar-data", "local.sqlite") };
        Assert.Equal(prof, ConversionRunner.ResolveCalendarAttachmentRoot(src, cfg));
    }

    [Fact]
    public void Unexpected_store_shape_yields_null_root()
    {
        var cfg = new ConversionConfig();
        var src = new CalendarSourceConfig { StorePath = Path.Combine(Path.GetTempPath(), "weird.sqlite") };
        Assert.Null(ConversionRunner.ResolveCalendarAttachmentRoot(src, cfg));  // → file attachments become link-only
    }
}
