// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class ConvertCommandDoneTests
{
    [Fact]
    public void Convert_DoneJson_IncludesEnrichmentSummary()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-done-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string mbox = Path.Combine(dir, "in.mbox");
        File.WriteAllText(mbox, "From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: <a@h>\nSubject: t\n\nbody\n");
        string cfg = Path.Combine(dir, "config.json");
        File.WriteAllText(cfg,
            "{\"outputs\":[{\"name\":\"Out\",\"sources\":[{\"path\":" +
            JsonSerializer.Serialize(mbox) + ",\"type\":\"mbox\"}]}]}");
        string outDir = Path.Combine(dir, "out");

        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = ConvertCommand.Run(new[] { "--config", cfg, "--output", outDir });
            Assert.Equal(0, exit);
            // JSON-Lines: the single-line done object carries the additive enrichment summary.
            string done = sw.ToString().Split('\n').First(l => l.Contains("\"type\":\"done\""));
            Assert.Contains("\"enrichment\"", done);
            Assert.Contains("\"sourcesAttempted\"", done);
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(dir, true);
        }
    }
}
