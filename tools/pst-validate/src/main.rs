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
                    if let Err(e) = walk_folders(&store, &root_folder, &mut Vec::new(), &mut folders, &mut errors) {
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

/// Map a folder's content-count read to `(count, optional error)`. Extracted so the fix — a
/// `content_count()` read failure is RECORDED as an error, never silently coerced to 0 — is
/// unit-testable without a real PST fixture. `folder_label` is the folder's full display path, so the
/// error message locates the offending folder even when two branches share a folder name.
fn message_count_or_error(folder_label: &str, count: io::Result<i32>) -> (u64, Option<ErrorEntry>) {
    match count {
        Ok(c) => (c.max(0) as u64, None),
        Err(e) => (
            0,
            Some(ErrorEntry {
                stage: "content_count".into(),
                message: format!("{folder_label}: {e}"),
            }),
        ),
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
    errors: &mut Vec<ErrorEntry>,
) -> io::Result<()> {
    let hierarchy_table = match parent_folder.hierarchy_table() {
        // Known limitation (#9, outlook-pst 1.2.0): `hierarchy_table()` returns `Option` and collapses
        // BOTH "genuinely no hierarchy table" (a real leaf) AND "table node present but unreadable" (a
        // CORRUPT table) into `None` — the crate's `.get_or_init(|| read_table(..).ok()?)` discards the
        // read error, and there is no Result-returning table accessor in the public API. So a corrupt
        // hierarchy table is indistinguishable from a leaf here and its subtree is silently omitted.
        // This is a bounded, dev-tool-only false-negative (it never affects the well-formed from-scratch
        // PSTs this tool validates); see README "Known limitations". A real fix needs an upstream crate
        // change (a Result-returning table accessor).
        None => return Ok(()),
        Some(t) => t.clone(),
    };

    for row in hierarchy_table.rows_matrix() {
        // Convert the row ID to a NodeId, build an EntryId, and open the child folder.
        let node = NodeId::from(u32::from(row.id()));
        let entry_id: EntryId = store.properties().make_entry_id(node)?;
        let child_folder = store.open_folder(&entry_id)?;

        let name = child_folder.properties().display_name()?;
        prefix.push(name.clone());
        let display_path = prefix.join(" / ");

        let (count, count_err) =
            message_count_or_error(&display_path, child_folder.properties().content_count());
        if let Some(err) = count_err {
            errors.push(err);
        }

        out.push(FolderEntry {
            path: prefix.clone(),
            display_path,
            message_count: count,
        });

        walk_folders(store, &child_folder, prefix, out, errors)?;
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

    #[test]
    fn message_count_ok_returns_count_and_no_error() {
        let (count, err) = message_count_or_error("Inbox", Ok(5));
        assert_eq!(count, 5);
        assert!(err.is_none());
    }

    #[test]
    fn message_count_negative_clamps_to_zero() {
        let (count, err) = message_count_or_error("Inbox", Ok(-3));
        assert_eq!(count, 0);
        assert!(err.is_none());
    }

    #[test]
    fn message_count_err_records_content_count_error_and_zero() {
        let e = io::Error::new(io::ErrorKind::InvalidData, "bad content-count property");
        let (count, err) = message_count_or_error("Archive", Err(e));
        assert_eq!(count, 0, "a read error must not inflate the count");
        let err = err.expect("a content_count read error must be recorded");
        assert_eq!(err.stage, "content_count");
        assert!(err.message.contains("Archive"), "message should locate the folder by its path label");
    }

}
