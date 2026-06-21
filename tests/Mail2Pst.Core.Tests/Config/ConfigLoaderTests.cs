// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class ConfigLoaderTests
{
    private const string SampleJson = """
    {
      "outputs": [
        {
          "name": "Personal",
          "maxSizeMB": 100,
          "folderMapping": "mirror",
          "sources": [
            { "path": "extracted/Inbox.mbox", "type": "mbox" },
            { "path": "extracted/Sent.mbox", "type": "mbox", "targetFolder": "Sent Items" }
          ]
        },
        {
          "name": "Archive",
          "folderMapping": "flatten",
          "sources": [
            { "path": "extracted/old.mbox", "type": "mbox" }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Load_ParsesOutputsAndSources()
    {
        string tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, SampleJson);

        try
        {
            ConversionConfig config = ConfigLoader.Load(tempPath);

            Assert.Equal(2, config.Outputs.Count);

            OutputGroupConfig personal = config.Outputs[0];
            Assert.Equal("Personal", personal.Name);
            Assert.Equal(100, personal.MaxSizeMB);
            Assert.Equal(FolderMappingMode.Mirror, personal.FolderMapping);
            Assert.Equal(2, personal.Sources.Count);
            Assert.Equal("extracted/Inbox.mbox", personal.Sources[0].Path);
            Assert.Equal("mbox", personal.Sources[0].Type);
            Assert.Null(personal.Sources[0].TargetFolder);
            Assert.Equal("Sent Items", personal.Sources[1].TargetFolder);

            OutputGroupConfig archive = config.Outputs[1];
            Assert.Equal(FolderMappingMode.Flatten, archive.FolderMapping);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Load_OutputGroup_DefaultsMaxSizeMBTo20000()
    {
        string tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, SampleJson);

        try
        {
            ConversionConfig config = ConfigLoader.Load(tempPath);

            Assert.Equal(20000, config.Outputs[1].MaxSizeMB);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Load_ParsesTargetFolderPath()
    {
        string json = """
        { "outputs": [ { "name": "Out", "maxSizeMB": 100, "folderMapping": "mirror",
          "sources": [ { "path": "a.mbox", "type": "mbox", "targetFolderPath": ["Accounts", "Work", "Sent"] } ] } ] }
        """;
        string tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, json);
        try
        {
            ConversionConfig config = ConfigLoader.Load(tempPath);
            Assert.Equal(new[] { "Accounts", "Work", "Sent" }, config.Outputs[0].Sources[0].TargetFolderPath);
        }
        finally { File.Delete(tempPath); }
    }

    [Fact]
    public void Load_ParsesJunkHandlingFolder()
    {
        string json = """
        { "junkHandling": "Folder", "outputs": [ { "name": "Out", "maxSizeMB": 100,
          "folderMapping": "mirror", "sources": [ { "path": "a.mbox", "type": "mbox" } ] } ] }
        """;
        string tempPath = Path.GetTempFileName();
        File.WriteAllText(tempPath, json);
        try
        {
            ConversionConfig config = ConfigLoader.Load(tempPath);
            Assert.Equal(JunkHandlingMode.Folder, config.JunkHandling);
        }
        finally { File.Delete(tempPath); }
    }
}
