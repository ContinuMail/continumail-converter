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
using System.IO;
using System.Text;
using Utilities;

namespace PSTFileFormat
{
    public class NodeStorageHelper
    {
        public static byte[] GetExternalPropertyBytes(HeapOnNode heap, SubnodeBTree subnodeBTree, HeapOrNodeID heapOrNodeID)
        {
            if (heapOrNodeID.IsEmpty)
            {
                return new byte[0];
            }
            else if (heapOrNodeID.IsHeapID)
            {
                byte[] result = heap.GetHeapItem(heapOrNodeID.HeapID);
                return result;
            }
            else
            {
                // indicates that the item is stored in the subnode block, and the NID is the local NID under the subnode
                Subnode subnode = subnodeBTree.GetSubnode(heapOrNodeID.NodeID);
                if (subnode != null)
                {
                    if (subnode.DataTree == null)
                    {
                        return new byte[0];
                    }
                    else
                    {
                        return subnode.DataTree.GetData();
                    }
                }
                else
                {
                    throw new MissingSubnodeException();
                }
            }
        }

        public static void RemoveExternalProperty(HeapOnNode heap, SubnodeBTree subnodeBTree, HeapOrNodeID heapOrNodeID)
        {
            if (!heapOrNodeID.IsEmpty)
            {
                if (heapOrNodeID.IsHeapID)
                {
                    heap.RemoveItemFromHeap(heapOrNodeID.HeapID);
                }
                else
                {
                    DataTree dataTree = subnodeBTree.GetSubnode(heapOrNodeID.NodeID).DataTree;
                    dataTree.Delete();
                    subnodeBTree.DeleteSubnodeEntry(heapOrNodeID.NodeID);
                }
            }
        }

        public static HeapOrNodeID StoreExternalProperty(PSTFile file, HeapOnNode heap, ref SubnodeBTree subnodeBTree, byte[] propertyBytes)
        {
            return StoreExternalProperty(file, heap, ref subnodeBTree, new HeapOrNodeID(HeapID.EmptyHeapID), propertyBytes);
        }

        /// <summary>
        /// [A11] INSERT-only streaming overload: heap-vs-subnode routing by length.
        /// ≤3580 B: reads from stream into a buffer and delegates to the existing heap (byte[]) INSERT path.
        /// &gt;3580 B: builds a new subnode DataTree via streaming AppendData (which ends with its own
        /// SaveChanges [R2:C1]), then inserts the subnode entry AFTER that flush [A4].
        /// [I2] No UPDATE-path stream overload — INSERT-only is enforced at the PropertyContext seam (Task 4).
        /// </summary>
        internal static HeapOrNodeID StoreExternalProperty(PSTFile file, HeapOnNode heap,
            ref SubnodeBTree subnodeBTree, Stream stream, long length,
            System.Threading.CancellationToken cancellationToken)
        {
            if (length <= HeapOnNode.MaximumAllocationLength)
            {
                byte[] small = new byte[(int)length];
                ReadExactly(stream, small, (int)length);
                return StoreExternalProperty(file, heap, ref subnodeBTree, small);
            }

            if (subnodeBTree == null)
            {
                subnodeBTree = new SubnodeBTree(file);
            }
            DataTree dataTree = new DataTree(file);
            dataTree.AppendData(stream, length, cancellationToken); // streams + persists/evicts + final local SaveChanges [R2:C1]

            NodeID subnodeID = file.Header.AllocateNextNodeID(NodeTypeName.NID_TYPE_LTP);
            subnodeBTree.InsertSubnodeEntry(subnodeID, dataTree, null); // [A4] reads RootBlock.BlockID AFTER AppendData's flush

            return new HeapOrNodeID(subnodeID);
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) throw new EndOfStreamException("Stream ended before the declared property length.");
                read += n;
            }
        }

        /// <param name="subnodeBTree">Note: We use ref, this way we are able to create a new subnode BTree and update the subnodeBTree the caller provided</param>
        /// <param name="heapOrNodeID">Existing HeapOrNodeID</param>
        public static HeapOrNodeID StoreExternalProperty(PSTFile file, HeapOnNode heap, ref SubnodeBTree subnodeBTree, HeapOrNodeID heapOrNodeID, byte[] propertyBytes)
        {
            // We should avoid storing items with length of 0, because those are consideref freed, and could be repurposed
            if (propertyBytes.Length == 0)
            {
                RemoveExternalProperty(heap, subnodeBTree, heapOrNodeID);
                return new HeapOrNodeID(HeapID.EmptyHeapID);
            }

            if (heapOrNodeID.IsHeapID) // if HeapOrNodeID is empty then IsHeapID == true
            {
                if (propertyBytes.Length <= HeapOnNode.MaximumAllocationLength)
                {
                    if (heapOrNodeID.IsEmpty)
                    {
                        return new HeapOrNodeID(heap.AddItemToHeap(propertyBytes));
                    }
                    else
                    {
                        return new HeapOrNodeID(heap.ReplaceHeapItem(heapOrNodeID.HeapID, propertyBytes));
                    }
                }
                else // old data (if exist) is stored on heap, but new data needs a subnode
                {
                    if (!heapOrNodeID.IsEmpty)
                    {
                        heap.RemoveItemFromHeap(heapOrNodeID.HeapID);
                    }

                    if (subnodeBTree == null)
                    {
                        subnodeBTree = new SubnodeBTree(file);
                    }
                    DataTree dataTree = new DataTree(file);
                    dataTree.AppendData(propertyBytes);
                    dataTree.SaveChanges();

                    NodeID subnodeID = file.Header.AllocateNextNodeID(NodeTypeName.NID_TYPE_LTP);
                    subnodeBTree.InsertSubnodeEntry(subnodeID, dataTree, null);

                    return new HeapOrNodeID(subnodeID);
                }
            }
            else // old data is stored in a subnode
            {
                Subnode subnode = subnodeBTree.GetSubnode(heapOrNodeID.NodeID);
                if (subnode.DataTree != null)
                {
                    subnode.DataTree.Delete();
                }
                subnode.DataTree = new DataTree(subnodeBTree.File);
                subnode.DataTree.AppendData(propertyBytes);
                subnode.SaveChanges(subnodeBTree);
                return new HeapOrNodeID(heapOrNodeID.NodeID);
            }
        }
    }
}
