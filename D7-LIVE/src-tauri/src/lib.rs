mod alerts;
mod core;
mod crash_recovery;
mod events;
mod logging;
mod media;
mod media_inputs;
mod performance;
mod persistence;
mod profiles;
mod rules;
mod runtime;
mod scenes;
mod settings;
mod tiktok;
mod ticker;
mod updater;
mod virtual_output;

use parking_lot::RwLock;
use serde::Serialize;
use serde_json::Value;
use std::sync::Arc;
use tauri::Manager;

#[derive(Default)]
struct AppState {
    runtime: Arc<RwLock<runtime::RuntimeState>>,
}

#[derive(Serialize)]
struct HealthSnapshot {
    app_version: &'static str,
    mode: &'static str,
    tiktok: tiktok::ConnectorState,
    virtual_camera: virtual_output::DeviceState,
    virtual_audio: virtual_output::DeviceState,
}

#[tauri::command]
fn health_snapshot(state: tauri::State<AppState>) -> HealthSnapshot {
    let runtime = state.runtime.read();
    HealthSnapshot {
        app_version: env!("CARGO_PKG_VERSION"),
        mode: "Desktop",
        tiktok: runtime.connector_state(),
        virtual_camera: runtime.virtual_output.camera,
        virtual_audio: runtime.virtual_output.audio,
    }
}

#[tauri::command]
fn get_config(state: tauri::State<AppState>) -> persistence::AppConfig {
    state.runtime.read().config.clone()
}

#[tauri::command]
fn save_config(config: persistence::AppConfig, state: tauri::State<AppState>) -> Result<(), String> {
    persistence::save(&config).map_err(|e| e.to_string())?;
    state.runtime.write().config = config;
    Ok(())
}

#[tauri::command]
fn inject_test_event(kind: String, state: tauri::State<AppState>) -> Result<runtime::ProcessedEvent, String> {
    state.runtime.write().process_event(events::LiveEvent::mock(&kind))
}

#[tauri::command]
fn event_history(state: tauri::State<AppState>) -> Vec<events::LiveEvent> {
    state.runtime.read().events.history()
}

#[tauri::command]
fn alert_queue(state: tauri::State<AppState>) -> Vec<alerts::AlertJob> {
    state.runtime.read().alerts.snapshot()
}

#[tauri::command]
fn pop_alert(state: tauri::State<AppState>) -> Option<alerts::AlertJob> {
    state.runtime.write().alerts.next()
}

#[tauri::command]
fn connect_mock_tiktok(state: tauri::State<AppState>) -> Result<tiktok::ConnectorState, String> {
    use tiktok::LiveProvider;
    let mut runtime = state.runtime.write();
    runtime.mock_tiktok.connect().map_err(|e| e.to_string())?;
    Ok(runtime.mock_tiktok.status())
}

#[tauri::command]
fn disconnect_mock_tiktok(state: tauri::State<AppState>) -> tiktok::ConnectorState {
    use tiktok::LiveProvider;
    let mut runtime = state.runtime.write();
    runtime.mock_tiktok.disconnect();
    runtime.mock_tiktok.status()
}

#[tauri::command]
fn performance_snapshot(state: tauri::State<AppState>) -> performance::PerformanceSnapshot {
    state.runtime.write().performance.snapshot()
}

