use crate::events::{EventType, LiveEvent};
use serde::{Deserialize, Serialize};
use std::collections::VecDeque;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct AlertJob {
    pub id: String,
    pub priority: u16,
    pub template: String,
    pub text: String,
    pub username: String,
    pub duration_ms: u64,
    pub sound: Option<String>,
    pub media: Option<String>,
    pub data: serde_json::Value,
}

#[derive(Default)]
pub struct AlertQueue {
    jobs: VecDeque<AlertJob>,
}

impl AlertQueue {
    pub fn enqueue(&mut self, job: AlertJob) {
        if let Some(existing) = self.merge_compatible(&job) {
            *existing = merge_gift_jobs(existing.clone(), job);
            return;
        }
        let pos = self.jobs.iter().position(|x| x.priority < job.priority).unwrap_or(self.jobs.len());
        self.jobs.insert(pos, job);
    }

    pub fn next(&mut self) -> Option<AlertJob> {
        self.jobs.pop_front()
    }

    pub fn snapshot(&self) -> Vec<AlertJob> {
        self.jobs.iter().cloned().collect()
    }

    fn merge_compatible(&mut self, incoming: &AlertJob) -> Option<&mut AlertJob> {
        if incoming.template != "gift" {
            return None;
        }
        let incoming_gift = incoming.data.get("gift_name").and_then(|v| v.as_str());
        self.jobs.iter_mut().find(|job| {
            job.template == "gift"
                && job.username == incoming.username
                && job.data.get("gift_name").and_then(|v| v.as_str()) == incoming_gift
        })
    }

    pub fn from_event(event: &LiveEvent) -> Option<AlertJob> {
        let (priority, template, text, duration_ms) = match event.event_type {
            EventType::Gift => {
                let gift = event.data.get("gift_name").and_then(|v| v.as_str()).unwrap_or("Gift");
                let count = event.data.get("gift_count").and_then(|v| v.as_u64()).unwrap_or(1);
                (40, "gift", format!("{} sent {} ×{}", event.display_name, gift, count), 4_500)
            }
            EventType::Subscribe => (80, "subscription", format!("{} subscribed 👑", event.display_name), 5_500),
            EventType::LikeMilestone => {
                let milestone = event.data.get("milestone").and_then(|v| v.as_u64()).unwrap_or(0);
                (70, "like_milestone", format!("{} LIKES 🔥", format_number(milestone)), 4_500)
            }
            EventType::Follow => (50, "follow", format!("{} تابعك الآن 👑", event.display_name), 4_500),
            EventType::Share => (30, "share", format!("{} شارك البث", event.display_name), 3_500),
            _ => return None,
        };
        Some(AlertJob {
            id: event.id.to_string(),
            priority,
            template: template.into(),
            text,
            username: event.username.clone(),
            duration_ms,
            sound: None,
            media: None,
            data: event.data.clone(),
        })
    }
}

fn merge_gift_jobs(mut a: AlertJob, b: AlertJob) -> AlertJob {
    let a_count = a.data.get("gift_count").and_then(|v| v.as_u64()).unwrap_or(1);
    let b_count = b.data.get("gift_count").and_then(|v| v.as_u64()).unwrap_or(1);
    let count = a_count.saturating_add(b_count);
    let gift = a.data.get("gift_name").and_then(|v| v.as_str()).unwrap_or("Gift").to_string();
    if let Some(obj) = a.data.as_object_mut() {
        obj.insert("gift_count".into(), serde_json::json!(count));
    }
    a.text = format!("{} sent {} ×{}", a.username, gift, count);
    a
}

fn format_number(n: u64) -> String {
    let text = n.to_string();
    let mut result = String::new();
    for (i, ch) in text.chars().rev().enumerate() {
        if i > 0 && i % 3 == 0 {
            result.push(',');
        }
        result.push(ch);
    }
    result.chars().rev().collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn higher_priority_goes_first() {
        let mut q = AlertQueue::default();
        q.enqueue(AlertQueue::from_event(&LiveEvent::mock("FOLLOW")).unwrap());
        q.enqueue(AlertQueue::from_event(&LiveEvent::mock("SUBSCRIPTION")).unwrap());
        assert_eq!(q.next().unwrap().template, "subscription");
    }

    #[test]
    fn merges_same_gift() {
        let mut q = AlertQueue::default();
        let a = AlertQueue::from_event(&LiveEvent::mock("GIFT")).unwrap();
        let b = AlertQueue::from_event(&LiveEvent::mock("GIFT")).unwrap();
        q.enqueue(a);
        q.enqueue(b);
        let item = q.next().unwrap();
        assert_eq!(item.data.get("gift_count").and_then(|v| v.as_u64()), Some(2));
    }
}
