// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

use serde::Serialize;
use std::sync::Mutex;
use tauri::path::BaseDirectory;
use tauri::{AppHandle, Emitter, Manager, State};
use tauri_plugin_opener::OpenerExt;
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

// Holds the single in-flight conversion's child handle so cancel_convert can
// write to its stdin. Lock only briefly — never across an .await.
#[derive(Default)]
struct ConvertState(Mutex<Option<CommandChild>>);

// Holds the single in-flight scan's child handle so we can reject a second
// concurrent scan. Lock only briefly — never across an .await.
#[derive(Default)]
struct ScanState(Mutex<Option<CommandChild>>);

// Drain every complete (newline-terminated) line from `buf`, returning each with
// trailing CR/LF stripped and empty lines skipped. Any trailing partial line is
// left in `buf` for the next chunk. Used by both the scan and convert readers so
// a JSON record that spans or batches across Stdout events is never dropped.
fn drain_lines(buf: &mut String) -> Vec<String> {
    let mut out = Vec::new();
    while let Some(nl) = buf.find('\n') {
        let line: String = buf.drain(..=nl).collect();
        let line = line.trim_end_matches(|c| c == '\r' || c == '\n').to_string();
        if !line.is_empty() {
            out.push(line);
        }
    }
    out
}

// Like run_sidecar, but returns stdout even when the sidecar exits nonzero AS LONG AS it printed
// something — import-colours' handled failures exit 1 while emitting a structured {type:"error"} JSON
// object the frontend needs. Err only on a spawn failure or a nonzero exit with no stdout.
async fn run_sidecar_capture(app: &tauri::AppHandle, args: Vec<String>) -> Result<String, String> {
    let output = app
        .shell()
        .sidecar("mail2pst-cli")
        .map_err(|e| format!("sidecar not found: {e}"))?
        .args(args)
        .output()
        .await
        .map_err(|e| format!("failed to run engine: {e}"))?;
    let stdout = String::from_utf8_lossy(&output.stdout).to_string();
    if output.status.success() || !stdout.trim().is_empty() {
        Ok(stdout)
    } else {
        Err(format!(
            "engine exited with {:?}: {}",
            output.status.code(),
            String::from_utf8_lossy(&output.stderr)
        ))
    }
}

async fn run_sidecar(app: &tauri::AppHandle, args: Vec<String>) -> Result<String, String> {
    let output = app
        .shell()
        .sidecar("mail2pst-cli")
        .map_err(|e| format!("sidecar not found: {e}"))?
        .args(args)
        .output()
        .await
        .map_err(|e| format!("failed to run engine: {e}"))?;

    if !output.status.success() {
        return Err(format!(
            "engine exited with {:?}: {}",
            output.status.code(),
            String::from_utf8_lossy(&output.stderr)
        ));
    }
    Ok(String::from_utf8_lossy(&output.stdout).to_string())
}

#[tauri::command]
async fn check_engine_version(app: tauri::AppHandle) -> Result<String, String> {
    run_sidecar(&app, vec!["--version".to_string()]).await
}

#[tauri::command]
async fn scan_sample(app: tauri::AppHandle) -> Result<String, String> {
    let sample = app
        .path()
        .resolve("resources/sample.mbox", BaseDirectory::Resource)
        .map_err(|e| format!("could not resolve bundled sample: {e}"))?;
    let sample = sample.to_string_lossy().to_string();
    run_sidecar(
        &app,
        vec!["scan".to_string(), "--input".to_string(), sample],
    )
    .await
}

#[tauri::command]
async fn discover_profile(app: tauri::AppHandle, dir: String) -> Result<String, String> {
    run_sidecar(&app, vec!["discover".to_string(), "--input".to_string(), dir]).await
}

#[tauri::command]
async fn preview_colours(app: tauri::AppHandle, dir: String) -> Result<String, String> {
    run_sidecar_capture(&app, vec!["import-colours".to_string(), "--profile".to_string(), dir]).await
}

#[tauri::command]
async fn apply_colours(app: tauri::AppHandle, dir: String) -> Result<String, String> {
    run_sidecar_capture(
        &app,
        vec!["import-colours".to_string(), "--profile".to_string(), dir, "--apply".to_string()],
    )
    .await
}

