/* ContinuMail addition (not part of upstream PSTFileFormat).
 *
 * Phase B of from-scratch store creation: on top of the raw scaffold written by
 * PSTFile.CreateEmptyStore (Phase A), reconstruct a complete, Outlook-openable empty store
 * by replaying the node set of a real Outlook-made store 1:1.
 *
 * WHY 1:1: a minimal spec-compliant store is NOT enough — Outlook's HrValidateIPMSubtree
 * rejects a store that lacks the full infrastructure (Search-Folders root, Receive-Folder
 * table, Outgoing-Queue, Views/Finder objects, Search-Management-Queue, …) and re-provisions
 * empty default folders. So we reproduce every node of the template (43 of them), byte-for-byte
 * in content, into a from-scratch file. The node blueprint lives in the generated partial
 * DefaultStoreTemplates.Blueprint.g.cs (produced by tools/pst-genblueprint). See tools/pst-genblueprint/README.md.
 * That blueprint is the frozen source of truth and is not routinely regenerated.
 *
 * Per-store customization on top of the verbatim copy:
 *   - a fresh store GUID (so every output PST is unique), replacing the template's GUID
 *     wherever it appears in the message-store node (PidTagRecordKey + the 3 folder EntryIDs);
 *   - English default-folder names (the template was made on a Danish Outlook).
 */
using System;

namespace PSTFileFormat
{
    internal static partial class DefaultStoreTemplates
    {
        private const uint NID_MESSAGE_STORE = 0x21;
        private const uint NID_ROOT_FOLDER = 0x122;
        private const uint NID_IPM_SUBTREE = 0x8022;   // "Top of Information Store"
        private const uint NID_DELETED_ITEMS = 0x8062;
        private const uint NID_SEARCH_ROOT = 0x8042;

        /// <summary>
        /// Populate an opened-but-empty scaffold into a valid default store by reconstructing
        /// the template's full node set. Wraps the whole mint in Begin/EndSavingChanges.
        /// </summary>
        internal static void Build(PSTFile file)
        {
            file.BeginSavingChanges();

            // One fresh GUID per store, substituted for the template's baked-in store GUID.
            byte[] freshGuid = Guid.NewGuid().ToByteArray();

            foreach (BlueprintNode bp in BlueprintNodes())
            {
                byte[] data = bp.Data;
                if (bp.Nid == NID_MESSAGE_STORE && data.Length > 0)
                {
                    data = ReplaceAll(data, TemplateStoreGuid, freshGuid);
                }

                DataTree dataTree = null;
                if (data.Length > 0)
                {
                    // The DataTree ctor already creates an empty block 0; fill THAT block
                    // (UpdateDataBlock), do not AddDataBlock (which would prepend a full zero block).
                    dataTree = new DataTree(file);
                    dataTree.UpdateDataBlock(0, data);
                    dataTree.SaveChanges();
                }
                file.NodeBTree.InsertNodeEntry(new NodeID(bp.Nid), dataTree, null, new NodeID(bp.Parent));
            }

            // Replicate the template's per-type NID high-water marks (rgnid) exactly, so the
            // header high-water-mark validation passes and later auto-allocated NIDs don't collide.
            for (int i = 0; i < BlueprintRgnid.Length; i++)
            {
                file.Header.EnsureNodeIndexAtLeast((NodeTypeName)i, BlueprintRgnid[i]);
            }

            // English default names (the verbatim template carries Danish ones).
            SetStoreDisplayName(file, "ContinuMail Store");
            RenameFolder(file, NID_IPM_SUBTREE, NID_ROOT_FOLDER, "Top of Information Store");
            RenameFolder(file, NID_DELETED_ITEMS, NID_IPM_SUBTREE, "Deleted Items");
            RenameFolder(file, NID_SEARCH_ROOT, NID_ROOT_FOLDER, "Search Root");

            file.EndSavingChanges();
        }

        // Replace every (non-overlapping) occurrence of <find> with <replacement> (equal length).
        private static byte[] ReplaceAll(byte[] data, byte[] find, byte[] replacement)
        {
            byte[] result = (byte[])data.Clone();
            for (int i = 0; i + find.Length <= result.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < find.Length; j++)
                {
                    if (result[i + j] != find[j]) { match = false; break; }
                }
                if (match)
                {
                    Array.Copy(replacement, 0, result, i, replacement.Length);
                    i += find.Length - 1;
                }
            }
            return result;
        }

        private static void SetStoreDisplayName(PSTFile file, string name)
        {
            PSTNode node = PSTNode.GetPSTNode(file, new NodeID(NID_MESSAGE_STORE));
            PropertyContext pc = node.PC;
            pc.SetStringProperty(PropertyID.PidTagDisplayName, name);
            pc.SaveChanges(new NodeID(NID_MESSAGE_STORE));
        }

        // Rename a default folder both in its own PC and in the parent's hierarchy-table row
        // (Outlook builds the visible tree from the hierarchy rows).
        private static void RenameFolder(PSTFile file, uint folderNid, uint parentNid, string name)
        {
            PSTNode node = PSTNode.GetPSTNode(file, new NodeID(folderNid));
            PropertyContext pc = node.PC;
            pc.SetStringProperty(PropertyID.PidTagDisplayName, name);
            pc.SaveChanges(new NodeID(folderNid));

            PSTFolder parent = PSTFolder.GetFolder(file, new NodeID(parentNid));
            TableContext hierarchyTable = parent.GetHierarchyTable();
            int row = hierarchyTable.GetRowIndex(folderNid);
            if (row >= 0)
            {
                hierarchyTable.SetStringProperty(row, PropertyID.PidTagDisplayName, name);
                hierarchyTable.SaveChanges(parent.GetHierarchyTableNodeID());
            }
        }
    }
}
