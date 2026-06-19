// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.Msf;

/// <summary>
/// Thunderbird message flags bitfield, mirroring Mozilla's <c>nsMsgMessageFlags.h</c>.
/// <see cref="LabelsMask"/> is named for completeness only; SP2 does not interpret it
/// into a label (see the SP2 design's non-reconciliation rule).
/// </summary>
[Flags]
public enum MsfMessageFlags : uint
{
    None       = 0u,
    Read       = 0x00000001u,
    Replied    = 0x00000002u,
    Marked     = 0x00000004u,
    Expunged   = 0x00000008u,
    HasRe      = 0x00000010u,
    Offline    = 0x00000080u,
    Forwarded  = 0x00001000u,
    New        = 0x00010000u,
    LabelsMask = 0x0E000000u,
    Attachment = 0x10000000u,
}
