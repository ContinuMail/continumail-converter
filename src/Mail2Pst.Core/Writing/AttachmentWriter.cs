// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Threading;
using Mail2Pst.Core.Models;
using PSTFileFormat;

namespace Mail2Pst.Core.Writing;

public sealed record AttachmentSpec(
    string FileName, string MimeType, AttachmentContent Content,
    string? ContentId = null, string? ContentLocation = null,
    bool IsInline = false, bool IsContactPhoto = false);

/// <summary>Shared by-value attachment writer for mail, contacts, and calendar. Extracted from
/// PstWriter.WriteAttachment (streaming/PERMUTE path) + ContactWriter.WriteContactPhoto —
/// behavior-preserving.</summary>
public sealed class AttachmentWriter
{
    private const int ATT_MHTML_REF = 4;                 // MS-OXCMSG PidTagAttachFlags
    private readonly long _streamingThresholdBytes;
    private readonly bool _streamingDisabled;

    public AttachmentWriter() : this(16L * 1024 * 1024,
        Environment.GetEnvironmentVariable("MAIL2PST_NO_ATTACH_STREAM") is { Length: > 0 }) { }

    public AttachmentWriter(long streamingThresholdBytes, bool streamingDisabled)
    {
        _streamingThresholdBytes = streamingThresholdBytes;
        _streamingDisabled = streamingDisabled;
    }

    public void Write(PSTFile file, MessageObject owner, AttachmentSpec spec, CancellationToken ct = default)
    {
        owner.CreateSubnodeBTreeIfNotExist();
        AttachmentObject att = AttachmentObject.CreateNewAttachmentObject(file, owner.SubnodeBTree);

        att.PC.SetStringProperty(PropertyID.PidTagAttachLongFilename, spec.FileName);
        att.PC.SetStringProperty(PropertyID.PidTagAttachFilename, spec.FileName);
        att.PC.SetStringProperty(PropertyID.PidTagDisplayName, spec.FileName);
        att.PC.SetStringProperty(PropertyID.PidTagAttachExtension, Path.GetExtension(spec.FileName));
        att.PC.SetStringProperty(PropertyID.PidTagAttachMimeTag, spec.MimeType);
        att.PC.SetInt32Property(PropertyID.PidTagAttachMethod, (int)AttachMethod.ByValue);

        // >2 GB guard so PidTagAttachSize's (int) cast below can never overflow. Mail already
        // pre-flights this in PstWriter's measure phase (skips the message); this is the shared-writer
        // backstop for calendar/contact callers that have no such preflight.
        if (spec.Content.Length > int.MaxValue)
            throw new AttachmentTooLargeException(spec.FileName, spec.Content.Length);

        bool canStream = spec.Content.Length >= _streamingThresholdBytes
            && file.Header.bCryptMethod == bCryptMethodName.NDB_CRYPT_PERMUTE
            && !_streamingDisabled;
        if (canStream)
        {
            using Stream s = spec.Content.OpenRead();
            att.PC.SetExternalProperty(PropertyID.PidTagAttachData, PropertyTypeName.PtypBinary,
                s, spec.Content.Length, ct);
        }
        else
        {
            att.PC.SetBytesProperty(PropertyID.PidTagAttachData, spec.Content.ReadAllBytes());
        }
        att.PC.SetInt32Property(PropertyID.PidTagAttachSize, (int)spec.Content.Length);

        if (!string.IsNullOrEmpty(spec.ContentId))
            att.PC.SetStringProperty(PropertyID.PidTagAttachContentId, spec.ContentId);
        if (!string.IsNullOrEmpty(spec.ContentLocation))
            att.PC.SetStringProperty(PropertyID.PidTagAttachContentLocation, spec.ContentLocation);

        if (spec.IsInline)
        {
            att.PC.SetInt32Property(PropertyID.PidTagAttachFlags, ATT_MHTML_REF);
            att.PC.SetBooleanProperty(PropertyID.PidTagAttachmentHidden, true);
        }
        if (spec.IsContactPhoto)
        {
            att.PC.SetBooleanProperty(PropertyID.PidTagAttachmentContactPhoto, true);
            att.PC.SetBooleanProperty(PropertyID.PidTagAttachmentHidden, false);
        }

        att.SaveChanges(owner.SubnodeBTree);
        owner.AddAttachment(att);
    }
}
