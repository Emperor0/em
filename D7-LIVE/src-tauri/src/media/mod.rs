use obws::Client;
use parking_lot::{Mutex, RwLock};
use serde::Serialize;
use sha2::{Digest, Sha256};
use std::{
    collections::HashSet,
    fs::{self, File},
    io::{self, Read},
    path::{Path, PathBuf},
    process::{Child, Command},
    sync::Arc,
    time::Duration,
};
use tauri::{path::BaseDirectory, AppHandle, Manager};
use thiserror::Error;
use tokio::sync::RwLock as AsyncRwLock;
use uuid::Uuid;
use zip::ZipArchive;

pub const OBS_VERSION: &str = "32.2.2";
pub const OBS_ZIP_SHA256: &str = "4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1";
const OBS_WS_PORT: u16 = 4455;
const DEFAULT_SCENES: &[&str] = &["TikTok Gaming", "Just Chatting", "BRB / Panic", "Horror", "COD"];

#[derive(Debug, Error)]
pub enum MediaError {
    #[error("media engine is not connected")]
    NotConnected,
    #[error("OBS runtime is missing. Run `npm run prepare:obs` or reinstall D7 LIVE")]
    RuntimeMissing,
    #[error("OBS runtime integrity check failed")]
    Integrity,
    #[error("I/O error: {0}")]
    Io(String),
    #[error("OBS error: {0}")]
    Obs(String),
    #[error("media engine failed to start: {0}")]
    Launch(String),
}

impl From<io::Error> for MediaError {
    fn from(value: io::Error) -> Self {
        Self::Io(value.to_string())
    }
}

#[derive(Debug, Clone, Serialize, Default)]
pub struct MediaStatus {
    pub installed: bool,
    pub process_running: bool,
    pub connected: bool,
    pub engine_version: String,
    pub obs_version: Option<String>,
    pub websocket_version: Option<String>,
    pub recording: bool,
    pub streaming: bool,
    pub replay_buffer: bool,
    pub virtual_camera: bool,
    pub current_scene: Option<String>,
    pub fps: f64,
    pub obs_cpu_percent: f64,
    pub obs_memory_mb: f64,
    pub frame_time_ms: f64,
    pub render_skipped_frames: u32,
    pub output_skipped_frames: u32,
    pub last_error: Option<String>,
}

pub struct MediaEngineState {
    client: AsyncRwLock<Option<Arc<Client>>>,
    process: Mutex<Option<Child>>,
    root: RwLock<Option<PathBuf>>,
    password: RwLock<Option<String>>,
    last_error: RwLock<Option<String>>,
}

impl Default for MediaEngineState {
    fn default() -> Self {
        Self {
            client: AsyncRwLock::new(None),
            process: Mutex::new(None),
            root: RwLock::new(None),
            password: RwLock::new(None),
            last_error: RwLock::new(None),
        }
    }
}

impl MediaEngineState {
    async fn client(&self) -> Result<Arc<Client>, MediaError> {
        self.client.read().await.clone().ok_or(MediaError::NotConnected)
    }

    fn set_error(&self, value: impl Into<String>) {
        *self.last_error.write() = Some(value.into());
    }

    fn clear_error(&self) {
        *self.last_error.write() = None;
    }

    fn process_running(&self) -> bool {
        let mut guard = self.process.lock();
        match guard.as_mut() {
            Some(child) => match child.try_wait() {
                Ok(None) => true,
                Ok(Some(_)) | Err(_) => {
                    *guard = None;
                    false
                }
            },
            None => false,
        }
    }
}

fn app_engine_dir(app: &AppHandle) -> Result<PathBuf, MediaError> {
    let base = app.path().app_local_data_dir().map_err(|e| MediaError::Io(e.to_string()))?;
    Ok(base.join("media-engine").join(format!("obs-{OBS_VERSION}")))
}

fn bundled_obs_zip(app: &AppHandle) -> Result<PathBuf, MediaError> {
    app.path()
        .resolve("resources/obs/obs.zip", BaseDirectory::Resource)
        .map_err(|e| MediaError::Io(e.to_string()))
}

fn sha256_file(path: &Path) -> Result<String, MediaError> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 1024 * 1024];
    loop {
        let read = file.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(format!("{:x}", hasher.finalize()))
}

fn find_obs_root(base: &Path) -> Option<PathBuf> {
    let direct = base.join("bin").join("64bit").join("obs64.exe");
    if direct.is_file() {
        return Some(base.to_path_buf());
    }
    let entries = fs::read_dir(base).ok()?;
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() && path.join("bin").join("64bit").join("obs64.exe").is_file() {
            return Some(path);
        }
    }
    None
}

