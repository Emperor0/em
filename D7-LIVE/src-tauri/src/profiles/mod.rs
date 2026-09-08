use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct Profile {
    pub name: String,
    pub width: u32,
    pub height: u32,
    pub fps: u32,
    pub encoder: String,
    pub bitrate_kbps: u32,
    pub audio_bitrate_kbps: u32,
    pub orientation: String,
}

impl Default for Profile {
    fn default() -> Self {
        Self {
            name: "TikTok Gaming".into(),
            width: 1080,
            height: 1920,
            fps: 60,
            encoder: "NVENC H.264".into(),
            bitrate_kbps: 6_000,
            audio_bitrate_kbps: 160,
            orientation: "vertical".into(),
        }
    }
}
