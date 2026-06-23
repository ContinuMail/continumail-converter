// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;

namespace Mail2Pst.Core;

/// <summary>
/// Ties together config -> mapping plan -> parsing -> PST writing -> report
/// for a full conversion run.
/// </summary>
public class ConversionRunner
{
    private readonly PstWriter _writer;

    public ConversionRunner(int checkIntervalMessages = 500, int progressIntervalMessages = 25)
    {
        _writer = new PstWriter(checkIntervalMessages, progressIntervalMessages);
    }

    public ConversionReport Run(ConversionConfig config, string outputDirectory, Action<ConversionProgressEvent>? onProgress = null, CancellationToken cancellationToken = default)
    {
        // Validate the config up front (output names, duplicates, sizes, sources)
        // so problems fail loudly before any output file is created.
        ConfigValidator.Validate(config);

        var report = new ConversionReport();
        List<PstOutputPlan> plans = MappingEngine.BuildPlan(config);

        var enrichmentOptions = new MsfEnrichmentOptions
        {
            TagResolver = MsfTagResolverFactory.Create(config.ProfilePath),
            JunkHandling = config.JunkHandling,
            DropExpunged = config.DropExpunged,
        };

        // Validate source types eagerly so an unsupported type fails loudly
        // before any output file is created, rather than mid-write.
        foreach (PstOutputPlan plan in plans)
        {
            foreach (SourceMapping mapping in plan.SourceMappings)
            {
                ParserRegistry.Get(mapping.Source.Type);
            }
        }

        int total = 0;
        if (onProgress is not null)
        {
            foreach (PstOutputPlan plan in plans)
            {
                foreach (SourceMapping mapping in plan.SourceMappings)
                {
                    try
                    {
                        total += ParserRegistry.Get(mapping.Source.Type).CountMessages(mapping.Source.Path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // best-effort: missing/unreadable source contributes 0 to the total
                    }
                }
            }
            onProgress(new ScanEvent(total));
        }

        try
        {
            foreach (PstOutputPlan plan in plans)
            {
                IEnumerable<PlannedMessage> plannedMessages = EnumeratePlannedMessages(plan, report, enrichmentOptions, onProgress);
                List<string> outputFiles = _writer.WritePlan(plan, plannedMessages, outputDirectory, report, total, onProgress, cancellationToken);
                report.AddOutputFiles(outputFiles);
            }

            // Post-conversion smoke test: prove every written PST opens and holds exactly the
            // messages we counted, before this run can be reported as `done`. A failure throws
            // InvalidDataException (not OperationCanceledException), so it is NOT caught below —
            // it propagates out of Run to the CLI's fatal error path. Runs only here, on the
            // fully-successful path: cancellation throws out of the loop above and never reaches
            // this line, and the writer's own fatal aborts have already propagated.
            PstOutputVerifier.Verify(report.OutputFiles, report.ConvertedCount);
        }
        catch (OperationCanceledException)
        {
            // The writer already deleted the in-progress part and recorded both the
            // deletion and any surviving completed parts on the report.
            report.MarkCancelled();
        }

        return report;
    }

    private static IEnumerable<PlannedMessage> EnumeratePlannedMessages(
        PstOutputPlan plan, ConversionReport report, MsfEnrichmentOptions enrichmentOptions,
        Action<ConversionProgressEvent>? onProgress = null)
    {
        foreach (SourceMapping mapping in plan.SourceMappings)
        {
            // Build optional .msf enrichment for this source (records attempted/degraded + any warning,
            // emitting a live WarningEvent through onProgress on degradation).
            SourceEnrichmentContext? enrichment =
                SourceEnrichmentContext.TryCreate(mapping.Source, enrichmentOptions, report, onProgress);

            // finally (not the natural foreach end) folds the per-message counts, so they survive early
            // disposal of this iterator (cancellation / split-cap stop / producer fault in the writer).
            try
            {
                IMailSourceParser parser = ParserRegistry.Get(mapping.Source.Type);
                using IEnumerator<ParseResult> results = parser.Parse(mapping.Source.Path).GetEnumerator();

                while (true)
                {
                    ParseResult result;
                    try
                    {
                        if (!results.MoveNext())
                        {
                            break;
                        }

                        result = results.Current;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        report.RecordSkipped(
                            new SourceReference { SourcePath = mapping.Source.Path, Identifier = "(source)" },
                            ex.Message);
                        break;
                    }

                    if (!result.Success)
                    {
                        report.RecordSkipped(result.Source, result.Error!);
                        continue;
                    }

                    foreach (string warning in result.Warnings)
                    {
                        report.RecordWarning(result.Source, warning);
                        onProgress?.Invoke(new WarningEvent(result.Source.SourcePath, result.Source.Identifier, warning));
                    }

                    MailMessage message = result.Message!;
                    bool keep = enrichment?.Apply(message) ?? true;
                    if (!keep)
                    {
                        // Expunged-dropped: never yielded, so it never reaches the writer's finally
                        // (PstWriter.WritePlan) that disposes attachment streams per dequeued message —
                        // dispose here in the producer so the dropped message's streams don't leak.
                        foreach (MailAttachment attachment in message.Attachments)
                            attachment.Content.Dispose();
                        continue;
                    }

                    // Route junk into a top-level "Junk Email" folder when JunkHandling.Folder.
                    // Evaluated AFTER enrichment, which is what makes message.IsJunk authoritative.
                    IReadOnlyList<string> targetPath = JunkRouting.ResolveTargetFolderPath(
                        mapping.TargetFolderPath, message.IsJunk, enrichmentOptions.JunkHandling);

                    yield return new PlannedMessage
                    {
                        Message = message,
                        TargetFolderPath = targetPath,
                    };
                }
            }
            finally
            {
                if (enrichment is not null)
                {
                    report.RecordEnrichmentCounts(enrichment.Result);
                }
            }
        }
    }
}
