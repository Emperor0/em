use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum DeviceState {
    Installed,
    Running,
    Unavailable,
    RepairRequired,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub struct VirtualOutputState {
    pub camera: DeviceState,
    pub audio: DeviceState,
}

impl Default for VirtualOutputState {
    fn default() -> Self {
        Self { camera: DeviceState::Unavailable, audio: DeviceState::Unavailable }
    }
}
