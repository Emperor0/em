use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Transform {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
    pub scale: f32,
    pub rotation: f32,
    pub opacity: f32,
    pub crop: [f32; 4],
    pub anchor_x: f32,
    pub anchor_y: f32,
    pub locked: bool,
    pub visible: bool,
}

impl Default for Transform {
    fn default() -> Self {
        Self {
            x: 0.0,
            y: 0.0,
            width: 1920.0,
            height: 1080.0,
            scale: 1.0,
            rotation: 0.0,
            opacity: 1.0,
            crop: [0.0; 4],
            anchor_x: 0.5,
            anchor_y: 0.5,
            locked: false,
            visible: true,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum SourceKind {
    GameCapture,
    WindowCapture,
    DisplayCapture,
    Camera,
    Image,
    Gif,
    Video,
    WebM,
    Text,
    Browser,
    Color,
    AudioInput,
    AudioOutput,
    Media,
    Group,
    NestedScene,
    Ticker,
    Alerts,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Source {
    pub id: Uuid,
    pub name: String,
    pub kind: SourceKind,
    pub transform: Transform,
    #[serde(default)]
    pub settings: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Scene {
    pub id: Uuid,
    pub name: String,
    pub sources: Vec<Source>,
}

impl Scene {
    pub fn new(name: impl Into<String>) -> Self {
        Self { id: Uuid::new_v4(), name: name.into(), sources: Vec::new() }
    }
}

pub fn default_scenes() -> Vec<Scene> {
    vec![
        Scene::new("TikTok Gaming"),
        Scene::new("Just Chatting"),
        Scene::new("BRB / Panic"),
        Scene::new("Horror"),
        Scene::new("COD"),
    ]
}
