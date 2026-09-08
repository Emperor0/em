use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct UpdateSettings {
    pub check_on_startup: bool,
    pub cadence: String,
    pub install_after_stream: bool,
    pub endpoint: Option<String>,
}

impl Default for UpdateSettings {
    fn default() -> Self {
        Self {
            check_on_startup: true,
            cadence: "daily".into(),
            install_after_stream: false,
            endpoint: Some("https://raw.githubusercontent.com/Emperor0/em/main/D7-LIVE/release/latest.json".into()),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct GeneralSettings {
    pub language: String,
    pub theme: String,
    pub creator_name: String,
    pub start_minimized: bool,
}

impl Default for GeneralSettings {
    fn default() -> Self {
        Self { language: "ar".into(), theme: "D7 Horror".into(), creator_name: "d7kt".into(), start_minimized: false }
    }
}
