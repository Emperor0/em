use crate::events::LiveEvent;
use serde::Serialize;
use thiserror::Error;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum ConnectorState {
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error,
}

#[derive(Debug, Error)]
pub enum ProviderError {
    #[error("provider unavailable: {0}")]
    Unavailable(String),
}

pub trait LiveProvider: Send + Sync {
    fn connect(&mut self) -> Result<(), ProviderError>;
    fn disconnect(&mut self);
    fn status(&self) -> ConnectorState;
    fn poll(&mut self) -> Result<Vec<LiveEvent>, ProviderError>;
}

pub struct MockTikTokProvider {
    state: ConnectorState,
}

impl Default for MockTikTokProvider {
    fn default() -> Self {
        Self { state: ConnectorState::Disconnected }
    }
}

impl LiveProvider for MockTikTokProvider {
    fn connect(&mut self) -> Result<(), ProviderError> {
        self.state = ConnectorState::Connecting;
        self.state = ConnectorState::Connected;
        Ok(())
    }

    fn disconnect(&mut self) {
        self.state = ConnectorState::Disconnected;
    }

    fn status(&self) -> ConnectorState {
        self.state
    }

    fn poll(&mut self) -> Result<Vec<LiveEvent>, ProviderError> {
        Ok(vec![])
    }
}