/// Writes the supplied colour plan to a temp file and runs `import-colours --apply --plan-file`,
/// then removes the temp file (mirrors start_convert's temp-config cleanup).
#[tauri::command]
async fn apply_colours_plan(app: AppHandle, plan: serde_json::Value) -> Result<String, String> {
    let unique = format!(
        "{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0)
    );
    let plan_path = std::env::temp_dir().join(format!("continumail-colourplan-{unique}.json"));
    std::fs::write(&plan_path, serde_json::to_string(&plan).map_err(|e| e.to_string())?)
        .map_err(|e| format!("cannot write plan: {e}"))?;
    let path_str = plan_path.to_string_lossy().to_string();
    let result = run_sidecar_capture(&app, vec![
        "import-colours".into(), "--plan-file".into(), path_str, "--apply".into(),
    ]).await;
    let _ = std::fs::remove_file(&plan_path); // best-effort cleanup
    result
}

#[derive(Serialize)]
struct FileStat {
    path: String,
    size: u64,
}

#[tauri::command]
fn list_mbox_in_dir(dir: String) -> Result<Vec<String>, String> {
    let entries = std::fs::read_dir(&dir).map_err(|e| format!("cannot read folder: {e}"))?;
    let mut paths: Vec<String> = Vec::new();
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_file()
            && path
                .extension()
                .and_then(|e| e.to_str())
                .map(|e| e.eq_ignore_ascii_case("mbox"))
                .unwrap_or(false)
        {
            paths.push(path.to_string_lossy().to_string());
        }
    }
    paths.sort_by(|a, b| a.to_lowercase().cmp(&b.to_lowercase()));
    Ok(paths)
}

#[tauri::command]
fn stat_files(paths: Vec<String>) -> Vec<FileStat> {
    paths
        .into_iter()
        .map(|path| {
            let size = std::fs::metadata(&path).map(|m| m.len()).unwrap_or(0);
            FileStat { path, size }
        })
        .collect()
}

#[tauri::command]
async fn start_scan(
    app: AppHandle,
    paths: Vec<String>,
    state: State<'_, ScanState>,
) -> Result<(), String> {
    if paths.is_empty() {
        return Err("No files to scan.".into());
    }
    // Reject a second concurrent scan.
    if state.0.lock().unwrap().is_some() {
        return Err("A scan is already running.".into());
    }

    let mut args: Vec<String> = vec!["scan".into()];
    for p in paths {
        args.push("--input".into());
        args.push(p);
    }
    args.push("--progress".into());

    let (mut rx, child) = app
        .shell()
        .sidecar("mail2pst-cli")
        .map_err(|e| format!("sidecar not found: {e}"))?
        .args(args)
        .spawn()
        .map_err(|e| format!("failed to start engine: {e}"))?;

    // Store the child BEFORE the reader task starts.
    *state.0.lock().unwrap() = Some(child);

    let app_for_task = app.clone();
    tauri::async_runtime::spawn(async move {
        // Buffer stdout and split on newlines — do NOT assume each Stdout event
        // is exactly one JSON line (a record may span events or arrive batched).
        let mut buf = String::new();
        let mut terminated = false;
        while let Some(event) = rx.recv().await {
            match event {
                CommandEvent::Stdout(bytes) => {
                    buf.push_str(&String::from_utf8_lossy(&bytes));
                    for line in drain_lines(&mut buf) {
                        let _ = app_for_task.emit("scan://line", line);
                    }
                }
                CommandEvent::Stderr(bytes) => {
                    let line = String::from_utf8_lossy(&bytes).to_string();
                    let _ = app_for_task.emit("scan://stderr", line);
                }
                CommandEvent::Terminated(payload) => {
                    terminated = true;
                    // Flush any trailing partial line (no terminating newline).
                    let rest = buf.trim_end_matches(|c| c == '\r' || c == '\n').to_string();
                    if !rest.is_empty() {
                        let _ = app_for_task.emit("scan://line", rest);
                    }
                    buf.clear();
                    *app_for_task.state::<ScanState>().0.lock().unwrap() = None;
                    let _ = app_for_task.emit("scan://exit", payload.code);
                }
                _ => {}
            }
        }
        // Defensive: if the stream ended WITHOUT a Terminated event, no exit was
        // emitted and the frontend Promise would hang forever. Flush any partial
        // line, clear the slot, and emit a synthetic failure so it rejects.
        if !terminated {
            let rest = buf.trim_end_matches(|c| c == '\r' || c == '\n').to_string();
            if !rest.is_empty() {
                let _ = app_for_task.emit("scan://line", rest);
            }
            *app_for_task.state::<ScanState>().0.lock().unwrap() = None;
            let _ = app_for_task.emit(
                "scan://stderr",
                "Scan process ended without a termination event.".to_string(),
            );
            let _ = app_for_task.emit("scan://exit", -1_i32);
        }
    });

    Ok(())
}

