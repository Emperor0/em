use obws::{
    requests::inputs::{Create, InputId, SetSettings, Volume},
    Client,
};
use serde::Serialize;
use serde_json::Value;
use tauri::{AppHandle, Manager};

const OBS_VERSION: &str = "32.2.2";
const OBS_WS_PORT: u16 = 4455;

#[derive(Debug, Clone, Serialize)]
pub struct InputSummary {
    pub name: String,
    pub uuid: String,
    pub kind: String,
    pub unversioned_kind: String,
    pub muted: Option<bool>,
}

#[derive(Debug, Clone, Serialize)]
pub struct CreatedInput {
    pub name: String,
    pub scene_item_id: i64,
}

async fn client(app: &AppHandle) -> Result<Client, String> {
    let root = app
        .path()
        .app_local_data_dir()
        .map_err(|e| e.to_string())?
        .join("media-engine")
        .join(format!("obs-{OBS_VERSION}"));
    let password_path = root.join("config").join("d7-live-websocket.password");
    let password = std::fs::read_to_string(&password_path)
        .map_err(|_| "D7 media engine is not initialized. Start Media first.".to_string())?;
    Client::connect("127.0.0.1", OBS_WS_PORT, Some(password.trim()))
        .await
        .map_err(|e| format!("OBS connection failed: {e}"))
}

pub async fn list(app: AppHandle) -> Result<Vec<InputSummary>, String> {
    let client = client(&app).await?;
    let inputs = client.inputs().list(None).await.map_err(|e| e.to_string())?;
    let mut result = Vec::with_capacity(inputs.len());
    for input in inputs {
        let muted = client.inputs().muted(InputId::Name(&input.id.name)).await.ok();
        result.push(InputSummary {
            name: input.id.name,
            uuid: input.id.uuid.to_string(),
            kind: input.kind,
            unversioned_kind: input.unversioned_kind,
            muted,
        });
    }
    Ok(result)
}

pub async fn kinds(app: AppHandle) -> Result<Vec<String>, String> {
    let client = client(&app).await?;
    client.inputs().list_kinds(false).await.map_err(|e| e.to_string())
}

pub async fn create(
    app: AppHandle,
    scene: String,
    name: String,
    kind: String,
    settings: Value,
) -> Result<CreatedInput, String> {
    let scene = scene.trim();
    let name = name.trim();
    let kind = kind.trim();
    if scene.is_empty() || name.is_empty() || kind.is_empty() {
        return Err("scene, name and kind are required".into());
    }
    let client = client(&app).await?;
    let request = Create {
        scene: scene.into(),
        input: name,
        kind,
        settings: Some(settings),
        enabled: Some(true),
    };
    let created = client.inputs().create(request).await.map_err(|e| e.to_string())?;
    Ok(CreatedInput { name: name.to_string(), scene_item_id: created.id as i64 })
}

pub async fn remove(app: AppHandle, name: String) -> Result<(), String> {
    let client = client(&app).await?;
    client.inputs().remove(InputId::Name(name.as_str())).await.map_err(|e| e.to_string())
}

pub async fn rename(app: AppHandle, name: String, new_name: String) -> Result<(), String> {
    if new_name.trim().is_empty() {
        return Err("new input name cannot be empty".into());
    }
    let client = client(&app).await?;
    client.inputs().set_name(InputId::Name(name.as_str()), new_name.trim()).await.map_err(|e| e.to_string())
}

pub async fn set_muted(app: AppHandle, name: String, muted: bool) -> Result<(), String> {
    let client = client(&app).await?;
    client.inputs().set_muted(InputId::Name(name.as_str()), muted).await.map_err(|e| e.to_string())
}

pub async fn toggle_mute(app: AppHandle, name: String) -> Result<bool, String> {
    let client = client(&app).await?;
    client.inputs().toggle_mute(InputId::Name(name.as_str())).await.map_err(|e| e.to_string())
}

pub async fn set_volume_db(app: AppHandle, name: String, db: f32) -> Result<(), String> {
    if !(-100.0..=26.0).contains(&db) {
        return Err("volume must be between -100 dB and +26 dB".into());
    }
    let client = client(&app).await?;
    client.inputs().set_volume(InputId::Name(name.as_str()), Volume::Db(db)).await.map_err(|e| e.to_string())
}

pub async fn settings(app: AppHandle, name: String) -> Result<Value, String> {
    let client = client(&app).await?;
    let response = client.inputs().settings::<Value>(InputId::Name(name.as_str())).await.map_err(|e| e.to_string())?;
    Ok(response.settings)
}

pub async fn set_settings(app: AppHandle, name: String, settings: Value, overlay: bool) -> Result<(), String> {
    let client = client(&app).await?;
    let request = SetSettings {
        input: InputId::Name(name.as_str()),
        settings: &settings,
        overlay: Some(overlay),
    };
    client.inputs().set_settings(request).await.map_err(|e| e.to_string())
}

pub async fn property_items(app: AppHandle, name: String, property: String) -> Result<Value, String> {
    let client = client(&app).await?;
    let items = client.inputs().properties_list_property_items(InputId::Name(name.as_str()), property.as_str()).await.map_err(|e| e.to_string())?;
    serde_json::to_value(items).map_err(|e| e.to_string())
}
