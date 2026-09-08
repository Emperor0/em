use crate::events::{EventType, LiveEvent};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum Condition {
    Always,
    GiftNameEquals { value: String },
    GiftCountAtLeast { value: u64 },
    CommentContains { value: String },
    UsernameEquals { value: String },
    MilestoneAtLeast { value: u64 },
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum RuleAction {
    ShowAlert { template: String },
    PlaySound { path: String },
    PlayMedia { path: String },
    ShowSource { source_id: String },
    HideSource { source_id: String },
    ChangeScene { scene_id: String },
    ChangeText { source_id: String, value: String },
    AddTickerMessage { value: String },
    IncrementCounter { name: String, amount: i64 },
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Rule {
    pub id: String,
    pub name: String,
    pub enabled: bool,
    pub event_type: EventType,
    pub conditions: Vec<Condition>,
    pub actions: Vec<RuleAction>,
}

impl Rule {
    pub fn matches(&self, event: &LiveEvent) -> bool {
        self.enabled && self.event_type == event.event_type && self.conditions.iter().all(|c| c.matches(event))
    }
}

impl Condition {
    fn matches(&self, event: &LiveEvent) -> bool {
        match self {
            Condition::Always => true,
            Condition::GiftNameEquals { value } => event.data.get("gift_name").and_then(|v| v.as_str()).is_some_and(|v| v.eq_ignore_ascii_case(value)),
            Condition::GiftCountAtLeast { value } => event.data.get("gift_count").and_then(|v| v.as_u64()).unwrap_or(0) >= *value,
            Condition::CommentContains { value } => event
                .data
                .get("message")
                .and_then(|v| v.as_str())
                .is_some_and(|m| m.to_lowercase().contains(&value.to_lowercase())),
            Condition::UsernameEquals { value } => event.username.eq_ignore_ascii_case(value),
            Condition::MilestoneAtLeast { value } => event.data.get("milestone").and_then(|v| v.as_u64()).unwrap_or(0) >= *value,
        }
    }
}

pub fn default_rules() -> Vec<Rule> {
    vec![
        Rule {
            id: "default-follow".into(),
            name: "New Follow".into(),
            enabled: true,
            event_type: EventType::Follow,
            conditions: vec![Condition::Always],
            actions: vec![RuleAction::ShowAlert { template: "follow".into() }],
        },
        Rule {
            id: "default-gift".into(),
            name: "Gift".into(),
            enabled: true,
            event_type: EventType::Gift,
            conditions: vec![Condition::Always],
            actions: vec![RuleAction::ShowAlert { template: "gift".into() }],
        },
        Rule {
            id: "default-like".into(),
            name: "Like Milestone".into(),
            enabled: true,
            event_type: EventType::LikeMilestone,
            conditions: vec![Condition::Always],
            actions: vec![RuleAction::ShowAlert { template: "like_milestone".into() }],
        },
    ]
}

pub fn evaluate<'a>(rules: &'a [Rule], event: &LiveEvent) -> Vec<&'a RuleAction> {
    rules.iter().filter(|r| r.matches(event)).flat_map(|r| r.actions.iter()).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn comment_rule_matches_case_insensitively() {
        let event = LiveEvent::mock("COMMENT");
        let rule = Rule {
            id: "1".into(),
            name: "GG".into(),
            enabled: true,
            event_type: EventType::Comment,
            conditions: vec![Condition::CommentContains { value: "gg".into() }],
            actions: vec![RuleAction::IncrementCounter { name: "gg".into(), amount: 1 }],
        };
        assert!(rule.matches(&event));
    }
}
