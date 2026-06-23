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

    // Produce a minimal PST via the converter CLI (which uses PSTFile.CreateEmptyStore
    // from scratch). Returns None if the CLI binary or dotnet runtime isn't available so
    // the callers can skip instead of fail. The returned path lives in a temp dir; callers
    // are responsible for deleting it.
    fn create_test_pst_via_cli() -> Option<(std::path::PathBuf, std::path::PathBuf)> {
        let fixtures = repo_root().join("fixtures");
        let sample = fixtures.join("sample.mbox");
        if !sample.exists() { return None; }

        let out_dir = std::env::temp_dir().join(format!("pst-validate-self-test-{}", std::process::id()));
        std::fs::create_dir_all(&out_dir).ok()?;

        // Write a minimal convert config.
        let mbox_path = sample.to_string_lossy();
        let config_json = format!(
            r#"{{"outputs":[{{"name":"Test","maxSizeMB":100,"folderMapping":"mirror","sources":[{{"path":"{mbox}","type":"mbox"}}]}}]}}"#,
            mbox = mbox_path.replace('\\', "\\\\")
        );
        let config_file = out_dir.join("config.json");
        std::fs::write(&config_file, config_json).ok()?;

        // Find the CLI binary (built by dotnet publish or dotnet build).
        let cli = repo_root()
            .join("src").join("Mail2Pst.Cli")
            .join("bin").join("Debug").join("net8.0").join("Mail2Pst.Cli.dll");
        if !cli.exists() { return None; }

        let status = std::process::Command::new("dotnet")
            .args([
                cli.to_str()?,
                "convert",
                "--config", config_file.to_str()?,
                "--output", out_dir.to_str()?,
            ])
            .output()
            .ok()?;
        if !status.status.success() { return None; }

        let pst = out_dir.join("Test.pst");
        if !pst.exists() { return None; }
        Some((pst, out_dir))
    }

    #[test]
    fn converter_output_pst_opens_cleanly() {
        // The converter now builds PSTs from scratch (PSTFile.CreateEmptyStore).
        // This test validates that the independent Rust reader can open and parse the result.
        // Skips automatically when the CLI binary is not yet built (opt-in, like IndependentValidationTests).
        let Some((pst, out_dir)) = create_test_pst_via_cli() else { return; };
        let report = open_and_report(pst.to_str().unwrap());
        let _ = std::fs::remove_dir_all(&out_dir);
        assert!(report.opened, "converter output must open: {:?}", report.errors);
        assert!(report.errors.is_empty());
    }

    #[test]
    fn converter_output_walk_totals_are_consistent() {
        // As above: creates a fresh PST via the converter and verifies walk-total consistency.
        // Skips automatically when the CLI binary is not yet built.
        let Some((pst, out_dir)) = create_test_pst_via_cli() else { return; };
        let report = open_and_report(pst.to_str().unwrap());
        let _ = std::fs::remove_dir_all(&out_dir);
        assert!(report.opened, "converter output must open: {:?}", report.errors);
        // total_messages must equal the sum of per-folder counts.
        let sum: u64 = report.folders.iter().map(|f| f.message_count).sum();
        assert_eq!(report.total_messages, sum);
        // No emitted folder path may be empty, and displayPath must join the segments.
        for f in &report.folders {
            assert!(!f.path.is_empty(), "root folder must not be emitted");
            assert_eq!(f.display_path, f.path.join(" / "));
        }
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

    fn repo_root() -> std::path::PathBuf {
        // tools/pst-validate -> repo root is two parents up from CARGO_MANIFEST_DIR.
        std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf()
    }
}
