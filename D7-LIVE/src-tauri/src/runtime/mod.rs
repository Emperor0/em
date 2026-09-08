use crate::{alerts::{AlertJob, AlertQueue}, events::{EventEngine, LiveEvent}, performance::PerformanceMonitor, persistence::AppConfig, rules::{self, RuleAction}, tiktok::{ConnectorState, LiveProvider, MockTikTokProvider}, virtual_output::VirtualOutputState};
use serde::Serialize;
use std::collections::HashMap;

pub struct RuntimeState {
    pub events: EventEngine,
    pub alerts: AlertQueue,
    pub config: AppConfig,
    pub mock_tiktok: MockTikTokProvider,
    pub performance: PerformanceMonitor,
    pub virtual_output: VirtualOutputState,
    pub counters: HashMap<String, i64>,
}

impl Default for RuntimeState {
    fn default() -> Self {
        Self {
            events: EventEngine::default(),
            alerts: AlertQueue::default(),
            config: crate::persistence::load_or_default(),
            mock_tiktok: MockTikTokProvider::default(),
            performance: PerformanceMonitor::default(),
            virtual_output: VirtualOutputState::default(),
            counters: HashMap::new(),
        }
    }
}

#[derive(Debug, Serialize)]
pub struct ProcessedEvent {
    pub event: LiveEvent,
    pub generated: Vec<LiveEvent>,
    pub queued_alerts: Vec<AlertJob>,
    pub actions: Vec<RuleAction>,
}

impl RuntimeState {
    pub fn process_event(&mut self, event: LiveEvent) -> Result<ProcessedEvent, String> {
        let generated = self.events.ingest(event.clone()).map_err(|e| e.to_string())?;
        let mut queued_alerts = Vec::new();
        let mut actions = Vec::new();

        for current in std::iter::once(&event).chain(generated.iter()) {
            let matched_actions: Vec<RuleAction> = rules::evaluate(&self.config.rules, current).into_iter().cloned().collect();
            for action in matched_actions.iter() {
                match action {
                    RuleAction::ShowAlert { .. } => {
                        if let Some(job) = AlertQueue::from_event(current) {
                            self.alerts.enqueue(job.clone());
                            queued_alerts.push(job);
                        }
                    }
                    RuleAction::AddTickerMessage { value } => {
                        if !self.config.ticker.messages.contains(value) {
                            self.config.ticker.messages.push(value.clone());
                        }
                    }
                    RuleAction::IncrementCounter { name, amount } => {
                        *self.counters.entry(name.clone()).or_insert(0) += amount;
                    }
                    _ => {}
                }
                actions.push(action.clone());
            }
        }

        Ok(ProcessedEvent { event, generated, queued_alerts, actions })
    }

    pub fn connector_state(&self) -> ConnectorState {
        self.mock_tiktok.status()
    }
}
