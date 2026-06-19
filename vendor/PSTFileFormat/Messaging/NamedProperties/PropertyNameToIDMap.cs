/* Copyright (C) 2012-2016 ROM Knowledgeware. All rights reserved.
 *
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 *
 * Maintainer: Tal Aloni <tal@kmrom.com>
 */
// ContinuMail modification 2026: added string-named (MNID_STRING) property support
// (GetOrCreateStringNamedProperty / ResolveStringNamedProperty / FillMap extension).
// See vendor/PSTFileFormat-MODIFICATIONS.md for details.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Utilities;

namespace PSTFileFormat
{
    public class PropertyNameToIDMap
    {
        PSTFile m_file;
        public Dictionary<PropertyName, ushort> m_map;
        public byte[] PropertySetGuidStreamCache;

        public PropertyNameToIDMap(PSTFile file)
        {
            m_file = file;
        }

        /// <summary>
        /// Create short PropertyID if not exist
        /// </summary>
        public PropertyID ObtainIDFromName(PropertyName propertyName)
        {
            Nullable<PropertyID> propertyID = GetIDFromName(propertyName);
            if (!propertyID.HasValue)
            {
                return AddToMap(propertyName.PropertyLongID, propertyName.PropertySetGuid);
            }
            else
            {
                return propertyID.Value;
            }
        }

        public Nullable<PropertyID> GetIDFromName(PropertyName propertyName)
        {
            if (m_map == null)
            {
                FillMap();
            }
            ushort result;
            bool success = m_map.TryGetValue(propertyName, out result);
            if (success)
            {
                return (PropertyID)result;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Reverse search, useful only for discovery purposes
        /// </summary>
        public Nullable<PropertyLongID> GetLongIDFromID(ushort propertyID)
        {
            if (m_map == null)
            {
                FillMap();
            }

            foreach (PropertyName propertyName in m_map.Keys)
            {
                if (m_map[propertyName] == propertyID)
                {
                    return propertyName.PropertyLongID;
                }
            }
            return null;
        }


        public void FillMap()
        {
            m_map = new Dictionary<PropertyName, ushort>();
            PSTNode node = m_file.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);
            byte[] buffer = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamEntry);
            if (buffer.Length % 8 > 0)
            {
                throw new InvalidPropertyException("Invalid NameidStreamEntry");
            }

            byte[] stringStream = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamString) ?? Array.Empty<byte>();

            for (int index = 0; index < buffer.Length; index += 8)
            {
                NameID nameID = new NameID(buffer, index);
                if (!nameID.IsStringIdentifier)
                {
                    ushort propertyShortID = nameID.PropertyShortID;
                    PropertyLongID propertyLongID = (PropertyLongID)nameID.dwPropertyID;
                    Guid propertySetGuid = GetPropertySetGuid(nameID.wGuid);

                    m_map.Add(new PropertyName(propertyLongID, propertySetGuid), propertyShortID);
                }
                else
                {
                    // String-named (MNID_STRING): dwPropertyID is offset into the string stream.
                    int off = (int)nameID.dwPropertyID;
                    if (off + 4 > stringStream.Length) continue;
                    int byteLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(stringStream.AsSpan(off));
                    if (off + 4 + byteLen > stringStream.Length) continue;
                    string name = Encoding.Unicode.GetString(stringStream, off + 4, byteLen);
                    Guid propertySetGuid = GetPropertySetGuid(nameID.wGuid);
                    var key = new PropertyName(name, propertySetGuid);
                    if (!m_map.ContainsKey(key))
                        m_map.Add(key, nameID.PropertyShortID);
                }
            }
        }

        /// <returns>short PropertyID used to store the property</returns>
        public PropertyID AddToMap(PropertyLongID propertyLongID, Guid propertySetGuid)
        {
            PSTNode node = m_file.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);

            int wGuid = GetPropertySetGuidIndexHint(propertySetGuid);
            if (wGuid == -1)
            {
                wGuid = 3 + AddPropertySetGuid(node, propertySetGuid);
            }