#[tauri::command]
async fn start_convert(
    app: AppHandle,
    config: serde_json::Value,
    output_dir: String,
    state: State<'_, ConvertState>,
) -> Result<(), String> {
    // Reject a second concurrent run.
    if state.0.lock().unwrap().is_some() {
        return Err("A conversion is already running.".into());
    }

    // Write the config to a UNIQUE temp file the sidecar will read (per-run name
    // avoids collisions with stale files / a second app instance).
    let unique = format!(
        "{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0)
    );
    let config_path = std::env::temp_dir().join(format!("continumail-convert-config-{unique}.json"));
    let config_text = serde_json::to_string(&config).map_err(|e| e.to_string())?;
    std::fs::write(&config_path, config_text).map_err(|e| format!("cannot write config: {e}"))?;
    let config_path_str = config_path.to_string_lossy().to_string();

    let (mut rx, child) = app
        .shell()
        .sidecar("mail2pst-cli")
        .map_err(|e| format!("sidecar not found: {e}"))?
        .args(["convert", "--config", &config_path_str, "--output", &output_dir])
        .spawn()
        .map_err(|e| format!("failed to start engine: {e}"))?;

    // Store the child BEFORE the reader task starts.
    *state.0.lock().unwrap() = Some(child);

    let app_for_task = app.clone();
    tauri::async_runtime::spawn(async move {
        // Buffer stdout and split on newlines — do NOT assume each Stdout event is
        // exactly one JSON line (a record may span events or arrive batched).
        let mut buf = String::new();
        let mut terminated = false;
        while let Some(event) = rx.recv().await {
            match event {
                CommandEvent::Stdout(bytes) => {
                    buf.push_str(&String::from_utf8_lossy(&bytes));
                    for line in drain_lines(&mut buf) {
                        let _ = app_for_task.emit("convert://line", line);
                    }
                }
                CommandEvent::Stderr(bytes) => {
                    let line = String::from_utf8_lossy(&bytes).to_string();
                    let _ = app_for_task.emit("convert://stderr", line);
                }
                CommandEvent::Terminated(payload) => {
                    terminated = true;
                    // Flush any trailing partial line (no terminating newline).
                    let rest = buf.trim_end_matches(|c| c == '\r' || c == '\n').to_string();
                    if !rest.is_empty() {
                        let _ = app_for_task.emit("convert://line", rest);
                    }
                    buf.clear();
                    *app_for_task.state::<ConvertState>().0.lock().unwrap() = None;
                    let _ = std::fs::remove_file(&config_path);
                    let _ = app_for_task.emit("convert://exit", payload.code);
                }
                _ => {}
            }
        }
        // Defensive: if the stream ended WITHOUT a Terminated event, flush any
        // partial line, clear the slot + temp file, and emit a synthetic failure
        // so the frontend can't hang forever.
        if !terminated {
            let rest = buf.trim_end_matches(|c| c == '\r' || c == '\n').to_string();
            if !rest.is_empty() {
                let _ = app_for_task.emit("convert://line", rest);
            }
            *app_for_task.state::<ConvertState>().0.lock().unwrap() = None;
            let _ = std::fs::remove_file(&config_path);
            let _ = app_for_task.emit("convert://exit", -1_i32);
        }
    });

    Ok(())
}

#[tauri::command]
fn cancel_convert(state: State<'_, ConvertState>) -> Result<(), String> {
    let mut guard = state.0.lock().unwrap();
    match guard.as_mut() {
        Some(child) => {
            child
                .write(b"cancel\n")
                .map_err(|e| format!("cancel failed: {e}"))?;
            Ok(())
        }
        None => Err("No conversion is running.".into()),
    }
}

#[tauri::command]
fn open_folder(app: AppHandle, path: String) -> Result<(), String> {
    app.opener()
        .reveal_item_in_dir(&path)
        .map_err(|e| format!("could not open folder: {e}"))
}

