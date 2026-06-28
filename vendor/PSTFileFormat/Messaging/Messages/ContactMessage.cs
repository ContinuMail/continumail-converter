/* ContinuMail addition 2026: IPM.Contact item factory (mirrors Note.cs).
 * Vendored PSTFileFormat is LGPLv3 — see vendor/LICENSE-PSTFileFormat.txt. */
using System;

namespace PSTFileFormat
{
    /// <summary>Outlook contact item (IPM.Contact).</summary>
    public class ContactMessage : MessageObject
    {
        protected ContactMessage(PSTNode node) : base(node) { }

        public static ContactMessage GetContact(PSTFile file, NodeID nodeID)
        {
            PSTNode node = file.GetNode(nodeID);
            return node.PC != null ? new ContactMessage(node) : null;
        }

        public static ContactMessage CreateNewContact(PSTFile file, NodeID parentNodeID)
        {
            return CreateNewContact(file, parentNodeID, Guid.NewGuid());
        }

        public static ContactMessage CreateNewContact(PSTFile file, NodeID parentNodeID, Guid searchKey)
        {
            MessageObject message = CreateNewMessage(file, FolderItemTypeName.Contact, parentNodeID, searchKey);
            var contact = new ContactMessage(message);
            contact.MessageFlags = MessageFlags.MSGFLAG_READ;
            contact.InternetCodepage = 65001; // UTF-8 (Note defaults to 1255 Hebrew — never inherit that)
            contact.Importance = MessageImportance.Normal;
            contact.Priority = MessagePriority.Normal;
            return contact;
        }
    }
}
