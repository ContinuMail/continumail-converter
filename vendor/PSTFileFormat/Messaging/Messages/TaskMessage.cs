/* ContinuMail addition 2026: IPM.Task item factory (mirrors ContactMessage.cs / Note.cs).
 * Vendored PSTFileFormat is LGPLv3 — see vendor/LICENSE-PSTFileFormat.txt. */
using System;

namespace PSTFileFormat
{
    /// <summary>Outlook task item (IPM.Task).</summary>
    public class TaskMessage : MessageObject
    {
        protected TaskMessage(PSTNode node) : base(node) { }

        public static TaskMessage GetTask(PSTFile file, NodeID nodeID)
        {
            PSTNode node = file.GetNode(nodeID);
            return node.PC != null ? new TaskMessage(node) : null;
        }

        public static TaskMessage CreateNewTask(PSTFile file, NodeID parentNodeID)
        {
            return CreateNewTask(file, parentNodeID, Guid.NewGuid());
        }

        public static TaskMessage CreateNewTask(PSTFile file, NodeID parentNodeID, Guid searchKey)
        {
            MessageObject message = CreateNewMessage(file, FolderItemTypeName.Task, parentNodeID, searchKey);
            var task = new TaskMessage(message);
            task.MessageFlags = MessageFlags.MSGFLAG_READ;
            task.InternetCodepage = 65001; // UTF-8 (Note defaults to 1255 Hebrew — never inherit that)
            task.Importance = MessageImportance.Normal;
            task.Priority = MessagePriority.Normal;
            return task;
        }
    }
}
