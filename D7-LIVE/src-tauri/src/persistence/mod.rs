use crate::{profiles::Profile, rules::Rule, scenes::Scene, settings::{GeneralSettings, UpdateSettings}, ticker::TickerConfig};
use directories::ProjectDirs;
use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf};
use thiserror::Error;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AppConfig {
    pub general: GeneralSettings,
    pub profile: Profile,
    pub scenes: Vec<Scene>,
    pub active_scene_id: Option<String>,
    pub ticker: TickerConfig,
    pub rules: Vec<Rule>,
    pub updates: UpdateSettings,
}

impl Default for AppConfig {
    fn default() -> Self {
        let scenes = crate::scenes::default_scenes();
        let active_scene_id = scenes.first().map(|s| s.id.to_string());
        Self {
            general: GeneralSettings::default(),
            profile: Profile::default(),
            scenes,
            active_scene_id,
            ticker: TickerConfig::default(),
            rules: crate::rules::default_rules(),
            updates: UpdateSettings::default(),
        }
    }
}

#[derive(Debug, Error)]
pub enum PersistenceError {
    #[error("D7 LIVE data directory unavailable")]
    Directory,
    #[error(transparent)]
    Io(#[from] std::io::Error),
    #[error(transparent)]
    Json(#[from] serde_json::Error),
}

pub fn data_dir() -> Result<PathBuf, PersistenceError> {
    ProjectDirs::from("com", "d7kt", "D7 LIVE").map(|d| d.data_local_dir().to_path_buf()).ok_or(PersistenceError::Directory)
}

pub fn config_path() -> Result<PathBuf, PersistenceError> {
    Ok(data_dir()?.join("Profiles").join("default.json"))
}

pub fn load_or_default() -> AppConfig {
    match load() {
        Ok(value) => value,
        Err(error) => {
            tracing::warn!(%error, "using default D7 LIVE configuration");
            AppConfig::default()
        }
    }
}

pub fn load() -> Result<AppConfig, PersistenceError> {
    let path = config_path()?;
    let bytes = fs::read(path)?;
    Ok(serde_json::from_slice(&bytes)?)
}

pub fn save(config: &AppConfig) -> Result<(), PersistenceError> {
    let path = config_path()?;
    let bytes = serde_json::to_vec_pretty(config)?;
    crate::crash_recovery::atomic_write(&path, &bytes)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_config_has_scene_and_rules() {
        let cfg = AppConfig::default();
        assert!(!cfg.scenes.is_empty());
        assert!(!cfg.rules.is_empty());
    }
}
