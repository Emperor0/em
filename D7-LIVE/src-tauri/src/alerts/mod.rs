use crate::events::{EventType,LiveEvent};
use serde::{Serialize,Deserialize};
use std::collections::VecDeque;

#[derive(Debug,Clone,Serialize,Deserialize)]
pub struct AlertJob { pub id:String, pub priority:u16, pub text:String, pub duration_ms:u64 }

#[derive(Default)] pub struct AlertQueue { jobs:VecDeque<AlertJob> }
impl AlertQueue {
  pub fn enqueue(&mut self,job:AlertJob){
    let pos=self.jobs.iter().position(|x|x.priority<job.priority).unwrap_or(self.jobs.len()); self.jobs.insert(pos,job)
  }
  pub fn next(&mut self)->Option<AlertJob>{self.jobs.pop_front()}
  pub fn from_event(e:&LiveEvent)->AlertJob{
    let (priority,text)=match e.event_type{
      EventType::Gift=>(40,format!("{} sent a gift",e.display_name)),EventType::Subscribe=>(80,format!("{} subscribed",e.display_name)),EventType::LikeMilestone=>(70,"LIKE MILESTONE".into()),EventType::Follow=>(50,format!("{} تابعك الآن 👑",e.display_name)),_=>(20,e.display_name.clone())};
    AlertJob{id:e.id.to_string(),priority,text,duration_ms:4500}
  }
}
