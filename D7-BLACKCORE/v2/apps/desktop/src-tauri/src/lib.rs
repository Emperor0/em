mod device;
mod mission;
mod runtime;

use device::DeviceSnapshot;
use runtime::{ApprovalRequest, RuntimeMission, SharedRuntime};
use serde::Serialize;
use std::{sync::atomic::{AtomicBool, Ordering}, time::Instant};
use tauri::State;

struct AppState { paused: AtomicBool, started: Instant }

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeStatus { paused: bool, uptime_seconds: u64, native: bool, version: &'static str }
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct CapabilitySnapshot { id: &'static str, label: &'static str, state: &'static str, evidence: String }
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeOverview { runtime: RuntimeStatus, device: DeviceSnapshot, capabilities: Vec<CapabilitySnapshot> }

fn status(state:&State<AppState>)->RuntimeStatus{RuntimeStatus{paused:state.paused.load(Ordering::SeqCst),uptime_seconds:state.started.elapsed().as_secs(),native:true,version:"2.0.0-dev.6"}}

#[tauri::command]
fn set_runtime_paused(state:State<AppState>,paused:bool)->RuntimeStatus{state.paused.store(paused,Ordering::SeqCst);status(&state)}

#[tauri::command]
fn submit_mission(state:State<AppState>,runtime:State<SharedRuntime>,goal:String)->Result<RuntimeMission,String>{if state.paused.load(Ordering::SeqCst){return Err("RUNTIME_PAUSED".into())}runtime.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.submit(goal)}

#[tauri::command]
fn get_active_mission(runtime:State<SharedRuntime>)->Result<Option<RuntimeMission>,String>{Ok(runtime.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.mission.clone())}

#[tauri::command]
fn get_pending_approvals(runtime:State<SharedRuntime>)->Result<Vec<ApprovalRequest>,String>{Ok(runtime.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.pending())}

#[tauri::command]
fn decide_approval(runtime:State<SharedRuntime>,approval_id:String,approve:bool)->Result<RuntimeMission,String>{runtime.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.decide(&approval_id,approve)}

#[tauri::command]
fn get_runtime_overview(state:State<AppState>)->RuntimeOverview{
 let device=device::collect_device_snapshot();
 let mut capabilities=vec![
  CapabilitySnapshot{id:"runtime.native",label:"Windows Native Runtime",state:"verified",evidence:"Tauri/Rust host answered this request directly.".into()},
  CapabilitySnapshot{id:"device.snapshot",label:"Live Device Twin",state:"verified",evidence:format!("{} processes sampled from Windows runtime.",device.process_count)},
  CapabilitySnapshot{id:"action.stop",label:"Emergency Automation Stop",state:"verified",evidence:"Native pause flag blocks mission submission.".into()},
  CapabilitySnapshot{id:"mission.runtime",label:"Mission Runtime + Journal",state:"verified",evidence:"Mission lifecycle is persisted to LOCALAPPDATA/D7_BLACKCORE/journal/events.ndjson.".into()},
  CapabilitySnapshot{id:"approval.queue",label:"Approval Queue",state:"verified",evidence:"Existing-target mutations stop in waiting_approval until explicit decision.".into()},
  CapabilitySnapshot{id:"checkpoint.file",label:"Pre-mutation Checkpoint",state:"verified",evidence:"Approved overwrite creates a local backup before mutation.".into()},
  CapabilitySnapshot{id:"windows.write",label:"General Windows Mutations",state:"partial",evidence:"Constrained Desktop file adapter only; broad adapters remain gated.".into()},
  CapabilitySnapshot{id:"browser.chrome",label:"Authenticated Chrome Operator",state:"untested",evidence:"Next execution milestone; no credential extraction.".into()},
  CapabilitySnapshot{id:"builder.native",label:"Builder Studio Native Host",state:"partial",evidence:"Native shell exists; IDE/tooling surface pending.".into()},
 ];
 capabilities.push(CapabilitySnapshot{id:"device.nvidia",label:"NVIDIA Telemetry",state:if device.gpu.is_some(){"verified"}else{"unavailable"},evidence:if device.gpu.is_some(){"nvidia-smi returned parseable telemetry.".into()}else{"nvidia-smi did not return telemetry.".into()}});
 capabilities.push(CapabilitySnapshot{id:"legacy.governor",label:"D7 Performance Governor Bridge",state:if device.governor.available{"verified"}else{"unavailable"},evidence:device.governor.source.clone().unwrap_or_else(||"C:\\ProgramData\\D7PerformanceGovernor\\status.json not found.".into())});
 capabilities.push(CapabilitySnapshot{id:"models.opencode",label:"OpenCode Local Provider",state:if device.open_code.available{"verified"}else{"unavailable"},evidence:format!("Health probe: {}",device.open_code.endpoint)});
 RuntimeOverview{runtime:status(&state),device,capabilities}
}

#[cfg_attr(mobile,tauri::mobile_entry_point)]
pub fn run(){tauri::Builder::default().manage(AppState{paused:AtomicBool::new(false),started:Instant::now()}).manage(SharedRuntime::new()).invoke_handler(tauri::generate_handler![get_runtime_overview,set_runtime_paused,submit_mission,get_active_mission,get_pending_approvals,decide_approval]).run(tauri::generate_context!()).expect("error while running D7 BLACKCORE");}