fn extract_verified_runtime(zip_path: &Path, destination: &Path) -> Result<PathBuf, MediaError> {
    if sha256_file(zip_path)? != OBS_ZIP_SHA256 {
        return Err(MediaError::Integrity);
    }

    if destination.exists() {
        fs::remove_dir_all(destination)?;
    }
    fs::create_dir_all(destination)?;

    let file = File::open(zip_path)?;
    let mut archive = ZipArchive::new(file).map_err(|e| MediaError::Io(e.to_string()))?;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).map_err(|e| MediaError::Io(e.to_string()))?;
        let Some(name) = entry.enclosed_name() else { continue };
        let output = destination.join(name);
        if entry.is_dir() {
            fs::create_dir_all(&output)?;
        } else {
            if let Some(parent) = output.parent() {
                fs::create_dir_all(parent)?;
            }
            let mut out = File::create(&output)?;
            io::copy(&mut entry, &mut out)?;
        }
    }

    let root = find_obs_root(destination).ok_or(MediaError::RuntimeMissing)?;
    fs::write(root.join("portable_mode.txt"), b"")?;
    fs::write(root.join(".d7-engine-version"), OBS_VERSION.as_bytes())?;
    Ok(root)
}

fn prepare_runtime(app: &AppHandle) -> Result<PathBuf, MediaError> {
    let destination = app_engine_dir(app)?;
    if let Some(root) = find_obs_root(&destination) {
        return Ok(root);
    }
    let zip = bundled_obs_zip(app)?;
    if !zip.is_file() {
        return Err(MediaError::RuntimeMissing);
    }
    extract_verified_runtime(&zip, &destination)
}

fn password_for(root: &Path) -> Result<String, MediaError> {
    let config = root.join("config").join("d7-live-websocket.password");
    if let Ok(existing) = fs::read_to_string(&config) {
        let existing = existing.trim().to_string();
        if existing.len() >= 24 {
            return Ok(existing);
        }
    }
    if let Some(parent) = config.parent() {
        fs::create_dir_all(parent)?;
    }
    let password = format!("d7-{}", Uuid::new_v4().simple());
    fs::write(config, password.as_bytes())?;
    Ok(password)
}

fn write_portable_config(root: &Path, password: &str) -> Result<(), MediaError> {
    let obs_config = root.join("config").join("obs-studio");
    fs::create_dir_all(&obs_config)?;
    let global = format!(
        "[General]\nFirstRun=false\nEnableAutoUpdates=false\n\n[BasicWindow]\nSysTrayEnabled=true\nSysTrayWhenStarted=true\nShowOnStartup=false\n\n[OBSWebSocket]\nFirstLoad=false\nServerEnabled=true\nServerPort={OBS_WS_PORT}\nAlertsEnabled=false\nAuthRequired=true\nServerPassword={password}\n"
    );
    fs::write(obs_config.join("global.ini"), global.as_bytes())?;

    let ws_dir = obs_config.join("plugin_config").join("obs-websocket");
    fs::create_dir_all(&ws_dir)?;
    let ws_json = serde_json::json!({
        "alerts_enabled": false,
        "auth_required": true,
        "first_load": false,
        "server_enabled": true,
        "server_password": password,
        "server_port": OBS_WS_PORT
    });
    fs::write(
        ws_dir.join("config.json"),
        serde_json::to_vec_pretty(&ws_json).map_err(|e| MediaError::Io(e.to_string()))?,
    )?;
    Ok(())
}

fn launch_obs(root: &Path, password: &str) -> Result<Child, MediaError> {
    let exe = root.join("bin").join("64bit").join("obs64.exe");
    if !exe.is_file() {
        return Err(MediaError::RuntimeMissing);
    }
    let mut command = Command::new(&exe);
    command
        .current_dir(exe.parent().unwrap_or(root))
        .arg("--portable")
        .arg("--multi")
        .arg("--minimize-to-tray")
        .arg("--disable-shutdown-check")
        .arg(format!("--websocket_port={OBS_WS_PORT}"))
        .arg(format!("--websocket_password={password}"))
        .arg("--websocket_ipv4_only");

    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000);
    }

    command.spawn().map_err(|e| MediaError::Launch(e.to_string()))
}

