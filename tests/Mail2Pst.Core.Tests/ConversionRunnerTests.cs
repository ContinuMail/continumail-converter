// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Reporting;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerTests
{
    [Fact]
    public void Run_ConvertsMboxFixtureIntoPst()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Path = fixturePath, Type = "mbox" },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir);

            Assert.Equal(2, report.ConvertedCount);
            Assert.Empty(report.Skipped);

            string outputPath = Path.Combine(outputDir, "Personal.pst");
            Assert.True(File.Exists(outputPath));

            var pst = new PSTFile(outputPath, FileAccess.Read);
            PSTFolder root = pst.TopOfPersonalFolders;
            var folder = (MailFolder)root.FindChildFolder("sample")!;
            Assert.Equal(2, folder.MessageCount);
            pst.CloseFile();
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_AttachmentFreeFixture_HasNoWarnings()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            string mboxPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");

            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Type = "mbox", Path = mboxPath },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir);

            Assert.Equal(2, report.ConvertedCount);
            Assert.Empty(report.Warnings);
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_MessageWithAttachments_ConvertsWithoutWarnings()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            string mboxPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "mbox-with-attachments.mbox");

            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Type = "mbox", Path = mboxPath },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir);

            Assert.Equal(6, report.ConvertedCount);
            Assert.Empty(report.Skipped);
            Assert.Empty(report.Warnings);
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_MessageWithAttachmentWarning_WarningAppearsInReport()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            // This fixture contains a single message whose attachment part has
            // no content body, which causes MimeMessageMapper.ExtractAttachments to
            // record a non-fatal warning while still parsing the message. The
            // assertion below exercises the ConversionRunner seam that copies
            // each ParseResult.Warnings entry into report.Warnings.
            string mboxPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "mbox-with-broken-attachment.mbox");

            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Type = "mbox", Path = mboxPath },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir);

            // The message itself still converts successfully; only the broken
            // attachment is dropped and surfaced as a warning.
            Assert.Equal(1, report.ConvertedCount);
            Assert.NotEmpty(report.Warnings);
            Assert.Contains(report.Warnings, w => w.Reason.Contains("broken.bin"));
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_MissingSourceFile_RecordsSkipInsteadOfThrowing()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Path = Path.Combine(outputDir, "missing.mbox"), Type = "mbox" },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir);

            Assert.Equal(0, report.ConvertedCount);
            Assert.Single(report.Skipped);
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_UnreadableSourceFile_RecordsSkipInsteadOfThrowing()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            // A directory path (rather than a file) makes File.OpenRead throw
            // UnauthorizedAccessException — the same shape as a permission-denied
            // source. The run must skip it (matching the count's best-effort catch),
            // not crash the whole conversion.
            string unreadablePath = Path.Combine(outputDir, "a-directory.mbox");
            Directory.CreateDirectory(unreadablePath);

            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Path = unreadablePath, Type = "mbox" },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir);

            Assert.Equal(0, report.ConvertedCount);
            Assert.Single(report.Skipped);
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_PopulatesOutputFilesOnReport()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            string mboxPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig> { new() { Type = "mbox", Path = mboxPath } },
                    },
                },
            };

            ConversionReport report = new ConversionRunner().Run(config, outputDir);

            Assert.Single(report.OutputFiles);
            Assert.EndsWith("Personal.pst", report.OutputFiles[0]);
            Assert.True(File.Exists(report.OutputFiles[0]));
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void Run_UnsupportedSourceType_ThrowsBeforeWritingAnyOutput()
    {

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal",
                        MaxSizeMB = 100,
                        FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Path = Path.Combine(outputDir, "unused.eml"), Type = "eml" },
                        },
                    },
                },
            };

            var runner = new ConversionRunner();

            Assert.Throws<NotSupportedException>(() => runner.Run(config, outputDir));
            Assert.False(File.Exists(Path.Combine(outputDir, "Personal.pst")));
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void Run_PreCancelledToken_ReturnsCancelledReportWithNoOutput()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");

        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig> { new() { Type = "mbox", Path = fixturePath } },
                    },
                },
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var runner = new ConversionRunner();
            ConversionReport report = runner.Run(config, outputDir, onProgress: null, cancellationToken: cts.Token);

            Assert.True(report.Cancelled);
            Assert.Empty(report.DeletedFiles);
            Assert.Empty(report.OutputFiles);
            Assert.False(File.Exists(Path.Combine(outputDir, "Personal.pst")));
        }
        finally { Directory.Delete(outputDir, true); }
    }
}
