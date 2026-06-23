use serde::Serialize;
use std::process::ExitCode;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct FolderEntry {
    path: Vec<String>,
    display_path: String,
    message_count: u64,
}

#[derive(Serialize)]
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
    // Scaffold: fail closed until Task 2 implements real opening.
    let report = Report {
        schema_version: 1,
        opened: false,
        file: file_name(&path),
        folders: Vec::new(),
        total_messages: 0,
        errors: vec![ErrorEntry {
            stage: "open".into(),
            message: "pst-validate scaffold: real PST opening not implemented yet".into(),
        }],
    };
    // Exactly one JSON object on stdout.
    println!("{}", serde_json::to_string(&report).expect("serialize report"));
    ExitCode::FAILURE
}

fn file_name(path: &str) -> String {
    std::path::Path::new(path)
        .file_name()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_else(|| path.to_string())
}
