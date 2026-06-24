use outlook_pst::{
    messaging::{
        folder::Folder,
        store::{EntryId, Store, UnicodeStore},
    },
    ndb::node_id::NodeId,
    UnicodePstFile,
};
use serde::Serialize;
use std::{io, process::ExitCode, rc::Rc};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct FolderEntry {
    path: Vec<String>,
    display_path: String,
    message_count: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ErrorEntry {
    stage: String,
    message: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Report {
    schema_version: u32,
    opened: bool,
    file: String,
    folders: Vec<FolderEntry>,
    total_messages: u64,
    errors: Vec<ErrorEntry>,
}

fn main() -> ExitCode {
    let path = std::env::args().nth(1).unwrap_or_default();
    let report = open_and_report(&path);
    let ok = report.opened && report.errors.is_empty();
    println!("{}", serde_json::to_string(&report).expect("serialize report"));
    if ok {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}

fn open_and_report(path: &str) -> Report {
    let file = file_name(path);
    let store = match open_store_at(path) {
        Ok(s) => s,
        Err(e) => {
            return Report {
                schema_version: 1,
                opened: false,
                file,
                folders: Vec::new(),
                total_messages: 0,
                errors: vec![ErrorEntry {
                    stage: "open".into(),
                    message: format!("{e}"),
                }],
            }
        }
    };

    let mut folders = Vec::new();
    let mut errors = Vec::new();

    // Get the IPM subtree entry ID (the "root" visible folder tree in an Outlook PST).
    // Descendants of this node are the visible folders; the IPM subtree node itself is not emitted.
    match store.properties().ipm_sub_tree_entry_id() {
        Err(e) => {
            errors.push(ErrorEntry {
                stage: "walk".into(),
                message: format!("ipm_sub_tree_entry_id: {e}"),
            });
        }
        Ok(root_entry_id) => {
            match store.open_folder(&root_entry_id) {
                Err(e) => {
                    errors.push(ErrorEntry {
                        stage: "walk".into(),
                        message: format!("open root folder: {e}"),
                    });
                }
                Ok(root_folder) => {
                    if let Err(e) = walk_folders(&store, &root_folder, &mut Vec::new(), &mut folders) {
                        errors.push(ErrorEntry {
                            stage: "walk".into(),
                            message: format!("{e}"),
                        });
                    }
                }
            }
        }
    }

    let total_messages: u64 = folders.iter().map(|f| f.message_count).sum();
    let opened = errors.is_empty();
    Report {
        schema_version: 1,
        opened,
        file,
        folders,
        total_messages,
        errors,
    }
}

/// Recursively walk all child folders of `parent_folder`.
/// The root/IPM subtree folder itself is NOT emitted — only its descendants.
/// `prefix` accumulates the path segments relative to the IPM subtree root.
fn walk_folders(
    store: &Rc<UnicodeStore>,
    parent_folder: &Rc<dyn Folder>,
    prefix: &mut Vec<String>,
    out: &mut Vec<FolderEntry>,
) -> io::Result<()> {
    let hierarchy_table = match parent_folder.hierarchy_table() {
        None => return Ok(()), // no subfolders
        Some(t) => t.clone(),
    };

    for row in hierarchy_table.rows_matrix() {
        // Convert the row ID to a NodeId, build an EntryId, and open the child folder.
        let node = NodeId::from(u32::from(row.id()));
        let entry_id: EntryId = store.properties().make_entry_id(node)?;
        let child_folder = store.open_folder(&entry_id)?;

        let name = child_folder.properties().display_name()?;
        let count = child_folder
            .properties()
            .content_count()
            .unwrap_or(0)
            .max(0) as u64;

        prefix.push(name.clone());
        out.push(FolderEntry {
            path: prefix.clone(),
            display_path: prefix.join(" / "),
            message_count: count,
        });

        walk_folders(store, &child_folder, prefix, out)?;
        prefix.pop();
    }

    Ok(())
}

/// Open a Unicode PST file and return its store.
/// Returns `io::Error` on any parse/IO failure.
fn open_store_at(path: &str) -> io::Result<Rc<UnicodeStore>> {
    let pst = UnicodePstFile::open(path)?;
    UnicodeStore::read(Rc::new(pst))
}

fn file_name(path: &str) -> String {
    std::path::Path::new(path)
        .file_name()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_else(|| path.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn garbage_file_reports_not_opened() {
        let mut tmp = std::env::temp_dir();
        tmp.push("pst-validate-garbage.pst");
        std::fs::write(&tmp, b"this is not a PST file at all").unwrap();

        let report = open_and_report(tmp.to_str().unwrap());

        assert!(!report.opened, "garbage must not open");
        assert!(!report.errors.is_empty(), "garbage must produce an error");
        assert_eq!(report.errors[0].stage, "open");
        let _ = std::fs::remove_file(&tmp);
    }

    #[test]
    fn json_shape_is_stable_on_success_and_failure() {
        for opened in [true, false] {
            let report = Report {
                schema_version: 1, opened, file: "x.pst".into(),
                folders: vec![FolderEntry { path: vec!["A".into()], display_path: "A".into(), message_count: 1 }],
                total_messages: 1,
                errors: if opened { vec![] } else { vec![ErrorEntry { stage: "open".into(), message: "e".into() }] },
            };
            let v: serde_json::Value = serde_json::from_str(&serde_json::to_string(&report).unwrap()).unwrap();
            for key in ["schemaVersion", "opened", "file", "folders", "totalMessages", "errors"] {
                assert!(v.get(key).is_some(), "missing key {key} when opened={opened}");
            }
            assert_eq!(v["schemaVersion"], 1);
            assert_eq!(v["folders"][0]["messageCount"], 1);
            assert_eq!(v["folders"][0]["displayPath"], "A");
        }
    }

}
