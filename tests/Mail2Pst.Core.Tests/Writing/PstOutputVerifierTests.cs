// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstOutputVerifierTests
{

    // Writes `count` messages (across two folders) into a fresh PST in `outputDir` and
    // returns the output file list plus the report's converted count.
    private static (List<string> outputs, int converted) WriteMessages(string outputDir, int count)
    {
        var plan = new PstOutputPlan { Name = "Personal", MaxSizeBytes = 100L * 1024 * 1024 };
        var messages = new List<PlannedMessage>();
        for (int i = 0; i < count; i++)
        {
            messages.Add(new PlannedMessage
            {
                TargetFolderPath = new[] { i % 2 == 0 ? "Inbox" : "Archive" },
                Message = new MailMessage
                {
                    Subject = $"Message {i}",
                    From = new MailAddress { Name = "Alice", Email = "alice@example.com" },
                    To = new List<MailAddress> { new() { Name = "Bob", Email = "bob@example.com" } },
                    TextBody = $"Body {i}",
                    Date = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                    Source = new SourceReference { SourcePath = "Inbox.mbox", Identifier = $"message #{i}" },
                },
            });
        }
        var report = new ConversionReport();
        var writer = new PstWriter();
        List<string> outputs = writer.WritePlan(plan, messages, outputDir, report);
        return (outputs, report.ConvertedCount);
    }

    [Fact]
    public void Verify_CountMatches_DoesNotThrow()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            (List<string> outputs, int converted) = WriteMessages(dir, 3);
            Assert.Equal(3, converted);
            PstOutputVerifier.Verify(outputs, converted); // must not throw
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_CountMismatch_ThrowsWithExpectedAndFound()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            (List<string> outputs, int converted) = WriteMessages(dir, 3);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => PstOutputVerifier.Verify(outputs, converted + 1));
            Assert.Contains("expected 4", ex.Message);
            Assert.Contains("found 3", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_MissingFile_ThrowsNamingFileWithInner()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string missing = Path.Combine(dir, "missing.pst"); // never created
            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => PstOutputVerifier.Verify(new[] { missing }, 0));
            Assert.Contains(missing, ex.Message);
            Assert.NotNull(ex.InnerException);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_CorruptFile_ThrowsNamingFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string garbage = Path.Combine(dir, "garbage.pst");
            File.WriteAllText(garbage, "this is not a PST file");
            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => PstOutputVerifier.Verify(new[] { garbage }, 0));
            Assert.Contains(garbage, ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_EmptyConversion_DoesNotThrow()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            (List<string> outputs, int converted) = WriteMessages(dir, 0);
            Assert.Equal(0, converted);
            PstOutputVerifier.Verify(outputs, converted); // 0 == 0, must not throw
        }
        finally { Directory.Delete(dir, true); }
    }
}