async fn connect_obs(password: &str) -> Result<Client, MediaError> {
    for attempt in 0..8u64 {
        if attempt > 0 {
            tokio::time::sleep(Duration::from_millis(700 + attempt * 150)).await;
        }
        let result = tokio::time::timeout(
            Duration::from_secs(5),
            Client::connect("127.0.0.1", OBS_WS_PORT, Some(password)),
        )
        .await;
        match result {
            Ok(Ok(client)) => return Ok(client),
            Ok(Err(error)) if attempt == 7 => return Err(MediaError::Obs(error.to_string())),
            Err(_) if attempt == 7 => return Err(MediaError::Launch("OBS WebSocket connection timed out".into())),
            _ => {}
        }
    }
    Err(MediaError::Launch("OBS WebSocket unavailable".into()))
}

async fn ensure_default_scenes(client: &Client) -> Result<(), MediaError> {
    let list = client.scenes().list().await.map_err(|e| MediaError::Obs(e.to_string()))?;
    let existing: HashSet<String> = list.scenes.into_iter().map(|scene| scene.id.name).collect();
    for name in DEFAULT_SCENES {
        if !existing.contains(*name) {
            client.scenes().create(name).await.map_err(|e| MediaError::Obs(e.to_string()))?;
        }
    }
    client
        .scenes()
        .set_current_program_scene(DEFAULT_SCENES[0])
        .await
        .map_err(|e| MediaError::Obs(e.to_string()))?;
    Ok(())
}

async fn set_record_directory(app: &AppHandle, client: &Client) -> Result<(), MediaError> {
    let base = app
        .path()
        .video_dir()
        .or_else(|_| app.path().app_local_data_dir())
        .map_err(|e| MediaError::Io(e.to_string()))?;
    let dir = base.join("D7 LIVE").join("Recordings");
    fs::create_dir_all(&dir)?;
    let value = dir.to_string_lossy().to_string();
    client.config().set_record_directory(&value).await.map_err(|e| MediaError::Obs(e.to_string()))
}

pub async fn launch(app: AppHandle, state: tauri::State<'_, MediaEngineState>) -> Result<MediaStatus, String> {
    if state.client.read().await.is_some() {
        return status(state).await;
    }

    let result: Result<(), MediaError> = async {
        let root = prepare_runtime(&app)?;
        let password = password_for(&root)?;
        write_portable_config(&root, &password)?;

        if !state.process_running() {
            let child = launch_obs(&root, &password)?;
            *state.process.lock() = Some(child);
        }

        let client = Arc::new(connect_obs(&password).await?);
        ensure_default_scenes(&client).await?;
        set_record_directory(&app, &client).await?;
        *state.root.write() = Some(root);
        *state.password.write() = Some(password);
        *state.client.write().await = Some(client);
        state.clear_error();
        Ok(())
    }
    .await;

    if let Err(error) = result {
        let message = error.to_string();
        state.set_error(message.clone());
        return Err(message);
    }
    status(state).await
}

pub async fn status(state: tauri::State<'_, MediaEngineState>) -> Result<MediaStatus, String> {
    let root = state.root.read().clone();
    let installed = root.as_ref().and_then(|p| find_obs_root(p)).is_some();
    let process_running = state.process_running();
    let client = state.client.read().await.clone();
    let Some(client) = client else {
        return Ok(MediaStatus {
            installed,
            process_running,
            connected: false,
            engine_version: env!("CARGO_PKG_VERSION").into(),
            last_error: state.last_error.read().clone(),
            ..MediaStatus::default()
        });
    };

    let version = client.general().version().await.ok();
    let stats = client.general().stats().await.ok();
    let recording = client.recording().status().await.ok().map(|x| x.active).unwrap_or(false);
    let streaming = client.streaming().status().await.ok().map(|x| x.active).unwrap_or(false);
    let replay_buffer = client.replay_buffer().status().await.unwrap_or(false);
    let virtual_camera = client.virtual_cam().status().await.unwrap_or(false);
    let current_scene = client.scenes().current_program_scene().await.ok().map(|x| x.id.name);

    Ok(MediaStatus {
        installed: true,
        process_running,
        connected: true,
        engine_version: env!("CARGO_PKG_VERSION").into(),
        obs_version: version.as_ref().map(|v| v.obs_studio_version.to_string()),
        websocket_version: version.as_ref().map(|v| v.obs_web_socket_version.to_string()),
        recording,
        streaming,
        replay_buffer,
        virtual_camera,
        current_scene,
        fps: stats.as_ref().map(|s| s.active_fps).unwrap_or(0.0),
        obs_cpu_percent: stats.as_ref().map(|s| s.cpu_usage).unwrap_or(0.0),
        obs_memory_mb: stats.as_ref().map(|s| s.memory_usage).unwrap_or(0.0),
        frame_time_ms: stats.as_ref().map(|s| s.average_frame_render_time).unwrap_or(0.0),
        render_skipped_frames: stats.as_ref().map(|s| s.render_skipped_frames).unwrap_or(0),
        output_skipped_frames: stats.as_ref().map(|s| s.output_skipped_frames).unwrap_or(0),
        last_error: state.last_error.read().clone(),
    })
}

