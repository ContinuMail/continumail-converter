use outlook_pst::{messaging::store::UnicodeStore, UnicodePstFile};
use serde::Serialize;
use std::{process::ExitCode, rc::Rc};

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
    match open_store_at(path) {
        Ok(_store) => Report {
            schema_version: 1,
            opened: true,
            file,
            folders: Vec::new(),
            total_messages: 0,
            errors: Vec::new(),
        },
        Err(e) => Report {
            schema_version: 1,
            opened: false,
            file,
            folders: Vec::new(),
            total_messages: 0,
            errors: vec![ErrorEntry {
                stage: "open".into(),
                message: format!("{e}"),
            }],
        },
    }
}

/// Open a Unicode PST file and return its store.
/// Returns `io::Error` on any parse/IO failure.
fn open_store_at(path: &str) -> std::io::Result<Rc<UnicodeStore>> {
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
    fn template_pst_opens_cleanly() {
        // The blank Unicode template the converter copies. Resolve from the repo root.
        let template = repo_root().join("assets").join("template.pst");
        let report = open_and_report(template.to_str().unwrap());
        assert!(report.opened, "template must open: {:?}", report.errors);
        assert!(report.errors.is_empty());
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
