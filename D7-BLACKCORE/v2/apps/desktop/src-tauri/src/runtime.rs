use crate::mission::{self, MissionResult};
use serde::Serialize;
use std::{env, fs::{self, OpenOptions}, io::Write, path::{Path, PathBuf}, sync::Mutex, time::{SystemTime, UNIX_EPOCH}};

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalRequest {
    pub id: String,
    pub mission_id: String,
    pub goal: String,
    pub action: String,
    pub reason: String,
    pub risk: String,
    pub target_path: String,
    pub status: String,
    pub created_at: u64,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeMission {
    pub id: String,
    pub goal: String,
    pub status: String,
    pub result: Option<MissionResult>,
    pub approval_id: Option<String>,
    pub updated_at: u64,
}

#[derive(Debug)]
pub struct RuntimeStore {
    root: PathBuf,
    pub mission: Option<RuntimeMission>,
    pub approvals: Vec<ApprovalRequest>,
}

fn now() -> u64 { SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs() }
fn id(prefix: &str) -> String { format!("{}-{}", prefix, SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos()) }

impl RuntimeStore {
    pub fn new() -> Self {
        let root = env::var("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|_| env::temp_dir()).join("D7_BLACKCORE");
        let _ = fs::create_dir_all(root.join("journal"));
        let _ = fs::create_dir_all(root.join("checkpoints"));
        Self { root, mission: None, approvals: Vec::new() }
    }

    fn journal(&self, kind: &str, value: &serde_json::Value) {
        let path = self.root.join("journal").join("events.ndjson");
        if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(path) {
            let record = serde_json::json!({"at":now(),"kind":kind,"value":value});
            let _ = writeln!(f, "{}", record);
        }
    }

    pub fn submit(&mut self, goal: String) -> Result<RuntimeMission, String> {
        let mid = id("mission");
        let target = mission::target_path_for_goal(&goal)?;
        let mut item = RuntimeMission { id: mid.clone(), goal: goal.clone(), status: "planning".into(), result: None, approval_id: None, updated_at: now() };
        self.mission = Some(item.clone());
        self.journal("mission.planning", &serde_json::json!({"missionId":mid,"goal":goal,"target":target}));

        if target.exists() {
            let aid = id("approval");
            let approval = ApprovalRequest {
                id: aid.clone(), mission_id: mid.clone(), goal: goal.clone(), action: "overwrite_existing_file".into(),
                reason: "الهدف موجود مسبقًا؛ يلزم موافقة قبل الاستبدال.".into(), risk: "medium".into(),
                target_path: target.display().to_string(), status: "pending".into(), created_at: now(),
            };
            self.approvals.push(approval.clone());
            item.status = "waiting_approval".into(); item.approval_id = Some(aid); item.updated_at = now();
            self.mission = Some(item.clone());
            self.journal("approval.requested", &serde_json::to_value(&approval).unwrap_or_default());
            return Ok(item);
        }

        item.status = "running".into(); self.mission = Some(item.clone());
        self.journal("mission.running", &serde_json::json!({"missionId":mid}));
        let result = mission::execute_fast_path_with_policy(&goal, false, None)?;
        item.status = "verifying".into(); self.mission = Some(item.clone());
        self.journal("mission.verifying", &serde_json::json!({"missionId":mid}));
        item.status = if result.verified { "completed".into() } else { "failed".into() };
        item.result = Some(result); item.updated_at = now(); self.mission = Some(item.clone());
        self.journal("mission.completed", &serde_json::to_value(&item).unwrap_or_default());
        Ok(item)
    }

    pub fn pending(&self) -> Vec<ApprovalRequest> { self.approvals.iter().filter(|x| x.status=="pending").cloned().collect() }

    pub fn decide(&mut self, approval_id: &str, approve: bool) -> Result<RuntimeMission, String> {
        let idx = self.approvals.iter().position(|x| x.id==approval_id).ok_or("APPROVAL_NOT_FOUND")?;
        if self.approvals[idx].status != "pending" { return Err("APPROVAL_ALREADY_DECIDED".into()); }
        let approval = self.approvals[idx].clone();
        self.approvals[idx].status = if approve { "approved".into() } else { "rejected".into() };
        self.journal("approval.decided", &serde_json::json!({"approvalId":approval_id,"approved":approve}));

        let mut mission = self.mission.clone().ok_or("MISSION_NOT_FOUND")?;
        if mission.id != approval.mission_id { return Err("MISSION_APPROVAL_MISMATCH".into()); }
        if !approve {
            mission.status = "cancelled".into(); mission.updated_at = now(); self.mission = Some(mission.clone());
            self.journal("mission.cancelled", &serde_json::to_value(&mission).unwrap_or_default());
            return Ok(mission);
        }

        let target = PathBuf::from(&approval.target_path);
        let checkpoint = self.make_checkpoint(&mission.id, &target)?;
        mission.status = "running".into(); mission.updated_at = now(); self.mission = Some(mission.clone());
        self.journal("checkpoint.created", &serde_json::json!({"missionId":mission.id,"checkpoint":checkpoint}));
        let result = mission::execute_fast_path_with_policy(&mission.goal, true, Some(Path::new(&checkpoint)))?;
        mission.status = "verifying".into(); self.mission = Some(mission.clone());
        mission.status = if result.verified { "completed".into() } else { "failed".into() };
        mission.result = Some(result); mission.updated_at = now(); self.mission = Some(mission.clone());
        self.journal("mission.completed", &serde_json::to_value(&mission).unwrap_or_default());
        Ok(mission)
    }

    fn make_checkpoint(&self, mission_id: &str, target: &Path) -> Result<String, String> {
        let dir = self.root.join("checkpoints").join(mission_id);
        fs::create_dir_all(&dir).map_err(|e| format!("CHECKPOINT_DIR_FAILED:{e}"))?;
        let backup = dir.join(target.file_name().ok_or("TARGET_FILENAME_MISSING")?);
        fs::copy(target, &backup).map_err(|e| format!("CHECKPOINT_COPY_FAILED:{e}"))?;
        Ok(backup.display().to_string())
    }
}

pub struct SharedRuntime(pub Mutex<RuntimeStore>);
impl SharedRuntime { pub fn new() -> Self { Self(Mutex::new(RuntimeStore::new())) } }
