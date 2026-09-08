use chrono::{DateTime,Utc};
use serde::{Serialize,Deserialize};
use std::collections::VecDeque;
use thiserror::Error;
use uuid::Uuid;

#[derive(Debug,Clone,Serialize,Deserialize,PartialEq,Eq)]
#[serde(rename_all="snake_case")]
pub enum EventType { Follow, Like, LikeMilestone, Comment, Gift, Subscribe, Share, ViewerJoin, Custom }

#[derive(Debug,Clone,Serialize,Deserialize)]
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
    pub fn mock(kind:&str)->Self {
        let event_type=match kind.to_ascii_uppercase().as_str(){
            "FOLLOW"=>EventType::Follow,"1000_LIKES"=>EventType::LikeMilestone,"GIFT"=>EventType::Gift,
            "SUBSCRIPTION"=>EventType::Subscribe,"COMMENT"=>EventType::Comment,"SHARE"=>EventType::Share,_=>EventType::Custom};
        Self{id:Uuid::new_v4(),provider:"mock_tiktok".into(),event_type,username:"test_user".into(),display_name:"Test User".into(),user_id:None,timestamp:Utc::now(),data:serde_json::json!({"likes":1000,"gift_name":"Rose","gift_count":1,"message":"GG"})}
    }
}

#[derive(Debug,Error)] pub enum EventError { #[error("duplicate event")] Duplicate }

pub struct EventEngine { history:VecDeque<LiveEvent>, max_history:usize }
impl Default for EventEngine { fn default()->Self{Self{history:VecDeque::new(),max_history:1000}} }
impl EventEngine {
    pub fn ingest(&mut self,event:LiveEvent)->Result<(),EventError>{
        if self.history.iter().any(|e|e.id==event.id){return Err(EventError::Duplicate)}
        self.history.push_front(event); while self.history.len()>self.max_history{self.history.pop_back();} Ok(())
    }
    pub fn history(&self)->impl Iterator<Item=&LiveEvent>{self.history.iter()}
}

#[cfg(test)] mod tests {use super::*; #[test] fn rejects_duplicate(){let mut e=EventEngine::default();let x=LiveEvent::mock("FOLLOW");e.ingest(x.clone()).unwrap();assert!(e.ingest(x).is_err());}}
