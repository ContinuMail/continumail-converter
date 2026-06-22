// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Tests that redirect the process-global Console.Out (via Console.SetOut) to capture CLI stdout
/// must not run in parallel with each other or other collections, or the global redirect races.
/// </summary>
[CollectionDefinition("ConsoleCapture", DisableParallelization = true)]
public sealed class ConsoleCaptureCollection { }