pub async fn scenes(state: tauri::State<'_, MediaEngineState>) -> Result<Vec<String>, String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    let list = client.scenes().list().await.map_err(|e| e.to_string())?;
    Ok(list.scenes.into_iter().map(|x| x.id.name).collect())
}

pub async fn create_scene(name: String, state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let name = name.trim();
    if name.is_empty() {
        return Err("scene name cannot be empty".into());
    }
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.scenes().create(name).await.map_err(|e| e.to_string())?;
    Ok(())
}

pub async fn set_scene(name: String, state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.scenes().set_current_program_scene(name.as_str()).await.map_err(|e| e.to_string())
}

pub async fn record_start(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.recording().start().await.map_err(|e| e.to_string())
}

pub async fn record_stop(state: tauri::State<'_, MediaEngineState>) -> Result<String, String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.recording().stop().await.map_err(|e| e.to_string())
}

pub async fn replay_start(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.replay_buffer().start().await.map_err(|e| e.to_string())
}

pub async fn replay_save(state: tauri::State<'_, MediaEngineState>) -> Result<Option<String>, String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.replay_buffer().save().await.map_err(|e| e.to_string())?;
    Ok(client.replay_buffer().last_replay().await.ok())
}

pub async fn replay_stop(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.replay_buffer().stop().await.map_err(|e| e.to_string())
}

pub async fn virtual_camera_start(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.virtual_cam().start().await.map_err(|e| e.to_string())
}

pub async fn virtual_camera_stop(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.virtual_cam().stop().await.map_err(|e| e.to_string())
}

pub async fn set_rtmp(server: String, key: String, state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    if !server.starts_with("rtmp://") && !server.starts_with("rtmps://") {
        return Err("RTMP server must begin with rtmp:// or rtmps://".into());
    }
    if key.trim().is_empty() {
        return Err("stream key cannot be empty".into());
    }
    let client = state.client().await.map_err(|e| e.to_string())?;
    let settings = serde_json::json!({"server": server, "key": key});
    client.config().set_stream_service_settings("rtmp_custom", &settings).await.map_err(|e| e.to_string())
}

pub async fn stream_start(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.streaming().start().await.map_err(|e| e.to_string())
}

pub async fn stream_stop(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let client = state.client().await.map_err(|e| e.to_string())?;
    client.streaming().stop().await.map_err(|e| e.to_string())
}

pub async fn shutdown(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    if let Some(client) = state.client.read().await.clone() {
        if client.streaming().status().await.ok().map(|s| s.active).unwrap_or(false) {
            return Err("cannot shut down media engine while streaming".into());
        }
        if client.recording().status().await.ok().map(|s| s.active).unwrap_or(false) {
            return Err("cannot shut down media engine while recording".into());
        }
    }
    *state.client.write().await = None;
    if let Some(mut child) = state.process.lock().take() {
        let _ = child.kill();
        let _ = child.wait();
    }
    Ok(())
}

pub fn install_virtual_camera(state: tauri::State<'_, MediaEngineState>) -> Result<(), String> {
    let root = state.root.read().clone().ok_or_else(|| "launch the D7 media engine first".to_string())?;
    let installer = root.join("data").join("obs-plugins").join("win-dshow").join("virtualcam-install.bat");
    if !installer.is_file() {
        return Err("OBS virtual camera installer was not found".into());
    }

    #[cfg(target_os = "windows")]
    {
        let escaped = installer.to_string_lossy().replace('"', "\"");
        let script = format!("Start-Process -FilePath 'cmd.exe' -Verb RunAs -Wait -ArgumentList '/c \"\"{}\"\"'", escaped);
        let status = Command::new("powershell.exe")
            .args(["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", &script])
            .status()
            .map_err(|e| e.to_string())?;
        if !status.success() {
            return Err(format!("virtual camera installer exited with {status}"));
        }
        return Ok(());
    }

    #[cfg(not(target_os = "windows"))]
    Err("virtual camera installation is only supported on Windows".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn official_runtime_hash_is_pinned() {
        assert_eq!(OBS_ZIP_SHA256.len(), 64);
        assert!(OBS_ZIP_SHA256.chars().all(|c| c.is_ascii_hexdigit()));
    }

    #[test]
    fn default_scene_set_contains_tiktok_gaming() {
        assert!(DEFAULT_SCENES.contains(&"TikTok Gaming"));
        assert!(DEFAULT_SCENES.contains(&"BRB / Panic"));
    }
}
