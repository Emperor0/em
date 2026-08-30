use serde::Serialize;
use std::{env, fs, path::{Path, PathBuf}, time::{SystemTime, UNIX_EPOCH}};

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MissionStepResult {
    pub title: String,
    pub status: String,
    pub evidence: Option<String>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MissionEvidence {
    pub kind: String,
    pub claim: String,
    pub value: String,
    pub verified: bool,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MissionResult {
    pub id: String,
    pub goal: String,
    pub status: String,
    pub quality_score: u8,
    pub target_path: Option<String>,
    pub verified: bool,
    pub steps: Vec<MissionStepResult>,
    pub evidence: Vec<MissionEvidence>,
}

fn mission_id() -> String {
    let nanos = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos();
    format!("mission-{nanos}")
}

fn valid_component(value: &str) -> bool {
    let v = value.trim();
    !v.is_empty()
        && v.len() <= 120
        && v != "."
        && v != ".."
        && !v.chars().any(|c| matches!(c, '\\' | '/' | ':' | '*' | '?' | '"' | '<' | '>' | '|'))
}

fn extract_between<'a>(text: &'a str, start: &str, stops: &[&str]) -> Option<&'a str> {
    let offset = text.find(start)? + start.len();
    let tail = &text[offset..];
    let mut end = tail.len();
    for stop in stops {
        if let Some(i) = tail.find(stop) { end = end.min(i); }
    }
    let value = tail[..end].trim();
    (!value.is_empty()).then_some(value)
}

fn parse_goal(goal: &str) -> Result<(String, String, String), String> {
    let folder = extract_between(goal, "باسم ", &["،", ",", " وداخله", " وداخله ملف", "\n"])
        .or_else(|| extract_between(goal, "folder named ", &[",", " and", "\n"]))
        .ok_or_else(|| "FAST_PATH_FOLDER_NAME_NOT_FOUND".to_string())?;

    let file = extract_between(goal, "ملف ", &[" واكتب", "،", ",", "\n"])
        .or_else(|| extract_between(goal, "file ", &[" and write", ",", "\n"]))
        .ok_or_else(|| "FAST_PATH_FILE_NAME_NOT_FOUND".to_string())?;

    let content = goal.split_once("اكتب فيه:").map(|(_, v)| v.trim())
        .or_else(|| goal.split_once("write:").map(|(_, v)| v.trim()))
        .ok_or_else(|| "FAST_PATH_CONTENT_NOT_FOUND".to_string())?;

    if !valid_component(folder) { return Err("UNSAFE_FOLDER_NAME".into()); }
    if !valid_component(file) { return Err("UNSAFE_FILE_NAME".into()); }
    if content.len() > 100_000 { return Err("CONTENT_TOO_LARGE".into()); }

    Ok((folder.to_string(), file.to_string(), content.to_string()))
}

fn desktop_root() -> Result<PathBuf, String> {
    let home = env::var("USERPROFILE").map_err(|_| "USERPROFILE_NOT_FOUND".to_string())?;
    let desktop = Path::new(&home).join("Desktop");
    if !desktop.exists() { return Err("DESKTOP_NOT_FOUND".into()); }
    Ok(desktop)
}