#[tauri::command]
async fn check_for_updates(state: tauri::State<'_, AppState>) -> Result<updater::UpdateCheck, String> {
    let endpoint = state.runtime.read().config.updates.endpoint.clone().ok_or_else(|| "update endpoint is not configured".to_string())?;
    updater::check(&endpoint, env!("CARGO_PKG_VERSION")).await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn media_launch(app: tauri::AppHandle, state: tauri::State<'_, media::MediaEngineState>) -> Result<media::MediaStatus, String> {
    media::launch(app, state).await
}

#[tauri::command]
async fn media_status(state: tauri::State<'_, media::MediaEngineState>) -> Result<media::MediaStatus, String> {
    media::status(state).await
}

#[tauri::command]
async fn media_scenes(state: tauri::State<'_, media::MediaEngineState>) -> Result<Vec<String>, String> {
    media::scenes(state).await
}

#[tauri::command]
async fn media_create_scene(name: String, state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::create_scene(name, state).await
}

#[tauri::command]
async fn media_set_scene(name: String, state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::set_scene(name, state).await
}

#[tauri::command]
async fn media_record_start(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::record_start(state).await
}

#[tauri::command]
async fn media_record_stop(state: tauri::State<'_, media::MediaEngineState>) -> Result<String, String> {
    media::record_stop(state).await
}

#[tauri::command]
async fn media_replay_start(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::replay_start(state).await
}

#[tauri::command]
async fn media_replay_save(state: tauri::State<'_, media::MediaEngineState>) -> Result<Option<String>, String> {
    media::replay_save(state).await
}

#[tauri::command]
async fn media_replay_stop(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::replay_stop(state).await
}

#[tauri::command]
async fn media_virtual_camera_start(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::virtual_camera_start(state).await
}

#[tauri::command]
async fn media_virtual_camera_stop(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::virtual_camera_stop(state).await
}

#[tauri::command]
fn media_install_virtual_camera(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::install_virtual_camera(state)
}

#[tauri::command]
async fn media_set_rtmp(server: String, key: String, state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::set_rtmp(server, key, state).await
}

#[tauri::command]
async fn media_stream_start(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::stream_start(state).await
}

#[tauri::command]
async fn media_stream_stop(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::stream_stop(state).await
}

#[tauri::command]
async fn media_shutdown(state: tauri::State<'_, media::MediaEngineState>) -> Result<(), String> {
    media::shutdown(state).await
}

#[tauri::command]
async fn media_inputs_list(app: tauri::AppHandle) -> Result<Vec<media_inputs::InputSummary>, String> {
    media_inputs::list(app).await
}

#[tauri::command]
async fn media_input_kinds(app: tauri::AppHandle) -> Result<Vec<String>, String> {
    media_inputs::kinds(app).await
}

#[tauri::command]
async fn media_input_create(app: tauri::AppHandle, scene: String, name: String, kind: String, settings: Value) -> Result<media_inputs::CreatedInput, String> {
    media_inputs::create(app, scene, name, kind, settings).await
}

#[tauri::command]
async fn media_input_remove(app: tauri::AppHandle, name: String) -> Result<(), String> {
    media_inputs::remove(app, name).await
}

#[tauri::command]
async fn media_input_rename(app: tauri::AppHandle, name: String, new_name: String) -> Result<(), String> {
    media_inputs::rename(app, name, new_name).await
}

#[tauri::command]
async fn media_input_set_muted(app: tauri::AppHandle, name: String, muted: bool) -> Result<(), String> {
    media_inputs::set_muted(app, name, muted).await
}

#[tauri::command]
async fn media_input_toggle_mute(app: tauri::AppHandle, name: String) -> Result<bool, String> {
    media_inputs::toggle_mute(app, name).await
}

#[tauri::command]
async fn media_input_set_volume_db(app: tauri::AppHandle, name: String, db: f32) -> Result<(), String> {
    media_inputs::set_volume_db(app, name, db).await
}

#[tauri::command]
async fn media_input_settings(app: tauri::AppHandle, name: String) -> Result<Value, String> {
    media_inputs::settings(app, name).await
}

#[tauri::command]
async fn media_input_set_settings(app: tauri::AppHandle, name: String, settings: Value, overlay: bool) -> Result<(), String> {
    media_inputs::set_settings(app, name, settings, overlay).await
}

#[tauri::command]
async fn media_input_property_items(app: tauri::AppHandle, name: String, property: String) -> Result<Value, String> {
    media_inputs::property_items(app, name, property).await
}

pub fn run() {
    logging::init();
    tauri::Builder::default()
        .manage(AppState::default())
        .manage(media::MediaEngineState::default())
        .setup(|app| {
            let state = app.state::<AppState>();
            let config = state.runtime.read().config.clone();
            if let Err(error) = persistence::save(&config) {
                tracing::warn!(%error, "initial config save skipped");
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            health_snapshot, get_config, save_config, inject_test_event, event_history, alert_queue, pop_alert,
            connect_mock_tiktok, disconnect_mock_tiktok, performance_snapshot, check_for_updates,
            media_launch, media_status, media_scenes, media_create_scene, media_set_scene,
            media_record_start, media_record_stop, media_replay_start, media_replay_save, media_replay_stop,
            media_virtual_camera_start, media_virtual_camera_stop, media_install_virtual_camera,
            media_set_rtmp, media_stream_start, media_stream_stop, media_shutdown,
            media_inputs_list, media_input_kinds, media_input_create, media_input_remove, media_input_rename,
            media_input_set_muted, media_input_toggle_mute, media_input_set_volume_db,
            media_input_settings, media_input_set_settings, media_input_property_items
        ])
        .run(tauri::generate_context!())
        .expect("error while running D7 LIVE");
}
