/* Copyright (C) 2012-2016 ROM Knowledgeware. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 * 
 * Maintainer: Tal Aloni <tal@kmrom.com>
 */
using System;
using System.Collections.Generic;
using System.Text;
using Utilities;

namespace PSTFileFormat
{
    public class PropertyContext : BTreeOnHeap<PropertyContextRecord>
    {
        private SubnodeBTree m_subnodeBTree;

        public PropertyContext(HeapOnNode heap, SubnodeBTree subnodeBTree) : base(heap)
        {
            m_subnodeBTree = subnodeBTree;
        }

        public void SaveChanges(NodeID pstNodeID)
        {
            SaveChanges();
            this.File.NodeBTree.UpdateNodeEntry(pstNodeID, this.DataTree, m_subnodeBTree);
        }

        /// <summary>
        /// The caller must update its reference to point to the new data tree and subnode B-tree
        /// </summary>
        public override void SaveChanges()
        {
            base.SaveChanges();
            if (m_subnodeBTree != null)
            {
                m_subnodeBTree.SaveChanges();
            }
        }

        public PropertyContextRecord GetRecordByPropertyID(PropertyID propertyID)
        {
            byte[] key = LittleEndianConverter.GetBytes((ushort)propertyID);
            PropertyContextRecord record = FindRecord(key);
            return record;
        }

        public byte[] GetExternalRecordData(PropertyContextRecord record)
        {
            if (record.IsExternal)
            {
                return NodeStorageHelper.GetExternalPropertyBytes(this.Heap, m_subnodeBTree, record.HeapOrNodeID);
            }
            else
            {
                throw new ArgumentException("Not an external record");
            }
        }

        public void RemoveProperty(PropertyID propertyID)
        {
            PropertyContextRecord oldRecord = GetRecordByPropertyID(propertyID);
            if (oldRecord != null)
            {
                if (oldRecord.IsExternal)
                {
                    NodeStorageHelper.RemoveExternalProperty(this.Heap, m_subnodeBTree, oldRecord.HeapOrNodeID);
                }
                this.RemoveRecord(oldRecord.Key);
            }
        }

        #region Get Property
        public bool GetBooleanProperty(PropertyID propertyID, bool defaultValue)
        {
            Nullable<bool> result = GetBooleanProperty(propertyID);
            if (result.HasValue)
            {
                return result.Value;
            }
            return defaultValue;
        }

