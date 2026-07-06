// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.OutlookCategories;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class CategoryFaiBakeTests
{
    // A profile whose prefs.js gives one mail tag a colour. No tagged messages are needed — the
    // mail-tag colour comes from the tag DEFINITION in prefs.js.
    private static string WriteProfileWithColouredTag(string root)
    {
        string profile = Path.Combine(root, "profile");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n");
        return profile;
    }

    private static ConversionConfig MailOnlyConfig(string profilePath, string groupName = "Personal", int maxSizeMB = 100)
    {
        string mbox = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");
        return new ConversionConfig
        {
            ProfilePath = profilePath,
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = groupName,
                    MaxSizeMB = maxSizeMB,
                    FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig> { new() { Type = "mbox", Path = mbox } },
                },
            },
        };
    }

    [Fact]
    public void Mail_only_conversion_with_coloured_tag_bakes_fai_into_a_created_calendar_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            new ConversionRunner().Run(MailOnlyConfig(WriteProfileWithColouredTag(root)), outDir);
            string pst = Directory.GetFiles(outDir, "*.pst")[0];

            var file = new PSTFile(pst, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            PSTFolder calendar = file.TopOfPersonalFolders.FindChildFolder("Calendar");
            Assert.NotNull(calendar);                                   // created for the FAI (mail-only store)
            Assert.Equal(1, calendar!.AssociatedMessageCount);
            MessageObject fai = calendar.GetAssociatedMessage(0);
            Assert.Equal(CategoryListFaiWriter.CategoryListMessageClass,
                fai.PC.GetStringProperty(PropertyID.PidTagMessageClass));
            Assert.Contains("name=\"Important\"",
                Encoding.UTF8.GetString(fai.PC.GetBytesProperty(PropertyID.PidTagRoamingXmlStream)));
            file.CloseFile();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
