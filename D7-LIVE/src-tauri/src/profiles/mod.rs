use serde::{Serialize,Deserialize};
#[derive(Debug,Clone,Serialize,Deserialize)]
pub struct Profile { pub name:String,pub width:u32,pub height:u32,pub fps:u32,pub encoder:String,pub bitrate_kbps:u32 }
impl Default for Profile {fn default()->Self{Self{name:"TikTok Gaming".into(),width:1080,height:1920,fps:60,encoder:"NVENC H.264".into(),bitrate_kbps:6000}}}
