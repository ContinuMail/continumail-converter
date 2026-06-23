using System;
using System.Collections.Generic;
using System.Reflection;
using PSTFileFormat;

class Probe
{
    static string Hex(byte[] b)
    {
        if (b == null) return "<null>";
        var sb = new System.Text.StringBuilder();
        foreach (var x in b) sb.Append(x.ToString("X2")).Append(' ');
        return sb.ToString().Trim();
    }

    static object F(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        var fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null) return fi.GetValue(o);
        var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null) return pi.GetValue(o);
        return "<no-field:" + name + ">";
    }

    static void DumpAllFields(object o, string indent)
    {
        if (o == null) { Console.WriteLine(indent + "<null>"); return; }
        var t = o.GetType();
        foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            object v;
            try { v = fi.GetValue(o); } catch (Exception e) { v = "<err:" + e.Message + ">"; }
            string disp;
            if (v is byte[] ba) disp = "byte[" + ba.Length + "] " + Hex(ba.Length > 32 ? ba[..32] : ba);
            else disp = v?.ToString() ?? "<null>";
            Console.WriteLine($"{indent}{fi.FieldType.Name} {fi.Name} = {disp}");
            string tn = fi.FieldType.Name;
            if ((tn == "BlockID" || tn == "BlockRef") && v != null)
                DumpAllFields(v, indent + "    ");
        }
    }

    static void Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: pst-probe <path-to-pst>  (pass a real Outlook-made empty PST as the oracle)"); return; }
        string path = args[0];
        Console.WriteLine("=== PROBE: " + path + " ===\n");
        var file = new PSTFile(path, System.IO.FileAccess.Read, WriterCompatibilityMode.Outlook2003RTM);

        // -------- HEADER --------
        Console.WriteLine("##### HEADER #####");
        var header = F(file, "m_header") ?? F(file, "Header");
        DumpAllFields(header, "  ");
        Console.WriteLine("\n  --- root (RootStructure) ---");
        var root = F(header, "root");
        DumpAllFields(root, "    ");

        // -------- MESSAGE STORE NODE 0x21 --------
        Console.WriteLine("\n##### MESSAGE STORE NODE 0x21 #####");
        try
        {
            var msNode = PSTNode.GetPSTNode(file, new NodeID(0x21));
            Console.WriteLine("  node class: " + msNode.GetType().Name + "  NodeID=0x" + msNode.NodeID.Value.ToString("X"));
            var pc = msNode.PC; // NamedPropertyContext : PropertyContext
            var records = pc.GetAllProperties();
            Console.WriteLine("  property count: " + records.Count);
            foreach (var rec in records)
            {
                ushort tag = (ushort)(PropertyID)F(rec, "wPropId");
                var ptype = F(rec, "wPropType");
                byte[] bytes = null;
                try { bytes = pc.GetExternalRecordData((PropertyContextRecord)rec); } catch { }
                string val;
                if (bytes != null) val = "ext byte[" + bytes.Length + "]: " + Hex(bytes);
                else val = "inline dwValueHnid=0x" + ((uint)F(rec, "dwValueHnid")).ToString("X8");
                Console.WriteLine($"    0x{tag:X4} {ptype} => {val}");
            }
        }
        catch (Exception e) { Console.WriteLine("  STORE NODE ERROR: " + e); }

        // -------- FOLDER WALK --------
        Console.WriteLine("\n##### FOLDER TREE (RootFolder 0x122) #####");
        WalkFolder(file.RootFolder, "  ");
        Console.WriteLine("\n##### TopOfPersonalFolders (0x8022) #####");
        try { WalkFolder(file.TopOfPersonalFolders, "  "); }
        catch (Exception e) { Console.WriteLine("  TOP ERROR: " + e.Message); }

        // -------- TEMPLATE TABLE-CONTEXT COLUMNS --------
        Console.WriteLine("\n##### TEMPLATE TABLE-CONTEXT COLUMNS #####");
        DumpTcColumns(file, 0x60D, "HIERARCHY_TABLE_TEMPLATE");
        DumpTcColumns(file, 0x60E, "CONTENTS_TABLE_TEMPLATE");
        DumpTcColumns(file, 0x60F, "ASSOC_CONTENTS_TABLE_TEMPLATE");
        DumpTcColumns(file, 0x671, "ATTACHMENT_TABLE");
        DumpTcColumns(file, 0x692, "RECIPIENT_TABLE");

        Console.WriteLine("\n=== DONE ===");
    }

    static void WalkFolder(PSTFolder folder, string indent)
    {
        if (folder == null) { Console.WriteLine(indent + "<null folder>"); return; }
        string name, cclass;
        try { name = folder.DisplayName; } catch { name = "<no-name>"; }
        try { cclass = folder.ContainerClass; } catch { cclass = ""; }
        Console.WriteLine($"{indent}NID=0x{folder.NodeID.Value:X} \"{name}\" class=[{cclass}]");
        try
        {
            foreach (var child in folder.GetChildFolders())
                WalkFolder(child, indent + "    ");
        }
        catch (Exception e) { Console.WriteLine(indent + "  <children err: " + e.Message + ">"); }
    }

    static void DumpTcColumns(PSTFile file, uint nid, string label)
    {
        Console.WriteLine($"  --- {label} (0x{nid:X}) ---");
        try
        {
            var node = PSTNode.GetPSTNode(file, new NodeID(nid));
            var tc = node.TableContext;
            var tcInfo = F(tc, "m_tcInfo");
            var cols = F(tcInfo, "rgTCOLDESC") as System.Collections.IEnumerable;
            if (cols == null) { Console.WriteLine("    <no rgTCOLDESC>"); return; }
            foreach (var col in cols)
            {
                uint ctag = (uint)F(col, "Tag");
                ushort ib = (ushort)F(col, "ibData");
                byte cb = (byte)F(col, "cbData");
                byte ibit = (byte)F(col, "iBit");
                Console.WriteLine($"    Tag=0x{ctag:X8} ibData={ib} cbData={cb} iBit={ibit}");
            }
        }
        catch (Exception e) { Console.WriteLine("    ERROR: " + e.Message); }
    }
}
