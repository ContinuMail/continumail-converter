// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Thrown when a single attachment's decoded content exceeds what a PST attachment can
/// represent (PidTagAttachSize is a PT_LONG, signed 32-bit). The message carrying it is
/// recorded as a per-message skip rather than aborting the whole conversion — see
/// <see cref="PstWriter.IsRecoverableWriteError"/>.
/// </summary>
public sealed class AttachmentTooLargeException : Exception
{
    public string FileName { get; }
    public long ContentLength { get; }

    public AttachmentTooLargeException(string fileName, long contentLength)
        : base($"Attachment '{fileName}' is {contentLength} bytes, which exceeds the maximum " +
               $"size a PST attachment can store ({PstWriter.MaxAttachmentBytes} bytes).")
    {
        FileName = fileName;
        ContentLength = contentLength;
    }
}
