mod alerts;
mod crash_recovery;
mod events;
mod logging;
mod performance;
mod profiles;
mod scenes;
mod settings;
mod tiktok;
mod ticker;
mod updater;
mod virtual_output;

use serde::Serialize;
use std::sync::Arc;
use parking_lot::RwLock;

#[derive(Default)]
struct AppState {
    events: Arc<RwLock<events::EventEngine>>,
}

#[derive(Serialize)]
struct HealthSnapshot {
    app_version: &'static str,
    mode: &'static str,
    tiktok: &'static str,
    virtual_camera: &'static str,
    virtual_audio: &'static str,
}

#[tauri::command]
fn health_snapshot() -> HealthSnapshot {
    HealthSnapshot { app_version: env!("CARGO_PKG_VERSION"), mode:"Desktop", tiktok:"Disconnected", virtual_camera:"Unavailable", virtual_audio:"Unavailable" }
}

#[tauri::command]
fn inject_test_event(kind: String, state: tauri::State<AppState>) -> Result<(), String> {
    let event = events::LiveEvent::mock(&kind);
    state.events.write().ingest(event).map_err(|e| e.to_string())
}

#[tauri::command]
async fn check_for_updates() -> Result<String, String> {
    updater::human_check_message().await.map_err(|e| e.to_string())
}

pub fn run() {
    logging::init();
    tauri::Builder::default()
        .manage(AppState::default())
        .invoke_handler(tauri::generate_handler![health_snapshot, inject_test_event, check_for_updates])
        .run(tauri::generate_context!())
        .expect("error while running D7 LIVE");
}
