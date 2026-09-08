use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::{HashSet, VecDeque};
use thiserror::Error;
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum EventType {
    Follow,
    Like,
    LikeMilestone,
    Comment,
    Gift,
    Subscribe,
    Share,
    ViewerJoin,
    Custom,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LiveEvent {
    pub id: Uuid,
    pub provider: String,
    pub event_type: EventType,
    pub username: String,
    pub display_name: String,
    pub user_id: Option<String>,
    pub timestamp: DateTime<Utc>,
    pub data: serde_json::Value,
}

impl LiveEvent {
    pub fn mock(kind: &str) -> Self {
        let (event_type, data) = match kind.to_ascii_uppercase().as_str() {
            "FOLLOW" => (EventType::Follow, serde_json::json!({})),
            "1000_LIKES" => (EventType::LikeMilestone, serde_json::json!({"likes": 1000, "milestone": 1000})),
            "GIFT" => (EventType::Gift, serde_json::json!({"gift_name": "Rose", "gift_count": 1, "coins": 1})),
            "SUBSCRIPTION" => (EventType::Subscribe, serde_json::json!({"months": 1})),
            "COMMENT" => (EventType::Comment, serde_json::json!({"message": "GG"})),
            "SHARE" => (EventType::Share, serde_json::json!({})),
            _ => (EventType::Custom, serde_json::json!({})),
        };
        Self {
            id: Uuid::new_v4(),
            provider: "mock_tiktok".into(),
            event_type,
            username: "test_user".into(),
            display_name: "Test User".into(),
            user_id: Some("mock-user-1".into()),
            timestamp: Utc::now(),
            data,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LikeSession {
    pub total: u64,
    pub milestones: Vec<u64>,
    pub fired: HashSet<u64>,
}

impl Default for LikeSession {
    fn default() -> Self {
        Self {
            total: 0,
            milestones: vec![100, 500, 1_000, 5_000, 10_000, 25_000, 50_000, 100_000],
            fired: HashSet::new(),
        }
    }
}

impl LikeSession {
    pub fn add(&mut self, amount: u64) -> Vec<u64> {
        self.total = self.total.saturating_add(amount);
        let mut hits = Vec::new();
        for milestone in self.milestones.iter().copied() {
            if self.total >= milestone && self.fired.insert(milestone) {
                hits.push(milestone);
            }
        }
        hits
    }

    pub fn reset(&mut self) {
        self.total = 0;
        self.fired.clear();
    }
}

#[derive(Debug, Error)]
pub enum EventError {
    #[error("duplicate event")]
    Duplicate,
}

pub struct EventEngine {
    history: VecDeque<LiveEvent>,
    known_ids: HashSet<Uuid>,
    max_history: usize,
    pub likes: LikeSession,
}

impl Default for EventEngine {
    fn default() -> Self {
        Self {
            history: VecDeque::new(),
            known_ids: HashSet::new(),
            max_history: 2_000,
            likes: LikeSession::default(),
        }
    }
}

impl EventEngine {
    pub fn ingest(&mut self, event: LiveEvent) -> Result<Vec<LiveEvent>, EventError> {
        if !self.known_ids.insert(event.id) {
            return Err(EventError::Duplicate);
        }

        let mut generated = Vec::new();
        if event.event_type == EventType::Like {
            let amount = event.data.get("count").and_then(|v| v.as_u64()).unwrap_or(1);
            for milestone in self.likes.add(amount) {
                generated.push(LiveEvent {
                    id: Uuid::new_v4(),
                    provider: event.provider.clone(),
                    event_type: EventType::LikeMilestone,
                    username: event.username.clone(),
                    display_name: event.display_name.clone(),
                    user_id: event.user_id.clone(),
                    timestamp: Utc::now(),
                    data: serde_json::json!({"likes": self.likes.total, "milestone": milestone}),
                });
            }
        }

        self.push_history(event);
        for item in generated.iter().cloned() {
            self.known_ids.insert(item.id);
            self.push_history(item);
        }
        Ok(generated)
    }

    fn push_history(&mut self, event: LiveEvent) {
        self.history.push_front(event);
        while self.history.len() > self.max_history {
            if let Some(old) = self.history.pop_back() {
                self.known_ids.remove(&old.id);
            }
        }
    }

    pub fn history(&self) -> Vec<LiveEvent> {
        self.history.iter().cloned().collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_duplicate() {
        let mut engine = EventEngine::default();
        let event = LiveEvent::mock("FOLLOW");
        engine.ingest(event.clone()).unwrap();
        assert!(engine.ingest(event).is_err());
    }

    #[test]
    fn like_milestones_fire_once() {
        let mut likes = LikeSession::default();
        assert_eq!(likes.add(100), vec![100]);
        assert!(likes.add(20).is_empty());
        assert_eq!(likes.add(380), vec![500]);
        assert!(likes.add(0).is_empty());
    }
}
