use obws::{
    requests::scene_items::{Id, Position, Scale, SceneItemTransform as RequestTransform, SetEnabled, SetIndex, SetLocked, SetTransform},
    Client,
};
use serde::Serialize;
use tauri::{AppHandle, Manager};

const OBS_VERSION: &str = "32.2.2";
const OBS_WS_PORT: u16 = 4455;

#[derive(Debug, Clone, Serialize)]
pub struct SceneItemState {
    pub item_id: i64,
    pub enabled: bool,
    pub locked: bool,
    pub index: u32,
    pub position_x: f32,
    pub position_y: f32,
    pub rotation: f32,
    pub scale_x: f32,
    pub scale_y: f32,
    pub source_width: f32,
    pub source_height: f32,
    pub width: f32,
    pub height: f32,
    pub crop_left: u32,
    pub crop_right: u32,
    pub crop_top: u32,
    pub crop_bottom: u32,
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

async fn item_id(client: &Client, scene: &str, source: &str) -> Result<i64, String> {
    client
        .scene_items()
        .id(Id {
            scene: scene.into(),
            source,
            search_offset: None,
        })
        .await
        .map_err(|e| format!("Scene item not found: {e}"))
}

pub async fn state(app: AppHandle, scene: String, source: String) -> Result<SceneItemState, String> {
    let client = client(&app).await?;
    let id = item_id(&client, scene.trim(), source.trim()).await?;
    let api = client.scene_items();
    let transform = api.transform(scene.as_str().into(), id).await.map_err(|e| e.to_string())?;
    let enabled = api.enabled(scene.as_str().into(), id).await.map_err(|e| e.to_string())?;
    let locked = api.locked(scene.as_str().into(), id).await.map_err(|e| e.to_string())?;
    let index = api.index(scene.as_str().into(), id).await.map_err(|e| e.to_string())?;

    Ok(SceneItemState {
        item_id: id,
        enabled,
        locked,
        index,
        position_x: transform.position_x,
        position_y: transform.position_y,
        rotation: transform.rotation,
        scale_x: transform.scale_x,
        scale_y: transform.scale_y,
        source_width: transform.source_width,
        source_height: transform.source_height,
        width: transform.width,
        height: transform.height,
        crop_left: transform.crop_left,
        crop_right: transform.crop_right,
        crop_top: transform.crop_top,
        crop_bottom: transform.crop_bottom,
    })
}

pub async fn set_transform(
    app: AppHandle,
    scene: String,
    source: String,
    x: f32,
    y: f32,
    rotation: f32,
    scale_x: f32,
    scale_y: f32,
) -> Result<(), String> {
    if !x.is_finite() || !y.is_finite() || !rotation.is_finite() || !scale_x.is_finite() || !scale_y.is_finite() {
        return Err("transform values must be finite numbers".into());
    }
    if scale_x.abs() > 100.0 || scale_y.abs() > 100.0 {
        return Err("scale is outside the safe range".into());
    }

    let client = client(&app).await?;
    let id = item_id(&client, scene.trim(), source.trim()).await?;
    client
        .scene_items()
        .set_transform(SetTransform {
            scene: scene.as_str().into(),
            item_id: id,
            transform: RequestTransform {
                position: Some(Position { x: Some(x), y: Some(y) }),
                rotation: Some(rotation),
                scale: Some(Scale { x: Some(scale_x), y: Some(scale_y) }),
                ..Default::default()
            },
        })
        .await
        .map_err(|e| e.to_string())
}

pub async fn set_enabled(app: AppHandle, scene: String, source: String, enabled: bool) -> Result<(), String> {
    let client = client(&app).await?;
    let id = item_id(&client, scene.trim(), source.trim()).await?;
    client
        .scene_items()
        .set_enabled(SetEnabled { scene: scene.as_str().into(), item_id: id, enabled })
        .await
        .map_err(|e| e.to_string())
}

pub async fn set_locked(app: AppHandle, scene: String, source: String, locked: bool) -> Result<(), String> {
    let client = client(&app).await?;
    let id = item_id(&client, scene.trim(), source.trim()).await?;
    client
        .scene_items()
        .set_locked(SetLocked { scene: scene.as_str().into(), item_id: id, locked })
        .await
        .map_err(|e| e.to_string())
}

pub async fn set_index(app: AppHandle, scene: String, source: String, index: u32) -> Result<(), String> {
    let client = client(&app).await?;
    let id = item_id(&client, scene.trim(), source.trim()).await?;
    client
        .scene_items()
        .set_index(SetIndex { scene: scene.as_str().into(), item_id: id, index })
        .await
        .map_err(|e| e.to_string())
}
