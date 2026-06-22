// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class ConvertCommandColourPlanTests
{
    private static string CreateMinimalMbox(string dir)
    {
        string mbox = Path.Combine(dir, "in.mbox");
        File.WriteAllText(mbox, "From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: <a@h>\nSubject: t\n\nbody\n");
        return mbox;
    }

    private static string CreateConfig(string dir, string mbox, string? profilePath = null)
    {
        string cfg = Path.Combine(dir, "config.json");
        string profilePart = profilePath is not null
            ? ",\"profilePath\":" + JsonSerializer.Serialize(profilePath)
            : "";
        File.WriteAllText(cfg,
            "{\"outputs\":[{\"name\":\"Out\",\"sources\":[{\"path\":" +
            JsonSerializer.Serialize(mbox) + ",\"type\":\"mbox\"}]}]" +
            profilePart + "}");
        return cfg;
    }

    [Fact]
    public void Convert_WithProfilePathAndColouredTag_DoneHasColourPlanEntry()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string profileDir = Path.Combine(dir, "profile");
        Directory.CreateDirectory(profileDir);

        // Write a minimal prefs.js with one coloured tag
        File.WriteAllText(Path.Combine(profileDir, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n");

        string mbox = CreateMinimalMbox(dir);
        string cfg = CreateConfig(dir, mbox, profileDir);
        string outDir = Path.Combine(dir, "out");

        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = ConvertCommand.Run(new[] { "--config", cfg, "--output", outDir });
            Assert.Equal(0, exit);

            string done = sw.ToString().Split('\n').First(l => l.Contains("\"type\":\"done\""));
            using var doc = JsonDocument.Parse(done);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("colourPlan", out JsonElement plan), "done must have colourPlan");
            Assert.Equal(JsonValueKind.Array, plan.ValueKind);

            // Find the Important entry
            var entry = plan.EnumerateArray().FirstOrDefault(e =>
                e.TryGetProperty("name", out var n) && n.GetString() == "Important");
            Assert.NotEqual(default, entry);
            Assert.Equal("#FF0000", entry.GetProperty("hex").GetString());
            Assert.Equal("would-add", entry.GetProperty("action").GetString());
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Convert_WithoutProfilePath_DoneHasEmptyColourPlan()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mail2pst-colour-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        string mbox = CreateMinimalMbox(dir);
        string cfg = CreateConfig(dir, mbox, profilePath: null);
        string outDir = Path.Combine(dir, "out");

        var sw = new StringWriter();
        TextWriter original = Console.Out;
        Console.SetOut(sw);
        try
        {
            int exit = ConvertCommand.Run(new[] { "--config", cfg, "--output", outDir });
            Assert.Equal(0, exit);

            string done = sw.ToString().Split('\n').First(l => l.Contains("\"type\":\"done\""));
            using var doc = JsonDocument.Parse(done);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("colourPlan", out JsonElement plan), "done must have colourPlan");
            Assert.Equal(JsonValueKind.Array, plan.ValueKind);
            Assert.Equal(0, plan.GetArrayLength());
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(dir, true);
        }
    }
}