        public Nullable<bool> GetBooleanProperty(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypBoolean)
                {
                    return (record.dwValueHnid != 0);
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public short GetInt16Property(PropertyID propertyID, short defaultValue)
        {
            Nullable<short> result = GetInt16Property(propertyID);
            if (result.HasValue)
            {
                return result.Value;
            }
            return defaultValue;
        }

        public Nullable<short> GetInt16Property(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypInteger16)
                {
                    return (short)record.dwValueHnid;
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public int GetInt32Property(PropertyID propertyID, int defaultValue)
        {
            Nullable<int> result = GetInt32Property(propertyID);
            if (result.HasValue)
            {
                return result.Value;
            }
            return defaultValue;
        }

        public Nullable<int> GetInt32Property(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypInteger32)
                {
                    return (int)record.dwValueHnid;
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public Nullable<long> GetInt64Property(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypInteger64)
                {
                    if (!record.HeapOrNodeID.IsEmpty)
                    {
                        byte[] longBytes = this.GetHeapItem(record.HeapID);
                        long result = LittleEndianConverter.ToInt64(longBytes, 0);
                        return result;
                    }
                    else
                    {
                        // The hid is set to zero if the data item is empty
                        return null;
                    }
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public Nullable<float> GetFloat32Property(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypFloating32)
                {
                    byte[] bytes = LittleEndianConverter.GetBytes(record.dwValueHnid);
                    return LittleEndianConverter.ToFloat32(bytes, 0);
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public Nullable<double> GetFloat64Property(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypFloating64)
                {
                    if (!record.HeapOrNodeID.IsEmpty)
                    {
                        byte[] floatBytes = this.GetHeapItem(record.HeapID);
                        double result = LittleEndianConverter.ToFloat64(floatBytes, 0);
                        return result;
                    }
                    else
                    {
                        // The hid is set to zero if the data item is empty
                        return null;
                    }
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public DateTime GetDateTimeProperty(PropertyID propertyID, DateTime defaultValue)
        {
            Nullable<DateTime> result = GetDateTimeProperty(propertyID);
            if (result.HasValue)
            {
                return result.Value;
            }
            return defaultValue;
        }

        public Nullable<DateTime> GetDateTimeProperty(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypTime)
                {
                    if (!record.HeapOrNodeID.IsEmpty)
                    {
                        byte[] dateTimeBytes = this.GetHeapItem(record.HeapID);
                        long temp = LittleEndianConverter.ToInt64(dateTimeBytes, 0);
                        DateTime result = DateTime.FromFileTimeUtc(temp);
                        return result;
                    }
                    else
                    {
                        // The hid is set to zero if the data item is empty
                        return null;
                    }
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public string GetStringProperty(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypString)
                {
                    byte[] stringBytes = GetExternalRecordData(record);
                    string result = Encoding.Unicode.GetString(stringBytes);
                    return result;
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public byte[] GetBytesProperty(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypBinary)
                {
                    return GetExternalRecordData(record);
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public PtypObjectRecord GetObjectRecordProperty(PropertyID propertyID)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType == PropertyTypeName.PtypObject)
                {
                    if (record.IsExternal)
                    {
                        if (record.IsHeapID)
                        {
                            byte[] buffer = this.GetHeapItem(record.HeapID);
                            PtypObjectRecord objRecord = new PtypObjectRecord(buffer);
                            return objRecord;
                        }
                        else
                        {
                            throw new InvalidPropertyException("Unexpected object record");
                        }
                    }
                    else
                    {
                        // this should never happen
                        throw new InvalidPropertyException("Unexpected record data type found");
                    }
                }
                else
                {
                    throw new InvalidPropertyException("Unexpected PC data type found");
                }
            }
            else
            {
                return null;
            }
        }

        public Subnode GetObjectProperty(PropertyID propertyID)
        {
            PtypObjectRecord objRecord = GetObjectRecordProperty(propertyID);
            Subnode subnode = m_subnodeBTree.GetSubnode(objRecord.Nid);
            return subnode;
        }
        #endregion

        #region Set Property
        public void SetInternalProperty(PropertyID propertyID, PropertyTypeName propertyType, uint propertyValue)
        {
            PropertyContextRecord oldRecord = GetRecordByPropertyID(propertyID);
            if (oldRecord == null)
            {
                PropertyContextRecord record = new PropertyContextRecord();
                record.wPropId = propertyID;
                record.wPropType = propertyType;
                record.dwValueHnid = propertyValue;
                this.AddRecord(record);
            }
            else
            {
                oldRecord.dwValueHnid = propertyValue;
                this.UpdateRecord(oldRecord);
            }
        }

        public void SetExternalProperty(PropertyID propertyID, PropertyTypeName propertyType, byte[] propertyBytes)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                if (record.wPropType != propertyType)
                {
                    throw new InvalidPropertyException("Property type mismatch");
                }

                if (record.IsExternal)
                {
                    HeapOrNodeID newHeapOrNodeID = NodeStorageHelper.StoreExternalProperty(this.File, this.Heap, ref m_subnodeBTree, record.HeapOrNodeID, propertyBytes);
                    if (record.HeapOrNodeID.Value != newHeapOrNodeID.Value)
                    {
                        record.HeapOrNodeID = newHeapOrNodeID;
                        UpdateRecord(record);
                    }
                }
                else
                {
                    // old record is not external but new record is, this should never happen.
                    throw new InvalidPropertyException("Old record should be external but is not");
                }
            }
            else // old record does not exist
            {
                record = new PropertyContextRecord();
                record.HeapOrNodeID = NodeStorageHelper.StoreExternalProperty(this.File, this.Heap, ref m_subnodeBTree, propertyBytes);
                record.wPropId = propertyID;
                record.wPropType = propertyType;
                AddRecord(record);
            }
        }

        /// <summary>
        /// INSERT-only stream overload. Allocates a new external storage entry for
        /// <paramref name="propertyID"/> by routing to
        /// <see cref="NodeStorageHelper.StoreExternalProperty(PSTFile,HeapOnNode,ref SubnodeBTree,System.IO.Stream,long,System.Threading.CancellationToken)"/>.
        /// The stream is consumed and closed synchronously before this method returns [A10].
        /// If a property record for <paramref name="propertyID"/> already exists,
        /// <see cref="System.NotSupportedException"/> is thrown — UPDATE is not supported via this
        /// overload [R2:H1].
        /// </summary>
        public void SetExternalProperty(PropertyID propertyID, PropertyTypeName propertyType, System.IO.Stream stream, long length, System.Threading.CancellationToken cancellationToken)
        {
            PropertyContextRecord record = GetRecordByPropertyID(propertyID);
            if (record != null)
            {
                throw new System.NotSupportedException("streaming external property write is INSERT-only");
            }
            else // old record does not exist
            {
                record = new PropertyContextRecord();
                record.HeapOrNodeID = NodeStorageHelper.StoreExternalProperty(this.File, this.Heap, ref m_subnodeBTree, stream, length, cancellationToken);
                record.wPropId = propertyID;
                record.wPropType = propertyType;
                AddRecord(record);
            }
        }

        public void SetBooleanProperty(PropertyID propertyID, bool value)
        {
            SetInternalProperty(propertyID, PropertyTypeName.PtypBoolean, Convert.ToByte(value));
        }

        public void SetInt16Property(PropertyID propertyID, short value)
        {
            SetInternalProperty(propertyID, PropertyTypeName.PtypInteger16, (ushort)value);
        }

        public void SetInt32Property(PropertyID propertyID, int value)
        {
            SetInternalProperty(propertyID, PropertyTypeName.PtypInteger32, (uint)value);
        }

        public void SetDateTimeProperty(PropertyID propertyID, DateTime value)
        {
            // We write as UTC to avoid conversion when using ToFileTimeUtc()
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
            byte[] propertyBytes = LittleEndianConverter.GetBytes(value.ToFileTimeUtc());
            SetExternalProperty(propertyID, PropertyTypeName.PtypTime, propertyBytes);
        }

        public void SetStringProperty(PropertyID propertyID, string value)
        {
            byte[] propertyBytes = UnicodeEncoding.Unicode.GetBytes(value);
            SetExternalProperty(propertyID, PropertyTypeName.PtypString, propertyBytes);
        }

        public void SetBytesProperty(PropertyID propertyID, byte[] value)
        {
            SetExternalProperty(propertyID, PropertyTypeName.PtypBinary, value);
        }

        public void SetObjectProperty(PropertyID propertyID, NodeID subnodeID, int size)
        {
            PtypObjectRecord objRecord = new PtypObjectRecord(subnodeID, (uint)size);
            byte[] propertyBytes = objRecord.GetBytes();

            SetExternalProperty(propertyID, PropertyTypeName.PtypObject, propertyBytes);
        }

        public void SetMultiStringProperty(PropertyID id, System.Collections.Generic.IReadOnlyList<string> values)
            => SetExternalProperty(id, PropertyTypeName.PtypMultiString, SerializeMultiString(values));

        public static byte[] SerializeMultiString(System.Collections.Generic.IReadOnlyList<string> values)
        {
            int count = values.Count;
            byte[][] items = new byte[count][];
            for (int i = 0; i < count; i++) items[i] = System.Text.Encoding.Unicode.GetBytes(values[i]);
            int headerLen = 4 + 4 * count;
            int total = headerLen; foreach (var b in items) total += b.Length;
            byte[] buf = new byte[total];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), (uint)count);
            int offset = headerLen;
            for (int i = 0; i < count; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4 + 4 * i), (uint)offset);
                offset += items[i].Length;
            }
            offset = headerLen;
            foreach (byte[] item in items) { System.Array.Copy(item, 0, buf, offset, item.Length); offset += item.Length; }
            return buf;
        }

