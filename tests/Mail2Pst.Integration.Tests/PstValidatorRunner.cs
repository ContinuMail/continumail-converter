// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mail2Pst.Integration.Tests;

public sealed record ValidatedFolder(IReadOnlyList<string> Path, string DisplayPath, long MessageCount);

public sealed record ValidatorResult(
    bool Opened, string File, IReadOnlyList<ValidatedFolder> Folders,
    long TotalMessages, IReadOnlyList<(string Stage, string Message)> Errors);

public sealed class PstValidatorException : Exception
{
    public PstValidatorException(string message) : base(message) { }
}

public static class PstValidatorRunner
{
    /// <summary>Path to the built pst-validate exe, or null when the gate is not configured.</summary>
    public static string? ValidatorPath =>
        Environment.GetEnvironmentVariable("MAIL2PST_PST_VALIDATOR") is { Length: > 0 } p ? p : null;

    public static ValidatorResult Run(string pstPath, TimeSpan timeout)
    {
        string exe = ValidatorPath
            ?? throw new InvalidOperationException("MAIL2PST_PST_VALIDATOR is not set.");

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proc.StartInfo.ArgumentList.Add(pstPath);
        proc.Start();

        // Read both streams concurrently BEFORE waiting: a full stderr pipe can't deadlock a
        // blocking stdout read, and the timeout actually bounds a hung/looping validator (a
        // synchronous ReadToEnd would block past the timeout if the child never exits).
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new PstValidatorException($"pst-validate timed out after {timeout.TotalSeconds:0}s on '{pstPath}'.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        return Interpret(stdout, stderr, proc.ExitCode);
    }

    // Pure, unit-testable contract enforcement (no process). Parses exactly one JSON object from
    // stdout and checks exit-code/JSON consistency. internal: same assembly as the tests.
    internal static ValidatorResult Interpret(string stdout, string stderr, int exitCode)
    {
        string trimmed = stdout.Trim();
        if (trimmed.Length == 0)
            throw new PstValidatorException($"pst-validate produced no stdout. stderr: {stderr}");

        // JsonDocument.Parse reads ONE document and throws on trailing non-whitespace, so a second
        // JSON object (e.g. "{}{}") is rejected here — no manual multi-object scan needed.
        JsonDocument doc;
        try { doc = JsonDocument.Parse(trimmed); }
        catch (JsonException ex)
        {
            throw new PstValidatorException(
                $"pst-validate stdout was not exactly one JSON object: {ex.Message}\nstdout: {trimmed}\nstderr: {stderr}");
        }
        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new PstValidatorException($"pst-validate stdout must be a JSON object.\nstdout: {trimmed}");

            var folders = root.GetProperty("folders").EnumerateArray().Select(f =>
                new ValidatedFolder(
                    f.GetProperty("path").EnumerateArray().Select(s => s.GetString() ?? "").ToList(),
                    f.GetProperty("displayPath").GetString() ?? "",
                    f.GetProperty("messageCount").GetInt64())).ToList();
            var errors = root.GetProperty("errors").EnumerateArray().Select(e =>
                (e.GetProperty("stage").GetString() ?? "", e.GetProperty("message").GetString() ?? "")).ToList();
            bool opened = root.GetProperty("opened").GetBoolean();

            // Contract: exit 0 IFF (opened && no errors). Any mismatch is a broken validator.
            bool jsonSaysOk = opened && errors.Count == 0;
            if (exitCode == 0 && !jsonSaysOk)
                throw new PstValidatorException(
                    $"pst-validate exited 0 but JSON reports failure (opened={opened}, errors={errors.Count}).\nstderr: {stderr}");
            if (exitCode != 0 && jsonSaysOk)
                throw new PstValidatorException(
                    $"pst-validate exited {exitCode} but JSON reports success.\nstderr: {stderr}");

            return new ValidatorResult(
                opened,
                root.GetProperty("file").GetString() ?? "",
                folders,
                root.GetProperty("totalMessages").GetInt64(),
                errors);
        }
    }
}
