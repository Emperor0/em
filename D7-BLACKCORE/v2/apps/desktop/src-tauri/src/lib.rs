mod device;
mod legacy;
mod mission;
mod modes;
mod runtime;

use device::DeviceSnapshot;
use modes::ModeDefinition;
use runtime::{ApprovalRequest,RuntimeMission,SharedRuntime};
use serde::Serialize;
use std::{sync::atomic::{AtomicBool,Ordering},time::Instant};
use tauri::State;

struct AppState { paused:AtomicBool, started:Instant }

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
struct RuntimeStatus { paused:bool, uptime_seconds:u64, native:bool, version:&'static str }

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
struct CapabilitySnapshot { id:&'static str, label:&'static str, state:&'static str, evidence:String }

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
struct RuntimeOverview { runtime:RuntimeStatus, device:DeviceSnapshot, capabilities:Vec<CapabilitySnapshot> }

fn status(s:&State<AppState>)->RuntimeStatus {
    RuntimeStatus { paused:s.paused.load(Ordering::SeqCst), uptime_seconds:s.started.elapsed().as_secs(), native:true, version:"2.0.0-dev.10" }
}

#[tauri::command]
fn set_runtime_paused(s:State<AppState>,paused:bool)->RuntimeStatus { s.paused.store(paused,Ordering::SeqCst);status(&s) }
#[tauri::command]
fn get_modes()->Vec<ModeDefinition> { modes::all_modes() }
#[tauri::command]
fn submit_mission(s:State<AppState>,r:State<SharedRuntime>,goal:String)->Result<RuntimeMission,String> {
    if s.paused.load(Ordering::SeqCst) { return Err("RUNTIME_PAUSED".into()) }
    r.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.submit(goal)
}
#[tauri::command]
fn get_active_mission(r:State<SharedRuntime>)->Result<Option<RuntimeMission>,String> { Ok(r.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.mission.clone()) }
#[tauri::command]
fn get_pending_approvals(r:State<SharedRuntime>)->Result<Vec<ApprovalRequest>,String> { Ok(r.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.pending()) }
#[tauri::command]
fn decide_approval(r:State<SharedRuntime>,approval_id:String,approve:bool)->Result<RuntimeMission,String> { r.0.lock().map_err(|_|"RUNTIME_LOCK_POISONED".to_string())?.decide(&approval_id,approve) }

#[tauri::command]
fn get_runtime_overview(s:State<AppState>)->RuntimeOverview {
    let d=device::collect_device_snapshot();
    let agent_state=if d.legacy_agent.request_pipe_available&&d.legacy_agent.event_pipe_available { "verified" } else if d.legacy_agent.source_available { "partial" } else { "unavailable" };
    let agent_evidence=if agent_state=="verified" {
        format!("D7 Agent duplex pipes online: {} + {}",d.legacy_agent.request_pipe,d.legacy_agent.event_pipe)
    } else if d.legacy_agent.source_available {
        format!("Legacy source found at {}; one or both pipes are offline.",d.legacy_agent.source_path)
    } else {
        format!("Legacy source not found at {}.",d.legacy_agent.source_path)
    };
    let protocol_state=if d.legacy_agent.protocol.source_available { "verified" } else { "unavailable" };
    let protocol_evidence=if d.legacy_agent.protocol.source_available {
        format!("pipe_server.py inspected: {} lines, {} handlers, {} commands, fingerprint {}",d.legacy_agent.protocol.line_count,d.legacy_agent.protocol.handler_candidates.len(),d.legacy_agent.protocol.command_candidates.len(),d.legacy_agent.protocol.source_fingerprint.clone().unwrap_or_default())
    } else { "pipe_server.py is not readable at discovered legacy source paths.".into() };
    let gate_state=if d.legacy_agent.protocol.probe_ready { "verified" } else if d.legacy_agent.protocol.source_available { "partial" } else { "unavailable" };
    let gate_evidence=format!("encoding={} discriminator={} framing={} safe_probes={} confidence={}%; {}",
        d.legacy_agent.protocol.encoding,
        d.legacy_agent.protocol.discriminator_field.clone().unwrap_or_else(||"none".into()),
        d.legacy_agent.protocol.framing_mode,
        d.legacy_agent.protocol.safe_probe_candidates.join(","),
        d.legacy_agent.protocol.confidence,
        d.legacy_agent.protocol.probe_reason);

    let mut c=vec![
        CapabilitySnapshot{id:"runtime.native",label:"Windows Native Runtime",state:"verified",evidence:"Tauri/Rust host answered directly.".into()},
        CapabilitySnapshot{id:"modes.native",label:"Native Mode Registry",state:"verified",evidence:format!("{} operating modes registered in Rust runtime.",modes::all_modes().len())},
        CapabilitySnapshot{id:"device.snapshot",label:"Live Device Twin",state:"verified",evidence:format!("{} processes sampled.",d.process_count)},
        CapabilitySnapshot{id:"action.stop",label:"Emergency Automation Stop",state:"verified",evidence:"Native pause flag blocks mission submission.".into()},
        CapabilitySnapshot{id:"mission.runtime",label:"Mission Runtime + Journal",state:"verified",evidence:"Lifecycle journal persists under LOCALAPPDATA/D7_BLACKCORE.".into()},
        CapabilitySnapshot{id:"approval.queue",label:"Approval Queue",state:"verified",evidence:"Existing-target mutation waits for explicit decision.".into()},
        CapabilitySnapshot{id:"checkpoint.file",label:"Pre-mutation Checkpoint",state:"verified",evidence:"Approved overwrite creates backup first.".into()},
        CapabilitySnapshot{id:"legacy.d7_agent",label:"D7 Agent Core Bridge",state:agent_state,evidence:agent_evidence},
        CapabilitySnapshot{id:"legacy.protocol_inspector",label:"D7 Protocol Inspector",state:protocol_state,evidence:protocol_evidence},
        CapabilitySnapshot{id:"legacy.protocol_gate",label:"D7 Read-only Probe Gate",state:gate_state,evidence:gate_evidence},
        CapabilitySnapshot{id:"windows.write",label:"General Windows Mutations",state:"partial",evidence:"Constrained Desktop adapter only.".into()},
        CapabilitySnapshot{id:"browser.chrome",label:"Authenticated Chrome Operator",state:"untested",evidence:"Pending verified local D7 IPC transport.".into()},
        CapabilitySnapshot{id:"builder.native",label:"Builder Studio Native Host",state:"partial",evidence:"Native host exists; IDE surface pending.".into()},
    ];
    c.push(CapabilitySnapshot{id:"device.nvidia",label:"NVIDIA Telemetry",state:if d.gpu.is_some(){"verified"}else{"unavailable"},evidence:if d.gpu.is_some(){"nvidia-smi telemetry parsed.".into()}else{"nvidia-smi unavailable.".into()}});
    c.push(CapabilitySnapshot{id:"legacy.governor",label:"D7 Performance Governor Bridge",state:if d.governor.available{"verified"}else{"unavailable"},evidence:d.governor.source.clone().unwrap_or_else(||"C:\\ProgramData\\D7PerformanceGovernor\\status.json not found.".into())});
    c.push(CapabilitySnapshot{id:"models.opencode",label:"OpenCode Local Provider",state:if d.open_code.available{"verified"}else{"unavailable"},evidence:format!("Health probe: {}",d.open_code.endpoint)});
    RuntimeOverview { runtime:status(&s), device:d, capabilities:c }
}

#[cfg_attr(mobile,tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(AppState{paused:AtomicBool::new(false),started:Instant::now()})
        .manage(SharedRuntime::new())
        .invoke_handler(tauri::generate_handler![get_runtime_overview,get_modes,set_runtime_paused,submit_mission,get_active_mission,get_pending_approvals,decide_approval])
        .run(tauri::generate_context!())
        .expect("error while running D7 BLACKCORE");
}
