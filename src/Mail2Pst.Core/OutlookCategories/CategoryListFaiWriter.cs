// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using PSTFileFormat;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Writes the Outlook master category list into a PST's Calendar folder as the
/// <c>IPM.Configuration.CategoryList</c> associated (FAI) message, carrying the master-list XML in
/// <c>PidTagRoamingXmlStream</c> (MS-OXOCFG §2.2.5.1.1). Classic Outlook reads this FAI from the
/// user's PRIMARY/default store only, so the baked FAI renders categories in colour when the
/// converted PST is opened (or imported into) as that primary store, with no Outlook automation;
/// a PST attached as a secondary data file shows the category names but not their colours (Outlook
/// does not consult a secondary store's own FAI). Idempotent: a second stamp updates the existing
/// FAI's stream rather than appending a duplicate.
/// </summary>
public static class CategoryListFaiWriter
{
    public const string CategoryListMessageClass = "IPM.Configuration.CategoryList";

    // Outlook locates the config message by subject; PidTagSubject is derived from prefix + normalized
    // subject, so set the normalized subject (0x0E1D) too. 0x0E1D is not in the vendored enum — cast.
    private const PropertyID PidTagNormalizedSubject = (PropertyID)0x0E1D;

    /// <summary>Upsert the CategoryList FAI carrying <paramref name="xmlBytes"/> into
    /// <paramref name="calendarFolder"/>'s associated contents table. Caller owns the open/save
    /// lifecycle (must be inside BeginSavingChanges).</summary>
    public static void Stamp(PSTFile file, PSTFolder calendarFolder, byte[] xmlBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(calendarFolder);
        ArgumentNullException.ThrowIfNull(xmlBytes);

        // Upsert: if a CategoryList FAI already exists (matched by BOTH class and subject — Outlook
        // locates it by subject, so a message with the class but a different subject is not ours),
        // update its stream + mod-time in place rather than appending a duplicate.
        for (int i = 0; i < calendarFolder.AssociatedMessageCount; i++)
        {
            MessageObject existing = calendarFolder.GetAssociatedMessage(i);
            if (existing is not null
                && string.Equals(existing.PC.GetStringProperty(PropertyID.PidTagMessageClass),
                       CategoryListMessageClass, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.PC.GetStringProperty(PropertyID.PidTagSubject),
                       CategoryListMessageClass, StringComparison.OrdinalIgnoreCase))
            {
                existing.PC.SetBytesProperty(PropertyID.PidTagRoamingXmlStream, xmlBytes);
                existing.PC.SetDateTimeProperty(PropertyID.PidTagLastModificationTime, DateTime.UtcNow);
                existing.SaveChanges();
                return;
            }
        }

        // Insert: create a fresh config message. CreateNewMessage seeds the required PC properties
        // (flags/status/creation+mod time/search key); we override class/subject/stream. The
        // MSGFLAG_ASSOCIATED flag is guaranteed by AddAssociatedMessage, so we do not set it here.
        MessageObject fai = MessageObject.CreateNewMessage(file, FolderItemTypeName.Note, calendarFolder.NodeID);
        fai.PC.SetStringProperty(PropertyID.PidTagMessageClass, CategoryListMessageClass);
        fai.PC.SetStringProperty(PropertyID.PidTagSubject, CategoryListMessageClass);
        fai.PC.SetStringProperty(PidTagNormalizedSubject, CategoryListMessageClass);
        fai.PC.SetBytesProperty(PropertyID.PidTagRoamingXmlStream, xmlBytes);
        fai.SaveChanges();

        calendarFolder.AddAssociatedMessage(fai);
    }
}
