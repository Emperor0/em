mod device;
mod mission;

use device::DeviceSnapshot;
use mission::MissionResult;
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

fn status(state: &State<AppState>) -> RuntimeStatus {
    RuntimeStatus { paused: state.paused.load(Ordering::SeqCst), uptime_seconds: state.started.elapsed().as_secs(), native: true, version: "2.0.0-dev.5" }
}

#[tauri::command]
fn set_runtime_paused(state: State<AppState>, paused: bool) -> RuntimeStatus {
    state.paused.store(paused, Ordering::SeqCst);
    status(&state)
}

#[tauri::command]
fn run_fast_path_mission(state: State<AppState>, goal: String) -> Result<MissionResult, String> {
    if state.paused.load(Ordering::SeqCst) { return Err("RUNTIME_PAUSED".into()); }
    mission::execute_fast_path(&goal)
}

#[tauri::command]
fn get_runtime_overview(state: State<AppState>) -> RuntimeOverview {
    let device = device::collect_device_snapshot();
    let mut capabilities = vec![
        CapabilitySnapshot { id: "runtime.native", label: "Windows Native Runtime", state: "verified", evidence: "Tauri/Rust host answered this request directly.".into() },
        CapabilitySnapshot { id: "device.snapshot", label: "Live Device Twin", state: "verified", evidence: format!("{} processes sampled from Windows runtime.", device.process_count) },
        CapabilitySnapshot { id: "action.stop", label: "Emergency Automation Stop", state: "verified", evidence: "Kernel pause flag is controlled by native AppState.".into() },
        CapabilitySnapshot { id: "files.fastpath", label: "Verified Desktop File Fast Path", state: "verified", evidence: "Native Rust adapter creates a Desktop folder/file and verifies content by read-back. Existing targets are blocked for approval.".into() },
        CapabilitySnapshot { id: "windows.write", label: "General Windows Mutations", state: "partial", evidence: "Only the constrained Desktop file Fast Path is enabled; broad mutations remain gated.".into() },
        CapabilitySnapshot { id: "browser.chrome", label: "Authenticated Chrome Operator", state: "untested", evidence: "Scheduled after local mission execution; no credential extraction will be used.".into() },
        CapabilitySnapshot { id: "builder.native", label: "Builder Studio Native Host", state: "partial", evidence: "Desktop host exists; IDE/tooling surface is not yet complete.".into() },
    ];
    if device.gpu.is_some() {
        capabilities.push(CapabilitySnapshot { id: "device.nvidia", label: "NVIDIA Telemetry", state: "verified", evidence: "nvidia-smi returned parseable telemetry.".into() });
    } else {
        capabilities.push(CapabilitySnapshot { id: "device.nvidia", label: "NVIDIA Telemetry", state: "unavailable", evidence: "nvidia-smi did not return telemetry in this snapshot.".into() });
    }
    capabilities.push(CapabilitySnapshot {
        id: "legacy.governor", label: "D7 Performance Governor Bridge",
        state: if device.governor.available { "verified" } else { "unavailable" },
        evidence: device.governor.source.clone().unwrap_or_else(|| "C:\\ProgramData\\D7PerformanceGovernor\\status.json not found.".into()),
    });
    capabilities.push(CapabilitySnapshot {
        id: "models.opencode", label: "OpenCode Local Provider",
        state: if device.open_code.available { "verified" } else { "unavailable" },
        evidence: format!("Health probe: {}", device.open_code.endpoint),
    });

    RuntimeOverview { runtime: status(&state), device, capabilities }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(AppState { paused: AtomicBool::new(false), started: Instant::now() })
        .invoke_handler(tauri::generate_handler![get_runtime_overview, set_runtime_paused, run_fast_path_mission])
        .run(tauri::generate_context!())
        .expect("error while running D7 BLACKCORE");
}
