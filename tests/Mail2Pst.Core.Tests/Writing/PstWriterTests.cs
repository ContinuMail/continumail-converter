// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterTests
{
    [Fact]
    public void WritePlan_CreatesFolderAndWritesMessage()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
            };

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "Test subject",
                        From = new MailAddress { Name = "Alice Example", Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Name = "Bob Example", Email = "bob@example.com" } },
                        TextBody = "Hello Bob",
                        Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                    },
                },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();

            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.Single(outputFiles);
            Assert.True(File.Exists(outputFiles[0]));
            Assert.Equal(1, report.ConvertedCount);
            Assert.Empty(report.Skipped);

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            PSTFolder root = pst.TopOfPersonalFolders;
            var inbox = (MailFolder)root.FindChildFolder("Inbox")!;

            Assert.Equal(1, inbox.MessageCount);
            Note note = inbox.GetNote(0);
            Assert.Equal("Test subject", note.Subject);
            Assert.Equal("Hello Bob", note.Body);
            Assert.Equal("Bob Example", note.DisplayTo);

            pst.CloseFile();
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void WritePlan_EmptySourceMapping_CreatesEmptyFolder()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            // Two mapped sources: one yields a message, the other yields none.
            // The empty source must still appear as an (empty) folder in the PST.
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
                SourceMappings =
                {
                    new SourceMapping { TargetFolderPath = new[] { "Inbox" } },
                    new SourceMapping { TargetFolderPath = new[] { "EmptyLabel" } },
                },
            };

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "Only message",
                        From = new MailAddress { Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Email = "bob@example.com" } },
                        TextBody = "hi",
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                    },
                },
            };

            var writer = new PstWriter();
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, new ConversionReport());

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            try
            {
                PSTFolder root = pst.TopOfPersonalFolders;
                PSTFolder? empty = root.FindChildFolder("EmptyLabel");
                Assert.NotNull(empty);
                Assert.Equal(0, empty!.MessageCount);
                Assert.Equal(1, root.FindChildFolder("Inbox")!.MessageCount);
            }
            finally { pst.CloseFile(); }
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void WritePlan_EmptySourceMapping_IncludeEmptyFoldersFalse_NoEmptyFolder()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            // Same setup as the empty-folder test, but opted out: the empty source
            // must NOT produce a folder; the one with mail still does.
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
                IncludeEmptyFolders = false,
                SourceMappings =
                {
                    new SourceMapping { TargetFolderPath = new[] { "Inbox" } },
                    new SourceMapping { TargetFolderPath = new[] { "EmptyLabel" } },
                },
            };

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "Only message",
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                    },
                },
            };

            List<string> outputFiles = new PstWriter().WritePlan(plan, messages, outputDir, new ConversionReport());

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            try
            {
                PSTFolder root = pst.TopOfPersonalFolders;
                Assert.Null(root.FindChildFolder("EmptyLabel"));
                Assert.Equal(1, root.FindChildFolder("Inbox")!.MessageCount);
            }
            finally { pst.CloseFile(); }
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_OutputNameEscapesDirectory_Throws()
    {
        // Isolated root so any escape attempt lands inside a dir we clean up.
        string root = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        string outputDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outputDir);
        try
        {
            // A traversal name must be rejected, not silently written outside outputDir.
            var plan = new PstOutputPlan { Name = "../escape", MaxSizeBytes = 100L * 1024 * 1024 };
            var writer = new PstWriter();

            Assert.ThrowsAny<Exception>(() =>
                writer.WritePlan(plan, new List<PlannedMessage>(), outputDir, new ConversionReport()));

            // Nothing was written anywhere under the root (i.e. nothing escaped outputDir).
            Assert.Empty(Directory.GetFiles(root, "*.pst", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WritePlan_AutoSplitsWhenExceedingMaxSize()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var plan = new PstOutputPlan
            {
                Name = "Archive",
                MaxSizeBytes = templateSize + 20_000,
            };

            string bigBody = new string('x', 5000);
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 10; i++)
            {
                messages.Add(new PlannedMessage
                {
                    TargetFolderPath = new[] { "Imported Mail" },
                    Message = new MailMessage
                    {
                        Subject = $"Message {i}",
                        From = new MailAddress { Name = "Alice Example", Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Name = "Bob Example", Email = "bob@example.com" } },
                        TextBody = bigBody,
                        Date = DateTimeOffset.UtcNow,
                        Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"message #{i}" },
                    },
                });
            }

            var report = new ConversionReport();
            var writer = new PstWriter(checkIntervalMessages: 2);

            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.True(outputFiles.Count >= 2, $"Expected at least 2 output files, got {outputFiles.Count}");
            Assert.Equal(10, report.ConvertedCount);
            Assert.All(outputFiles, path => Assert.True(File.Exists(path)));

            // Every output file's folder structure must include "Imported Mail"
            foreach (string path in outputFiles)
            {
                var pst = new PSTFile(path, FileAccess.Read);
                PSTFolder root = pst.TopOfPersonalFolders;
                Assert.NotNull(root.FindChildFolder("Imported Mail"));
                pst.CloseFile();
            }
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void WritePlan_SingleFile_NamedWithoutPartSuffix()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Personal", MaxSizeBytes = 100L * 1024 * 1024 };
            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "only",
                        Source = new SourceReference { SourcePath = "s.mbox", Identifier = "#1" },
                    },
                },
            };

            List<string> outputFiles = new PstWriter().WritePlan(plan, messages, outputDir, new ConversionReport());

            Assert.Single(outputFiles);
            Assert.Equal("Personal.pst", Path.GetFileName(outputFiles[0]));
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_MultipleParts_NamedWithPartSuffixes()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 20_000 };
            string bigBody = new string('x', 5000);
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 10; i++)
            {
                messages.Add(new PlannedMessage
                {
                    TargetFolderPath = new[] { "Imported Mail" },
                    Message = new MailMessage
                    {
                        Subject = $"Message {i}",
                        TextBody = bigBody,
                        Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"#{i}" },
                    },
                });
            }

            var writer = new PstWriter(checkIntervalMessages: 2);
            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, new ConversionReport());

            Assert.True(outputFiles.Count >= 2, $"expected a split, got {outputFiles.Count}");
            Assert.Equal("Archive-1.pst", Path.GetFileName(outputFiles[0]));
            Assert.Equal("Archive-2.pst", Path.GetFileName(outputFiles[1]));
            Assert.All(outputFiles, p => Assert.True(File.Exists(p)));
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_HtmlOnlyMessage_PopulatesPlainTextAndHtmlBodies()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
            };

            const string html = "<html><body><p>Hello <b>Bob</b></p></body></html>";

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "HTML only",
                        From = new MailAddress { Name = "Alice Example", Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Name = "Bob Example", Email = "bob@example.com" } },
                        TextBody = null,
                        HtmlBody = html,
                        Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                    },
                },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();

            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.Equal(1, report.ConvertedCount);
            Assert.Empty(report.Skipped);

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            PSTFolder root = pst.TopOfPersonalFolders;
            var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
            Note note = inbox.GetNote(0);

            // Plain-text body must be non-empty and must not contain raw HTML tags.
            Assert.False(string.IsNullOrWhiteSpace(note.Body));
            Assert.DoesNotContain("<p>", note.Body);
            Assert.Contains("Hello", note.Body);
            Assert.Contains("Bob", note.Body);

            // The raw HTML must also be stored, with PidTagNativeBody marking it as the native body.
            byte[] htmlBytes = note.PC.GetBytesProperty(PropertyID.PidTagHtml);
            Assert.NotNull(htmlBytes);
            string storedHtml = System.Text.Encoding.UTF8.GetString(htmlBytes);
            Assert.Equal(html, storedHtml);
            Assert.Equal(3, note.PC.GetInt32Property(PropertyID.PidTagNativeBody));

            pst.CloseFile();
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void WritePlan_MessageWithAttachment_WritesAttachmentData()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
            };

            byte[] attachmentBytes = System.Text.Encoding.UTF8.GetBytes("Hello attachment content");

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "With attachment",
                        From = new MailAddress { Name = "Alice Example", Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Name = "Bob Example", Email = "bob@example.com" } },
                        TextBody = "See attached",
                        Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                        Attachments = new List<MailAttachment>
                        {
                            new()
                            {
                                FileName = "hello.txt",
                                MimeType = "text/plain",
                                Content = AttachmentContent.FromBytes(attachmentBytes),
                            },
                        },
                    },
                },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();

            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.Equal(1, report.ConvertedCount);
            Assert.Empty(report.Skipped);

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            PSTFolder root = pst.TopOfPersonalFolders;
            var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
            Note note = inbox.GetNote(0);

            Assert.Equal(1, note.AttachmentCount);
            AttachmentObject attachment = note.GetAttachmentObject(0);
            Assert.Equal("hello.txt", attachment.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename));
            Assert.Equal("text/plain", attachment.PC.GetStringProperty(PropertyID.PidTagAttachMimeTag));
            Assert.Equal((int)AttachMethod.ByValue, attachment.PC.GetInt32Property(PropertyID.PidTagAttachMethod));
            Assert.Equal(attachmentBytes, attachment.PC.GetBytesProperty(PropertyID.PidTagAttachData));
            Assert.Equal(attachmentBytes.Length, attachment.PC.GetInt32Property(PropertyID.PidTagAttachSize));

            pst.CloseFile();
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void WritePlan_AttachmentWithContentLocation_WritesContentLocation()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
            };

            const string location = "http://example.com/images/logo.png";
            byte[] imageBytes = System.Text.Encoding.UTF8.GetBytes("PNGDATA123");

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "With content-location attachment",
                        From = new MailAddress { Name = "Alice Example", Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Name = "Bob Example", Email = "bob@example.com" } },
                        TextBody = "See attached",
                        Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                        Attachments = new List<MailAttachment>
                        {
                            new()
                            {
                                FileName = "logo.png",
                                MimeType = "image/png",
                                ContentLocation = location,
                                Content = AttachmentContent.FromBytes(imageBytes),
                            },
                        },
                    },
                },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();

            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.Equal(1, report.ConvertedCount);

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            PSTFolder root = pst.TopOfPersonalFolders;
            var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
            Note note = inbox.GetNote(0);

            Assert.Equal(1, note.AttachmentCount);
            AttachmentObject attachment = note.GetAttachmentObject(0);
            Assert.Equal(location, attachment.PC.GetStringProperty(PropertyID.PidTagAttachContentLocation));

            pst.CloseFile();
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void WritePlan_InlineAttachment_SetsContentIdAndFlags()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);

        try
        {
            var plan = new PstOutputPlan
            {
                Name = "Personal",
                MaxSizeBytes = 100L * 1024 * 1024,
            };

            byte[] imageBytes = System.Text.Encoding.UTF8.GetBytes("PNGDATA123");

            var messages = new List<PlannedMessage>
            {
                new()
                {
                    TargetFolderPath = new[] { "Inbox" },
                    Message = new MailMessage
                    {
                        Subject = "With inline image",
                        From = new MailAddress { Name = "Alice Example", Email = "alice@example.com" },
                        To = new List<MailAddress> { new() { Name = "Bob Example", Email = "bob@example.com" } },
                        HtmlBody = "<html><body><p>See image: <img src=\"cid:test-image\"></p></body></html>",
                        Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #1" },
                        Attachments = new List<MailAttachment>
                        {
                            new()
                            {
                                FileName = "pic.png",
                                MimeType = "image/png",
                                ContentId = "test-image",
                                IsInline = true,
                                Content = AttachmentContent.FromBytes(imageBytes),
                            },
                        },
                    },
                },
            };

            var report = new ConversionReport();
            var writer = new PstWriter();

            List<string> outputFiles = writer.WritePlan(plan, messages, outputDir, report);

            Assert.Equal(1, report.ConvertedCount);

            var pst = new PSTFile(outputFiles[0], FileAccess.Read);
            PSTFolder root = pst.TopOfPersonalFolders;
            var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
            Note note = inbox.GetNote(0);

            Assert.Equal(1, note.AttachmentCount);
            AttachmentObject attachment = note.GetAttachmentObject(0);
            Assert.Equal("test-image", attachment.PC.GetStringProperty(PropertyID.PidTagAttachContentId));
            Assert.Equal(4, attachment.PC.GetInt32Property(PropertyID.PidTagAttachFlags));

            pst.CloseFile();
        }
        finally
        {
            Directory.Delete(outputDir, true);
        }
    }
}
