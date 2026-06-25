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

namespace PSTFileFormat
{
    public class AttachmentObject : Subnode
    {
        public AttachmentObject(Subnode subnode)
            : base(subnode.File, subnode.SubnodeID, subnode.DataTree, subnode.SubnodeBTree)
        {
        }

        public Subnode AttachedNode
        {
            get
            {
                PropertyContext pc = this.PC;
                if (pc != null)
                {
                    Subnode subnode = pc.GetObjectProperty(PropertyID.PidTagAttachData);
                    return subnode;
                }
                return null;
            }
        }

        public static AttachmentObject CreateNewAttachmentObject(PSTFile file, SubnodeBTree subnodeBTree)
        {
            PropertyContext pc = PropertyContext.CreateNewPropertyContext(file);
            pc.SaveChanges();

            NodeID pcNodeID = file.Header.AllocateNextNodeID(NodeTypeName.NID_TYPE_ATTACHMENT);
            subnodeBTree.InsertSubnodeEntry(pcNodeID, pc.DataTree, pc.SubnodeBTree);

            Subnode subnode = new Subnode(file, pcNodeID, pc.DataTree, pc.SubnodeBTree);
            return new AttachmentObject(subnode);
        }

    }
}