            byte[] oldBuffer = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamEntry);
            int propertyIndex = oldBuffer.Length / 8;

            NameID nameID = new NameID(propertyLongID, (ushort)wGuid, (ushort)propertyIndex);
            byte[] newBuffer = new byte[oldBuffer.Length + 8];
            Array.Copy(oldBuffer, newBuffer, oldBuffer.Length);
            nameID.WriteBytes(newBuffer, oldBuffer.Length);
            node.PC.SetBytesProperty(PropertyID.PidTagNameidStreamEntry, newBuffer);
            
            AddPropertyToHashBucket(node, nameID);
            node.SaveChanges();

            PropertyID propertyID = (PropertyID)(nameID.PropertyShortID);
            m_map.Add(new PropertyName(propertyLongID, propertySetGuid), (ushort)propertyID);
            return propertyID;
        }
        
        // Note: Changes must be saved by the caller method
        private void AddPropertyToHashBucket(Node node, NameID nameID)
        { 
            int bucketCount = node.PC.GetInt32Property(PropertyID.PidTagNameidBucketCount).Value;
            uint firstBucketPropertyID = (uint)PropertyID.PidTagNameidBucketBase;
            uint arg0 = nameID.dwPropertyID;
            uint arg1 = Convert.ToUInt32(nameID.IdentifierType) + nameID.wGuid << 1;
            ushort bucketIndex = (ushort)((arg0 ^ arg1) % bucketCount);

            if (nameID.IsStringIdentifier)
            {
                throw new NotImplementedException("Named property with string identifier is not supported");
            }
            PropertyID bucketPropertyID = (PropertyID)(firstBucketPropertyID + bucketIndex);
            byte[] oldBuffer = node.PC.GetBytesProperty(bucketPropertyID);
            if (oldBuffer == null)
            {
                oldBuffer = new byte[0];
            }
            byte[] newBuffer = new byte[oldBuffer.Length + NameID.Length];
            Array.Copy(oldBuffer, newBuffer, oldBuffer.Length);
            nameID.WriteBytes(newBuffer, oldBuffer.Length);
            node.PC.SetBytesProperty(bucketPropertyID, newBuffer);
        }

        private Guid GetPropertySetGuid(int indexHint)
        {
            if (indexHint == 0)
            {
                return Guid.Empty;
            }
            else if (indexHint == 1)
            {
                return PropertySetGuid.PS_MAPI;
            }
            else if (indexHint == 2)
            {
                return PropertySetGuid.PS_PUBLIC_STRINGS;
            }
            else
            {
                int propertySetGuidIndex = indexHint - 3;
                if (PropertySetGuidStreamCache == null)
                {
                    PSTNode node = m_file.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);
                    PropertySetGuidStreamCache = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamGuid);
                }

                int offset = propertySetGuidIndex * 16;
                Guid guid = LittleEndianConverter.ToGuid(PropertySetGuidStreamCache, offset);
                return guid;
            }
        }

        private int GetPropertySetGuidIndexHint(Guid propertySetGuid)
        {
            if (propertySetGuid == Guid.Empty)
            {
                return 0;
            }
            else if (propertySetGuid == PropertySetGuid.PS_MAPI)
            {
                return 1;
            }
            else if (propertySetGuid == PropertySetGuid.PS_PUBLIC_STRINGS)
            {
                return 2;
            }
            else
            {
                int guidIndex = GetPropertySetGuidIndex(propertySetGuid);
                if (guidIndex == -1)
                {
                    return -1;
                }
                else
                {
                    return 3 + guidIndex;
                }
            }
        }

        private int GetPropertySetGuidIndex(Guid propertySetGuid)
        {
            PSTNode node = m_file.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);
            byte[] buffer = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamGuid);
            if (buffer.Length % 16 > 0)
            {
                throw new InvalidPropertyException("Invalid NameidStreamGuid");
            }

            for (int index = 0; index < buffer.Length; index += 16)
            {
                Guid guid = LittleEndianConverter.ToGuid(buffer, index);
                if (guid == propertySetGuid)
                {
                    return index / 16;
                }
            }

            return -1;
        }

        // Note: Changes must be saved by the caller method
        /// <returns>Index of property set GUID</returns>
        private int AddPropertySetGuid(Node node, Guid propertySetGuid)
        {
            byte[] oldBuffer = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamGuid);
            byte[] newBuffer = new byte[oldBuffer.Length + 16];
            Array.Copy(oldBuffer, newBuffer, oldBuffer.Length);
            LittleEndianWriter.WriteGuidBytes(newBuffer, oldBuffer.Length, propertySetGuid);
            node.PC.SetBytesProperty(PropertyID.PidTagNameidStreamGuid, newBuffer);

            PropertySetGuidStreamCache = newBuffer; // update cache

            return oldBuffer.Length / 16;
        }

        // -----------------------------------------------------------------------
        // ContinuMail addition 2026: string-named (MNID_STRING) property support.
        // Scope: built-in GUID hints only (guidHint 0-2 = Guid.Empty/PS_MAPI/
        // PS_PUBLIC_STRINGS). Arbitrary set-GUIDs would also require a GUID-stream
        // entry — documented extension point, not implemented here.
        // -----------------------------------------------------------------------

        private static byte[] Concat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Array.Copy(a, 0, r, 0, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }

        /// <summary>
        /// Re-parses the entry + string streams to find the short id assigned to a
        /// string-named property in the given built-in GUID hint bucket.
        /// Returns null if not found.
        /// </summary>
        public static ushort? ResolveStringNamedProperty(PSTFile file, ushort guidHint, string name)
        {
            PSTNode node = file.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);
            byte[] entryStream  = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamEntry)  ?? Array.Empty<byte>();
            byte[] stringStream = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamString) ?? Array.Empty<byte>();

            for (int i = 0; i + NameID.Length <= entryStream.Length; i += NameID.Length)
            {
                var nid = new NameID(entryStream, i);
                if (!nid.IsStringIdentifier || nid.wGuid != guidHint) continue;

                int off = (int)nid.dwPropertyID; // offset into the string stream
                if (off + 4 > stringStream.Length) continue;
                int byteLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(stringStream.AsSpan(off));
                if (off + 4 + byteLen > stringStream.Length) continue;
                string stored = Encoding.Unicode.GetString(stringStream, off + 4, byteLen);
                if (string.Equals(stored, name, StringComparison.Ordinal))
                    return nid.PropertyShortID;
            }
            return null;
        }

        /// <summary>
        /// Idempotent: if a string-named property with <paramref name="name"/> in
        /// <paramref name="guidHint"/> already exists in the Name-to-ID map, returns
        /// its short id. Otherwise registers it (entry stream + name-string stream +
        /// hash bucket at the corrected index), saves the map node, and returns the
        /// new short id (0x8000 + wPropIdx).
        /// </summary>
        /// <remarks>
        /// Hash-bucket index: <c>(crc ^ ((guidHint&lt;&lt;1)|1)) % bucketCount</c>
        /// where crc = CRC32(UTF-16LE name bytes). This differs from the vendored
        /// <c>AddPropertyToHashBucket</c> which computes <c>(N+wGuid)&lt;&lt;1</c>
        /// — an off-by-one for string ids (N=1 instead of N=0). Never call
        /// <c>AddPropertyToHashBucket</c> for string ids; it throws anyway.
        /// </remarks>
        public static ushort GetOrCreateStringNamedProperty(PSTFile file, ushort guidHint, string name)
        {
            // Idempotency: check first without modifying.
            ushort? existing = ResolveStringNamedProperty(file, guidHint, name);
            if (existing.HasValue) return existing.Value;

            PSTNode node = file.GetNode((uint)InternalNodeName.NID_NAME_TO_ID_MAP);

            byte[] entryStream  = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamEntry)  ?? Array.Empty<byte>();
            byte[] stringStream = node.PC.GetBytesProperty(PropertyID.PidTagNameidStreamString) ?? Array.Empty<byte>();

            if (entryStream.Length % NameID.Length != 0)
                throw new InvalidDataException("Corrupt PidTagNameidStreamEntry length");

            ushort wPropIdx = (ushort)(entryStream.Length / NameID.Length);
            uint stringStreamOffset = (uint)stringStream.Length; // 4-aligned by construction

            byte[] nameBytes = Encoding.Unicode.GetBytes(name); // UTF-16LE, no null terminator

            // --- Name-string stream: [DWORD byteLength][UTF-16LE name][pad to 4-byte boundary] ---
            int padded = (nameBytes.Length + 3) & ~3;
            byte[] strEntry = new byte[4 + padded];
            BinaryPrimitives.WriteUInt32LittleEndian(strEntry.AsSpan(0), (uint)nameBytes.Length);
            Array.Copy(nameBytes, 0, strEntry, 4, nameBytes.Length);
            node.PC.SetBytesProperty(PropertyID.PidTagNameidStreamString, Concat(stringStream, strEntry));

            // --- Entry stream: NameID with dwPropertyID = string-stream offset, N=1 (string) ---
            var entryNameId = new NameID((PropertyLongID)stringStreamOffset, guidHint, wPropIdx)
            {
                IdentifierType = true,
            };
            byte[] entryBytes = new byte[NameID.Length];
            entryNameId.WriteBytes(entryBytes, 0);
            node.PC.SetBytesProperty(PropertyID.PidTagNameidStreamEntry, Concat(entryStream, entryBytes));

            // --- Hash bucket: NameID with dwPropertyID = CRC32(name bytes) ---
            // Corrected index: (crc ^ ((guidHint<<1)|1)) % bucketCount.
            // The vendored helper uses (N+wGuid)<<1 which gives the wrong value for N=1
            // (string ids); since it also throws NotImplementedException for string ids,
            // we compute the bucket directly here.
            uint crc = PSTCRCCalculation.ComputeCRC(nameBytes, nameBytes.Length);
            int bucketCount = node.PC.GetInt32Property(PropertyID.PidTagNameidBucketCount)!.Value;
            uint arg1 = (uint)((guidHint << 1) | 1);
            ushort bucketIndex = (ushort)((crc ^ arg1) % (uint)bucketCount);
            var bucketProp = (PropertyID)((uint)PropertyID.PidTagNameidBucketBase + bucketIndex);

            var bucketNameId = new NameID((PropertyLongID)crc, guidHint, wPropIdx)
            {
                IdentifierType = true,
            };
            byte[] bucketEntryBytes = new byte[NameID.Length];
            bucketNameId.WriteBytes(bucketEntryBytes, 0);
            byte[] bucketStream = node.PC.GetBytesProperty(bucketProp) ?? Array.Empty<byte>();
            node.PC.SetBytesProperty(bucketProp, Concat(bucketStream, bucketEntryBytes));

            node.SaveChanges();
            return (ushort)(0x8000 + wPropIdx);
        }
    }
}

