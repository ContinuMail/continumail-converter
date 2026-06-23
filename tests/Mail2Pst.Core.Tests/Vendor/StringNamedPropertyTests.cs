// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Vendor;

public class StringNamedPropertyTests
{
    [Fact]
    public void MultiString_Serializer_RoundTrips_IncludingNonAscii()
    {
        var values = new List<string> { "Work", "Important", "Ældre" };
        byte[] blob = PropertyContext.SerializeMultiString(values);
        Assert.Equal(values, PropertyContext.DeserializeMultiString(blob));
    }

    [Fact]
    public void MultiString_Deserializer_RejectsOversizedCount()
    {
        Assert.Throws<System.IO.InvalidDataException>(
            () => PropertyContext.DeserializeMultiString(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
    }

    [Fact]
    public void Keywords_StringNamedProp_Registers_RoundTrips_AndIsIdempotent()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mail2pst-sp3-" + System.Guid.NewGuid());
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "t.pst");
        PSTFile.CreateEmptyStore(path);
        ushort writtenId;
        var categories = new System.Collections.Generic.List<string> { "Thunderbird", "Important", "Ældre" };
        try
        {
            var file = new PSTFile(path, System.IO.FileAccess.ReadWrite);
            file.BeginSavingChanges();
            PSTFolder folder = file.TopOfPersonalFolders.CreateChildFolder("Inbox", FolderItemTypeName.Note);
            writtenId = PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords");
            // idempotent: second call returns the same id, does not double-register
            Assert.Equal(writtenId, PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords"));
            Note note = Note.CreateNewNote(file, folder.NodeID);
            note.Subject = "probe";
            note.PC.SetMultiStringProperty((PropertyID)writtenId, categories);
            note.SaveChanges();
            folder.AddMessage(note); folder.SaveChanges();
            file.EndSavingChanges(); file.CloseFile();

            var reopened = new PSTFile(path, System.IO.FileAccess.Read);
            try
            {
                ushort? resolved = PropertyNameToIDMap.ResolveStringNamedProperty(reopened, 2, "Keywords");
                Assert.True(resolved.HasValue);
                Assert.Equal(writtenId, resolved!.Value);
                var inbox = (MailFolder)reopened.TopOfPersonalFolders.FindChildFolder("Inbox")!;
                Note readBack = inbox.GetNote(0);
                PropertyContextRecord rec = readBack.PC.GetRecordByPropertyID((PropertyID)resolved.Value);
                Assert.NotNull(rec);
                Assert.Equal(PropertyTypeName.PtypMultiString, rec.wPropType);
                Assert.Equal(categories, PropertyContext.DeserializeMultiString(readBack.PC.GetExternalRecordData(rec)));

                // Lock the corrected hash-bucket index (the spike's key finding): the CRC-keyed string
                // NameID for "Keywords" must sit at (crc ^ ((2<<1)|1)) % bucketCount — NOT the vendored
                // off-by-one (N+wGuid)<<1. A broken bucket write would still pass the stream-scan resolve above.
                PSTNode mapNode = reopened.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);
                byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes("Keywords");
                uint crc = PSTCRCCalculation.ComputeCRC(nameBytes, nameBytes.Length);
                int bucketCount = mapNode.PC.GetInt32Property(PropertyID.PidTagNameidBucketCount)!.Value;
                ushort bucketIndex = (ushort)((crc ^ (uint)((2 << 1) | 1)) % (uint)bucketCount);
                var bucketProp = (PropertyID)((uint)PropertyID.PidTagNameidBucketBase + bucketIndex);
                byte[] bucket = mapNode.PC.GetBytesProperty(bucketProp) ?? System.Array.Empty<byte>();
                bool found = false;
                for (int i = 0; i + NameID.Length <= bucket.Length; i += NameID.Length)
                {
                    var nid = new NameID(bucket, i);
                    if (nid.IsStringIdentifier && nid.dwPropertyID == crc && nid.wGuid == 2 &&
                        nid.wPropIdx == (ushort)(writtenId - 0x8000)) { found = true; break; }
                }
                Assert.True(found, "CRC-keyed \"Keywords\" string NameID must be in the bucket at the corrected index");
            }
            finally { reopened.CloseFile(); }
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