fn execute_at_root(root: &Path, goal: &str) -> Result<MissionResult, String> {
    let id = mission_id();
    let (folder_name, file_name, content) = parse_goal(goal)?;
    let folder = root.join(folder_name);
    let file = folder.join(file_name);

    let mut steps = vec![
        MissionStepResult { title: "فهم الهدف وتقييد المسار".into(), status: "completed".into(), evidence: Some(root.display().to_string()) },
        MissionStepResult { title: "إنشاء المجلد".into(), status: "pending".into(), evidence: None },
        MissionStepResult { title: "كتابة الملف".into(), status: "pending".into(), evidence: None },
        MissionStepResult { title: "إعادة قراءة الملف والتحقق".into(), status: "pending".into(), evidence: None },
    ];

    if file.exists() {
        return Err(format!("APPROVAL_REQUIRED_EXISTING_TARGET:{}", file.display()));
    }

    fs::create_dir_all(&folder).map_err(|e| format!("CREATE_DIR_FAILED:{e}"))?;
    steps[1].status = "completed".into();
    steps[1].evidence = Some(folder.display().to_string());

    fs::write(&file, content.as_bytes()).map_err(|e| format!("WRITE_FILE_FAILED:{e}"))?;
    steps[2].status = "completed".into();
    steps[2].evidence = Some(format!("{} bytes", content.as_bytes().len()));

    let read_back = fs::read_to_string(&file).map_err(|e| format!("VERIFY_READ_FAILED:{e}"))?;
    let verified = read_back == content;
    if !verified { return Err("VERIFY_CONTENT_MISMATCH".into()); }
    steps[3].status = "completed".into();
    steps[3].evidence = Some(read_back.clone());

    Ok(MissionResult {
        id,
        goal: goal.to_string(),
        status: "completed".into(),
        quality_score: 100,
        target_path: Some(file.display().to_string()),
        verified,
        steps,
        evidence: vec![
            MissionEvidence { kind: "file".into(), claim: "Target file exists".into(), value: file.display().to_string(), verified: file.exists() },
            MissionEvidence { kind: "test".into(), claim: "Read-back content matches requested content".into(), value: read_back, verified },
        ],
    })
}

pub fn execute_fast_path(goal: &str) -> Result<MissionResult, String> {
    execute_at_root(&desktop_root()?, goal)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn unique_root() -> PathBuf {
        env::temp_dir().join(format!("blackcore-fastpath-test-{}", mission_id()))
    }

    #[test]
    fn parses_arabic_fast_path_goal() {
        let goal = "أنشئ مجلد على سطح المكتب باسم BLACKCORE_TEST، وداخله ملف result.txt واكتب فيه: D7 BLACKCORE WORKS";
        let parsed = parse_goal(goal).expect("goal should parse");
        assert_eq!(parsed.0, "BLACKCORE_TEST");
        assert_eq!(parsed.1, "result.txt");
        assert_eq!(parsed.2, "D7 BLACKCORE WORKS");
    }

    #[test]
    fn rejects_path_traversal_components() {
        assert!(!valid_component(".."));
        assert!(!valid_component("a/b"));
        assert!(!valid_component("a\\b"));
        assert!(!valid_component("C:bad"));
    }

    #[test]
    fn creates_writes_and_verifies_real_file() {
        let root = unique_root();
        fs::create_dir_all(&root).expect("temp root");
        let goal = "أنشئ مجلد على سطح المكتب باسم BLACKCORE_TEST، وداخله ملف result.txt واكتب فيه: D7 BLACKCORE WORKS";
        let result = execute_at_root(&root, goal).expect("mission should complete");
        assert_eq!(result.status, "completed");
        assert!(result.verified);
        assert_eq!(result.quality_score, 100);
        let target = result.target_path.as_ref().expect("target path");
        assert_eq!(fs::read_to_string(target).unwrap(), "D7 BLACKCORE WORKS");
        fs::remove_dir_all(&root).expect("cleanup");
    }

    #[test]
    fn refuses_overwrite_without_approval() {
        let root = unique_root();
        let folder = root.join("BLACKCORE_TEST");
        fs::create_dir_all(&folder).unwrap();
        fs::write(folder.join("result.txt"), "ORIGINAL").unwrap();
        let goal = "أنشئ مجلد على سطح المكتب باسم BLACKCORE_TEST، وداخله ملف result.txt واكتب فيه: REPLACEMENT";
        let error = execute_at_root(&root, goal).unwrap_err();
        assert!(error.starts_with("APPROVAL_REQUIRED_EXISTING_TARGET:"));
        assert_eq!(fs::read_to_string(folder.join("result.txt")).unwrap(), "ORIGINAL");
        fs::remove_dir_all(&root).expect("cleanup");
    }
}