#[tauri::command]
fn open_junk_help(app: AppHandle) -> Result<(), String> {
    app.opener()
        .open_url(
            "https://support.mozilla.org/en-US/kb/thunderbird-and-junk-spam-messages",
            None::<&str>,
        )
        .map_err(|e| format!("could not open link: {e}"))
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ProfileEntry {
    name: String,
    path: String,
    is_default: bool,
    accounts: Vec<String>,
    convertible: bool,
}

/// Parse profiles.ini. `tb_root` = the Thunderbird dir (parent of Profiles/). Pure + testable.
fn parse_profiles_ini(content: &str, tb_root: &std::path::Path) -> Vec<ProfileEntry> {
    let mut defaults: Vec<String> = Vec::new();
    let mut profiles: Vec<(String, String, bool, Option<String>)> = Vec::new();
    let mut sec = String::new();
    let (mut name, mut path, mut is_rel, mut sec_default) =
        (None::<String>, None::<String>, true, None::<String>);

    for line in content.lines() {
        let l = line.trim();
        if l.starts_with('[') && l.ends_with(']') {
            if sec.starts_with("Profile") {
                if let (Some(n), Some(p)) = (name.take(), path.take()) {
                    profiles.push((n, p, is_rel, sec_default.take()));
                }
            }
            sec = l[1..l.len() - 1].to_string();
            name = None;
            path = None;
            is_rel = true;
            sec_default = None;
            continue;
        }
        let Some((k, v)) = l.split_once('=') else {
            continue;
        };
        let (k, v) = (k.trim(), v.trim());
        if sec.starts_with("Install") && k == "Default" {
            defaults.push(v.replace('\\', "/"));
        } else if sec.starts_with("Profile") {
            match k {
                "Name" => name = Some(v.to_string()),
                "Path" => path = Some(v.to_string()),
                "IsRelative" => is_rel = v == "1",
                "Default" if v == "1" => sec_default = Some("1".into()),
                _ => {}
            }
        }
    }
    if sec.starts_with("Profile") {
        if let (Some(n), Some(p)) = (name.take(), path.take()) {
            profiles.push((n, p, is_rel, sec_default.take()));
        }
    }

    profiles
        .into_iter()
        .map(|(name, raw, is_rel, sd)| {
            let abs = if is_rel {
                tb_root.join(&raw).to_string_lossy().to_string()
            } else {
                raw.clone()
            };
            let norm = raw.replace('\\', "/");
            let is_default = sd.is_some() || defaults.iter().any(|d| d.as_str() == norm);
            ProfileEntry { name, path: abs, is_default, accounts: Vec::new(), convertible: false }
        })
        .collect()
}

/// Parse mail.identity.*.useremail values from prefs.js. Pure. Case-insensitive dedupe, keep first.
fn parse_identity_emails(prefs: &str) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    let mut seen: std::collections::HashSet<String> = std::collections::HashSet::new();
    for line in prefs.lines() {
        let l = line.trim_start();
        if !l.starts_with("user_pref(") { continue; }
        let Some((key_raw, val_raw)) = two_quoted(l) else { continue; };
        if !(key_raw.starts_with("mail.identity.") && key_raw.ends_with(".useremail")) { continue; }
        let mid = &key_raw["mail.identity.".len()..key_raw.len() - ".useremail".len()];
        if mid.is_empty() || mid.contains('.') { continue; }
        let Some(v) = prefs_unescape(&val_raw) else { continue; };
        let v = v.trim().to_string();
        if v.is_empty() { continue; }
        if seen.insert(v.to_lowercase()) { out.push(v); }
    }
    out
}

/// Raw contents (escapes intact) of the first two double-quoted strings on a line, or None.
fn two_quoted(s: &str) -> Option<(String, String)> {
    let chars: Vec<char> = s.chars().collect();
    let mut found: Vec<String> = Vec::new();
    let mut i = 0;
    while i < chars.len() && found.len() < 2 {
        if chars[i] != '"' { i += 1; continue; }
        i += 1;
        let mut buf = String::new();
        loop {
            if i >= chars.len() { return None; }
            let c = chars[i];
            if c == '\\' {
                if i + 1 >= chars.len() { return None; }
                buf.push(c); buf.push(chars[i + 1]); i += 2; continue;
            }
            if c == '"' { i += 1; break; }
            buf.push(c); i += 1;
        }
        found.push(buf);
    }
    if found.len() == 2 { Some((found.remove(0), found.remove(0))) } else { None }
}

