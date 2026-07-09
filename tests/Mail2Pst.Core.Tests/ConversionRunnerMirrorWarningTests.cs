// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests;

public class ConversionRunnerMirrorWarningTests
{
    [Fact]
    public void Run_MirrorCollision_RecordsPlanningWarning_InReportAndAsEventAfterScan()
    {
        // Two real mbox files whose stems collapse to the same mirror folder ("Project"). The
        // disambiguation warning must reach report.Warnings AND be emitted as a WarningEvent — and,
        // per the CLI contract, the WarningEvent must come AFTER the ScanEvent.
        const string Msg = "From a@b Mon Jan  1 00:00:00 2020\r\nSubject: x\r\n\r\nbody\r\n";
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-mirror-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string v1 = Path.Combine(dir, "Project.v1");
        string v2 = Path.Combine(dir, "Project.v2");
        File.WriteAllText(v1, Msg, new UTF8Encoding(false));
        File.WriteAllText(v2, Msg, new UTF8Encoding(false));
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new() { Name = "Out", FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig>
                        {
                            new() { Path = v1, Type = "mbox" },
                            new() { Path = v2, Type = "mbox" },
                        } },
                },
            };
            var events = new List<ConversionProgressEvent>();
            ConversionReport report = new ConversionRunner().Run(config, dir, events.Add);

            Assert.Contains(report.Warnings, w => w.Reason.Contains("Project (2)", StringComparison.OrdinalIgnoreCase));
            int scanIdx = events.FindIndex(e => e is ScanEvent);
            int warnIdx = events.FindIndex(e => e is WarningEvent we && we.Reason.Contains("Project (2)", StringComparison.OrdinalIgnoreCase));
            Assert.True(scanIdx >= 0 && warnIdx > scanIdx, "the mapping WarningEvent must come after the ScanEvent");
        }
        finally { Directory.Delete(dir, true); }
    }
}
