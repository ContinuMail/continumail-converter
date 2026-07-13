// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using MimeKit;

namespace Mail2Pst.Core.Reverse;

/// <summary>
/// Seam implemented by Plan 4 (<c>MimeReconstructor</c>): turns one read-back <see cref="PstMailMessage"/>
/// into a MimeKit <see cref="MimeMessage"/> (identity/structural headers + body tree + attachments). Plan 3's
/// <c>MboxTreeWriter</c> consumes this to serialize each message into the mbox tree; it does NOT reconstruct
/// MIME itself. Implementations MUST NOT emit <c>X-Mozilla-*</c> headers — the writer owns those (it writes
/// them as the first header lines so their position/format stay under the writer's control).
/// </summary>
public interface IMimeReconstructor
{
    MimeMessage Reconstruct(PstMailMessage message);
}