        public static System.Collections.Generic.List<string> DeserializeMultiString(byte[] buffer)
        {
            if (buffer.Length < 4)
                throw new System.IO.InvalidDataException("MV-Unicode buffer too short to contain count.");
            uint rawCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0));
            if (rawCount > (uint)(buffer.Length - 4) / 4)   // also guards headerLen overflow
                throw new System.IO.InvalidDataException("MV-Unicode buffer too short to contain all offset entries.");
            int count = (int)rawCount;
            int headerLen = 4 + 4 * count;
            int[] offsets = new int[count];
            for (int i = 0; i < count; i++)
            {
                offsets[i] = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4 + 4 * i));
                if (offsets[i] < headerLen || offsets[i] > buffer.Length)
                    throw new System.IO.InvalidDataException("MV-Unicode offset is out of range.");
                if (i > 0 && offsets[i] < offsets[i - 1])
                    throw new System.IO.InvalidDataException("MV-Unicode offsets are not non-decreasing.");
            }
            var result = new System.Collections.Generic.List<string>(count);
            for (int i = 0; i < count; i++)
            {
                int start = offsets[i];
                int end = (i + 1 < count) ? offsets[i + 1] : buffer.Length;
                result.Add(System.Text.Encoding.Unicode.GetString(buffer, start, end - start));
            }
            return result;
        }

        #endregion

        public List<PropertyContextRecord> GetAllProperties()
        {
            List<PropertyContextRecord> result = new List<PropertyContextRecord>();
            if (BTreeHeader.bIdxLevels == 0)
            {
                byte[] leafBytes = GetHeapItem(BTreeHeader.hidRoot);
                int offset = 0;

                while (offset < leafBytes.Length)
                {
                    PropertyContextRecord record = new PropertyContextRecord(leafBytes, offset);
                    result.Add(record);
                    offset += record.RecordLength;
                }
            }
            return result;
        }

        public int GetTotalLengthOfAllProperties()
        {
            int result = 0;
            List<PropertyContextRecord> properties = GetAllProperties();
            foreach (PropertyContextRecord record in properties)
            {
                if (record.wPropId == PropertyID.MessageTotalAttachmentSize)
                {
                    // Note: We do not count this property
                }
                else if (record.IsExternal && record.wPropType != PropertyTypeName.PtypObject)
                {
                    // Note: We do not count the length of PtypObjectRecord
                    // The object length itself will be counted seperately
                    result += GetExternalRecordData(record).Length;
                }
                else if (record.wPropType == PropertyTypeName.PtypBoolean)
                {
                    result += 1;
                }
                else if (record.wPropType == PropertyTypeName.PtypInteger16)
                {
                    result += 2;
                }
                else if (record.wPropType == PropertyTypeName.PtypInteger32)
                {
                    result += 4;
                }
                else if (record.wPropType == PropertyTypeName.PtypTime)
                {
                    result += 8;
                }
            }
            return result;
        }

        /// <summary>
        /// For discovery purposes
        /// </summary>
        public List<string> ListAllProperties()
        {
            List<string> result = new List<string>();
            List<PropertyContextRecord> properties = GetAllProperties();
            foreach (PropertyContextRecord record in properties)
            {
                string value;
                switch (record.wPropType)
                {
                    case PropertyTypeName.PtypBoolean:
                        value = GetBooleanProperty(record.wPropId).ToString();
                        break;

                    case PropertyTypeName.PtypInteger16:
                        value = GetInt16Property(record.wPropId).ToString() + " (Int16)";
                        break;
                    case PropertyTypeName.PtypInteger32:
                        value = GetInt32Property(record.wPropId).ToString();
                        break;
                    case PropertyTypeName.PtypInteger64:
                        value = GetInt64Property(record.wPropId).ToString() + " (Int64)";
                        break;
                    case PropertyTypeName.PtypFloating32:
                        value = GetFloat32Property(record.wPropId).ToString() + " (Float32)";
                        break;
                    case PropertyTypeName.PtypFloating64:
                        value = GetFloat64Property(record.wPropId).ToString() + " (Float64)";
                        break;
                    case PropertyTypeName.PtypTime:
                        value = GetDateTimeProperty(record.wPropId).ToString();
                        break;
                    case PropertyTypeName.PtypString:
                        value = GetStringProperty(record.wPropId);
                        break;
                    case PropertyTypeName.PtypMultiString:
                        value = "Unsupported type: MultiString";
                        break;
                    case PropertyTypeName.PtypBinary:
                        {
                            try
                            {
                                byte[] bytes = GetBytesProperty((PropertyID)record.wPropId);
                                value = StringHelper.GetByteArrayString(bytes);
                            }
                            catch(MissingSubnodeException)
                            {
                                value = "Missing (Binary)";
                            }
                            break;
                        }
                    case PropertyTypeName.PtypObject:
                        {
                            PropertyContext pc = GetObjectProperty((PropertyID)record.wPropId).PC;
                            value = "Subnode PC:\r\n";
                            if (pc != null)
                            {
                                List<string> subProperies = pc.ListAllProperties();
                                foreach (string line in subProperies)
                                {
                                    value += "\t" + line + "\r\n";
                                }
                            }
                            break;
                        }
                    default:
                        value = "unknown type: 0x" + record.wPropType.ToString("x");
                        break;
                }

                string propertyIDString = GetPropertyIDString((ushort)record.wPropId);
                result.Add(propertyIDString + ": " + value);

            }

            return result;
        }

        public virtual string GetPropertyIDString(ushort propertyID)
        {
            if (Enum.IsDefined(typeof(PropertyID), propertyID))
            {
                return ((PropertyID)propertyID).ToString();
            }
            else
            {
                return "0x" + propertyID.ToString("x");
            }
        }

        public SubnodeBTree SubnodeBTree
        {
            get
            {
                return m_subnodeBTree;
            }
        }

        public static PropertyContext CreateNewPropertyContext(PSTFile file)
        {
            return CreateNewPropertyContext(file, null);
        }

        /// <param name="subnodeBTree">Subnode BTree that will be associated with the new PC</param>
        public static PropertyContext CreateNewPropertyContext(PSTFile file, SubnodeBTree subnodeBTree)
        {
            HeapOnNode heap = HeapOnNode.CreateNewHeap(file);
            
            BTreeOnHeapHeader bTreeHeader = new BTreeOnHeapHeader();
            bTreeHeader.cbKey = PropertyContextRecord.RecordKeyLength;
            bTreeHeader.cbEnt = PropertyContextRecord.RecordDataLength;

            HeapID newUserRoot = heap.AddItemToHeap(bTreeHeader.GetBytes());
            // The heap header may have just been updated
            HeapOnNodeHeader header = heap.HeapHeader;
            header.bClientSig = OnHeapTypeName.bTypePC;
            header.hidUserRoot = newUserRoot;
            heap.UpdateHeapHeader(header);
            heap.FlushToDataTree();

            return new PropertyContext(heap, subnodeBTree);
        }
    }
}
