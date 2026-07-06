// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Mail2Pst.Core.Cli;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Cli;

internal static class ImportColoursCommand
{
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ColdVerifyCap = TimeSpan.FromSeconds(60);

    internal static int Run(string[] args)
    {
        ImportColoursInput input = ImportColoursInput.Parse(args);
        if (input.Error is not null)
        {
            Console.Error.WriteLine($"{input.Error}");
            Console.Error.WriteLine("Usage: continumail-convert import-colours --profile <thunderbird-profile-dir> [--apply] [--outlook-profile <name>]");
            Console.Error.WriteLine("       continumail-convert import-colours --plan-file <path> [--apply] [--outlook-profile <name>]");
            return 1;
        }

        IReadOnlyList<CategoryCandidate> plan;
        if (input.PlanFile is not null)
        {
            try
            {
                plan = LoadPlanFromFile(input.PlanFile);
            }
            catch (Exception ex)
            {
                CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours", message = $"Could not load plan file: {ex.Message}", fatal = true });
                Console.Error.WriteLine($"import-colours failed: {ex.Message}");
                return 1;
            }
        }
        else
        {
            string prefsPath = Path.Combine(input.ProfilePath!, "prefs.js");
            string content = File.Exists(prefsPath) ? File.ReadAllText(prefsPath) : string.Empty;
            plan = CategoryColorPlan.Build(PrefsTagReader.ParseText(content), PrefsTagReader.ParseColors(content));
        }

        if (!input.Apply)
        {
            Emit("preview", outlookAvailable: OutlookDetection.ClassicOutlookAvailable(), plan);
            return 0;
        }

        if (!OperatingSystem.IsWindows() || !OutlookDetection.ClassicOutlookAvailable())
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours",
                message = "Outlook is required for --apply; preview works without it.", fatal = true });
            Console.Error.WriteLine("Outlook is required for --apply.");
            return 1;
        }

        OutlookProfileInfo profileInfo = OutlookProfileRegistry.Read(new WindowsRegistryKeyReader());
        ProfileResolution target = OutlookProfileResolver.Resolve(input.OutlookProfile, profileInfo);
        if (target.Name is null)
        {
            string msg = target.ErrorCode switch
            {
                "unknown-outlook-profile" => $"Outlook profile '{input.OutlookProfile}' not found. Available: {string.Join(", ", profileInfo.Profiles)}.",
                "no-outlook-profile" => "No Outlook profile exists. Create one with 'outlook-profiles create --name ContinuMail' or open Outlook once.",
                _ => $"Multiple Outlook profiles and no usable default — pass --outlook-profile. Available: {string.Join(", ", profileInfo.Profiles)}.",
            };
            CliArgs.WriteJsonLine(new { type = "error", stage = target.ErrorCode, message = msg, fatal = true });
            Console.Error.WriteLine(msg);
            return 1;
        }

        // Outer state carried across ColourApplyCoordinator's attempt(s): the durable "what we've ever
        // expected to have added" set (a retry's own results can under-report — see coldVerify below),
        // the transient Outlook PIDs we most recently started (for the KB-004 shutdown guard), and the
        // last successful attempt's candidate list (for the success emit).
        var expectedDurable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastStartedPids = new List<int>();
        IReadOnlyList<CategoryCandidate> appliedResults = Array.Empty<CategoryCandidate>();

        bool Attempt() => Sta.Run(() =>
        {
            OutlookComCategoryStore store;
            try
            {
                store = TransientComRetry.Run(
                    () => new OutlookComCategoryStore(target.Name),
                    ComErrorClassifier.IsTransientOpen,
                    maxAttempts: 3,
                    sleep: i => Thread.Sleep(TimeSpan.FromMilliseconds(500 * i)));
            }
            catch (Exception ex) when (ComErrorClassifier.IsTransientOpen(ex))
            {
                // ONLY a transient "store not ready" failure that exhausted TransientComRetry's attempts
                // becomes OutlookStoreNotReadyException (stage outlook-store-not-ready). Every other
                // store-open failure propagates with its ORIGINAL type so the command's existing catches
                // handle it: interactive-logon → outlook-profile-logon-failed, anything else → the generic
                // catch → import-colours. (Until Task 0 seeds a real transient HRESULT into
                // ComErrorClassifier this branch is unreachable — correct: there is no store-not-ready
                // condition to detect yet.)
                throw new OutlookStoreNotReadyException(ex.Message, ex);
            }

            using (store)
            {
                IReadOnlyList<CategoryCandidate> results = CategoryColorApplier.Apply(plan, store);
                appliedResults = results;
                CategoryVerify.MergeAdded(expectedDurable, results);
                store.Commit(CategoryVerify.ExpectedAdded(results)); // in-session read-back verifies this attempt's adds
                store.Shutdown();
                lastStartedPids = store.StartedPids.ToList();
                return store.CleanExit;
            }
        }, ApplyTimeout);

        bool ColdVerify()
        {
            ColdVerifyOutcome outcome = OutlookCategoryVerifier.Verify(target.Name!, expectedDurable, ColdVerifyCap);
            if (outcome.VerifierFailed) throw new ColourVerifierFailedException();
            return outcome.AllPresent;
        }

        void EnsureNoTrackedProcess()
        {
            if (!ProcessCleanup.WaitUntilGone(lastStartedPids, IsOutlookPidAlive, TimeSpan.FromSeconds(30), i => Thread.Sleep(250)))
                throw new OutlookProcessCleanupTimeoutException();
        }

        try
        {
            ColourApplyResult result = ColourApplyCoordinator.Run(Attempt, ColdVerify, EnsureNoTrackedProcess);
            if (!result.Success)
            {
                string failMsg = "Colour import could not be verified as persisted after a retry — Outlook state is uncertain; re-run import-colours --apply once Outlook is fully closed.";
                CliArgs.WriteJsonLine(new { type = "error", stage = result.FailureStage ?? "colour-apply-unverified", message = failMsg, fatal = true });
                Console.Error.WriteLine(failMsg);
                return 1;
            }

            EmitApplySuccess(appliedResults, result);
            return 0;
        }
        catch (TimeoutException)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours",
                message = "Outlook did not respond (a security prompt may be open). Dismiss it / use a trusted context and re-run.",
                fatal = true });
            Console.Error.WriteLine("Outlook timed out — dismiss any Outlook security prompt and re-run.");
            return 1;
        }
        catch (Exception ex) when (OutlookDetection.LooksLikeInteractiveLogonRequired(ex))
        {
            string msg = $"Outlook profile '{target.Name}' requires an interactive login and cannot be coloured headlessly — open Outlook once with this profile, choose another profile, or create a ContinuMail viewing profile.";
            CliArgs.WriteJsonLine(new { type = "error", stage = "outlook-profile-logon-failed", message = msg, fatal = true });
            Console.Error.WriteLine(msg);
            return 1;
        }
        catch (Exception ex) when (ex is OutlookStoreNotReadyException or ColourReadbackException
            or ColourVerifierFailedException or OutlookProcessCleanupTimeoutException)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = ColourApplyStages.FromException(ex), message = ex.Message, fatal = true });
            Console.Error.WriteLine($"import-colours failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours", message = ex.Message, fatal = true });
            Console.Error.WriteLine($"import-colours failed: {ex.Message}");
            return 1;
        }
    }

    // KB-004 process-liveness probe for ProcessCleanup.WaitUntilGone: alive iff the PID still resolves to
    // an OUTLOOK.EXE process; any failure (already exited, access denied, ...) is treated as "gone" — never
    // throw out of this probe.
    private static bool IsOutlookPidAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.ProcessName == "OUTLOOK";
        }
        catch
        {
            return false;
        }
    }

    // Reads a colour plan JSON array (shape: [{name,hex,outlookColor,action}]) and normalises
    // the action field so a would-add with no colour is safely downgraded to skipped-no-colour.
    internal static IReadOnlyList<CategoryCandidate> LoadPlanFromFile(string path)
    {
        string json = File.ReadAllText(path);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw new FormatException("Plan file must be a JSON array.");

        var result = new List<CategoryCandidate>();
        foreach (JsonElement el in root.EnumerateArray())
        {
            string name = el.GetProperty("name").GetString() ?? string.Empty;
            string? hex = el.TryGetProperty("hex", out JsonElement hexEl) ? hexEl.GetString() : null;
            int? outlookColor = el.TryGetProperty("outlookColor", out JsonElement ocEl) && ocEl.ValueKind == JsonValueKind.Number
                ? ocEl.GetInt32()
                : null;
            string? rawAction = el.TryGetProperty("action", out JsonElement actEl) ? actEl.GetString() : null;

            // Safe normalisation:
            // - action present → use it, but demote would-add with null colour
            // - action absent + colour present → would-add
            // - action absent + colour absent → skipped-no-colour
            string action;
            if (rawAction is not null)
            {
                action = (rawAction == "would-add" && outlookColor is null) ? "skipped-no-colour" : rawAction;
            }
            else
            {
                action = outlookColor is not null ? "would-add" : "skipped-no-colour";
            }

            result.Add(new CategoryCandidate(name, hex, outlookColor, action));
        }
        return result;
    }

    private static void Emit(string mode, bool outlookAvailable, IReadOnlyList<CategoryCandidate> categories)
    {
        // NOTE: do NOT set schemaVersion here — CliEventSerializer.Serialize injects it (verified: it does
        // `node["schemaVersion"] = SchemaVersion`). Matches DiscoverCommand, which also omits it.
        var output = new
        {
            type = "importColours",
            mode,
            outlookAvailable,
            categories = categories.Select(c => new { name = c.Name, hex = c.Hex, outlookColor = c.OutlookColor, action = c.Action }),
        };
        Console.WriteLine(CliEventSerializer.Serialize(output, indented: true));
    }

    // Apply-path success emit: the base "apply" shape plus additive verify-then-close diagnostics.
    // shutdownClean:false with an otherwise-successful result means Outlook had to be force-killed but a
    // cold read-back proved the write persisted anyway — never a fatal condition, just visibility.
    private static void EmitApplySuccess(IReadOnlyList<CategoryCandidate> categories, ColourApplyResult result)
    {
        var output = new
        {
            type = "importColours",
            mode = "apply",
            outlookAvailable = true,
            categories = categories.Select(c => new { name = c.Name, hex = c.Hex, outlookColor = c.OutlookColor, action = c.Action }),
            shutdownClean = result.ShutdownClean,
            coldVerifyAttempted = result.ColdVerifyAttempted,
            coldVerified = result.ColdVerified,
            retryCount = result.RetryCount,
        };
        Console.WriteLine(CliEventSerializer.Serialize(output, indented: true));
    }
}