/// Unescape a prefs.js double-quoted value. None on dangling/unknown escape.
fn prefs_unescape(raw: &str) -> Option<String> {
    let mut s = String::with_capacity(raw.len());
    let mut it = raw.chars().peekable();
    while let Some(ch) = it.next() {
        if ch != '\\' { s.push(ch); continue; }
        match it.next()? {
            '\\' => s.push('\\'), '"' => s.push('"'),
            'n' => s.push('\n'), 'r' => s.push('\r'), 't' => s.push('\t'),
            'u' => {
                let hex: String = (0..4).map(|_| it.next()).collect::<Option<String>>()?;
                let cp = u32::from_str_radix(&hex, 16).ok()?;
                s.push(char::from_u32(cp)?);
            }
            _ => return None,
        }
    }
    Some(s)
}

fn is_convertible(email_count: usize, has_mail_store: bool) -> bool { email_count > 0 || has_mail_store }

/// True if Mail/ or ImapMail/ recursively contains a non-dir, non-.msf file.
fn has_mail_store(profile_dir: &std::path::Path) -> bool {
    ["Mail", "ImapMail"].iter().any(|s| dir_has_mailbox_file(&profile_dir.join(s)))
}

fn dir_has_mailbox_file(dir: &std::path::Path) -> bool {
    let Ok(rd) = std::fs::read_dir(dir) else { return false; };
    for entry in rd.flatten() {
        let p = entry.path();
        if p.is_dir() { if dir_has_mailbox_file(&p) { return true; } }
        else if p.extension().and_then(|e| e.to_str()) != Some("msf") { return true; }
    }
    false
}

#[tauri::command]
fn list_thunderbird_profiles() -> Result<Vec<ProfileEntry>, String> {
    let appdata = match std::env::var_os("APPDATA") {
        Some(v) => std::path::PathBuf::from(v),
        None => return Ok(vec![]),
    };
    let tb = appdata.join("Thunderbird");
    let ini = tb.join("profiles.ini");
    let content = match std::fs::read_to_string(&ini) {
        Ok(c) => c,
        Err(_) => return Ok(vec![]),
    };
    let mut out = Vec::new();
    for e in parse_profiles_ini(&content, &tb) {
        let dir = std::path::Path::new(&e.path);
        let accounts = std::fs::read_to_string(dir.join("prefs.js"))
            .map(|p| parse_identity_emails(&p))
            .unwrap_or_default();
        let convertible = is_convertible(accounts.len(), has_mail_store(dir));
        out.push(ProfileEntry { name: e.name, path: e.path, is_default: e.is_default, accounts, convertible });
    }
    Ok(out)
}

