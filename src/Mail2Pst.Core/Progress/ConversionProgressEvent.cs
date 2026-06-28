// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
namespace Mail2Pst.Core.Progress;

public abstract record ConversionProgressEvent;

public sealed record ScanEvent(int TotalMessages) : ConversionProgressEvent;

public sealed record ProgressEvent(
    int Converted,
    int TotalMessages,
    int Warnings,
    int Skipped,
    string? CurrentSource = null,
    string? CurrentFolder = null,
    long EstimatedOutputBytes = 0,
    int ContactsConverted = 0,
    int ContactsTotal = 0,
    string Phase = "mail") : ConversionProgressEvent;

public sealed record WarningEvent(
    string Source,
    string Identifier,
    string Reason) : ConversionProgressEvent;
