// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.Models;

/// <summary>An embedded contact photo: raw image bytes + its media type (e.g. "image/jpeg").</summary>
public class ContactPhoto
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string MediaType { get; set; } = "image/jpeg";
}

/// <summary>Shared contact-photo policy so the mapper (cap enforcement) and the writer/size
/// estimate use ONE constant and never drift.</summary>
public static class ContactPhotoPolicy
{
    public const long MaxContactPhotoBytes = 10L * 1024 * 1024;
}