/// Returns %APPDATA%\Thunderbird\Profiles when it exists, else None. Windows-only app.
#[tauri::command]
fn default_thunderbird_profiles_dir() -> Result<Option<String>, String> {
    let appdata = match std::env::var_os("APPDATA") {
        Some(v) => std::path::PathBuf::from(v),
        None => return Ok(None),
    };
    let dir = appdata.join("Thunderbird").join("Profiles");
    Ok(if dir.is_dir() { Some(dir.to_string_lossy().to_string()) } else { None })
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_shell::init())
        .manage(ConvertState::default())
        .manage(ScanState::default())
        .invoke_handler(tauri::generate_handler![
            check_engine_version,
            scan_sample,
            discover_profile,
            list_mbox_in_dir,
            stat_files,
            start_convert,
            start_scan,
            cancel_convert,
            open_folder,
            open_junk_help,
            preview_colours,
            apply_colours,
            apply_colours_plan,
            default_thunderbird_profiles_dir,
            list_thunderbird_profiles
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[cfg(test)]
mod profile_scan_tests {
    use super::*;
    use std::fs;

    #[test]
    fn parse_emails_dedupes_case_insensitively_keeping_first() {
        let p = "\
user_pref(\"mail.identity.id1.useremail\", \"Alice@EXAMPLE.com\");\n\
user_pref(\"mail.identity.id2.useremail\", \"alice@example.com\");\n\
user_pref(\"mail.identity.id3.useremail\", \"bob@example.test\");\n\
user_pref(\"mail.identity.id4.fullName\", \"Not An Email\");\n";
        assert_eq!(parse_identity_emails(p), vec!["Alice@EXAMPLE.com", "bob@example.test"]);
    }

    #[test]
    fn parse_emails_unescapes_and_skips_malformed() {
        let p = "\
user_pref(\"mail.identity.id1.useremail\", \"al\\u00e6ce@example.com\");\n\
user_pref(\"mail.identity.id2.useremail\", \"bad\\xZZ@example.com\");\n\
user_pref(\"mail.identity.id3.useremail\", \"   \");\n";
        assert_eq!(parse_identity_emails(p), vec!["alæce@example.com"]); // id2 skipped (bad escape), id3 empty
    }

    #[test]
    fn convertible_truth_table() {
        assert!(is_convertible(1, false));
        assert!(is_convertible(0, true));
        assert!(!is_convertible(0, false));
    }

    #[test]
    fn mail_store_detection() {
        let root = std::env::temp_dir().join(format!("cm-mailstore-{}", std::process::id()));
        let _ = fs::remove_dir_all(&root);
        let a = root.join("a"); fs::create_dir_all(a.join("Mail/Local Folders")).unwrap();
        fs::write(a.join("Mail/Local Folders/Inbox"), b"x").unwrap();
        assert!(has_mail_store(&a));
        let b = root.join("b"); fs::create_dir_all(b.join("Mail/Local Folders")).unwrap();
        fs::write(b.join("Mail/Local Folders/Inbox.msf"), b"x").unwrap();
        assert!(!has_mail_store(&b));
        let c = root.join("c"); fs::create_dir_all(c.join("Mail/Local Folders/Archives.sbd")).unwrap();
        fs::write(c.join("Mail/Local Folders/Archives.sbd/2024"), b"x").unwrap();
        assert!(has_mail_store(&c));
        let d = root.join("d"); fs::create_dir_all(d.join("ImapMail/acct")).unwrap();
        fs::write(d.join("ImapMail/acct/INBOX"), b"x").unwrap();
        assert!(has_mail_store(&d));
        let e = root.join("e"); fs::create_dir_all(e.join("Mail")).unwrap(); fs::create_dir_all(e.join("ImapMail")).unwrap();
        assert!(!has_mail_store(&e));
        let _ = fs::remove_dir_all(&root);
    }
}

#[cfg(test)]
mod profiles_ini_tests {
    use super::*;
    use std::path::Path;

    const INI: &str = "\
[Install4F96D1932A9F858E]\n\
Default=Profiles/abc.default-release\n\
\n\
[Profile0]\n\
Name=default-release\n\
IsRelative=1\n\
Path=Profiles/abc.default-release\n\
\n\
[Profile1]\n\
Name=dev\n\
IsRelative=1\n\
Path=Profiles/xyz.dev\n";

    #[test]
    fn parses_profiles_and_marks_default() {
        let out = parse_profiles_ini(INI, Path::new("C:/TB"));
        assert_eq!(out.len(), 2);
        let def = out.iter().find(|p| p.is_default).unwrap();
        assert_eq!(def.name, "default-release");
        assert!(def.path.ends_with("Profiles/abc.default-release") || def.path.ends_with("Profiles\\abc.default-release"));
        assert!(!out.iter().find(|p| p.name == "dev").unwrap().is_default);
    }

    #[test]
    fn empty_content_yields_empty() {
        assert!(parse_profiles_ini("", Path::new("C:/x")).is_empty());
    }
}

#[cfg(test)]
mod tests {
    use super::drain_lines;

    #[test]
    fn drain_lines_splits_batched_records_and_keeps_partial() {
        // Two complete records arrive batched in one chunk, plus a partial third
        // with no terminating newline.
        let mut buf = String::from("{\"type\":\"started\"}\n{\"type\":\"progress\"}\n{\"type\":\"do");
        let lines = drain_lines(&mut buf);
        assert_eq!(lines, vec!["{\"type\":\"started\"}", "{\"type\":\"progress\"}"]);
        // The incomplete record is left in the buffer for the next chunk.
        assert_eq!(buf, "{\"type\":\"do");

        // The rest of the record arrives; now it drains as one complete line.
        buf.push_str("ne\"}\n");
        let lines = drain_lines(&mut buf);
        assert_eq!(lines, vec!["{\"type\":\"done\"}"]);
        assert_eq!(buf, "");
    }

    #[test]
    fn drain_lines_strips_crlf_and_skips_empty_lines() {
        let mut buf = String::from("a\r\n\r\nb\n");
        let lines = drain_lines(&mut buf);
        assert_eq!(lines, vec!["a", "b"]);
        assert_eq!(buf, "");
    }
}
