use serde::{Serialize,Deserialize};
#[derive(Debug,Clone,Serialize,Deserialize)]
pub struct TickerConfig { pub messages:Vec<String>,pub speed_px_sec:f32,pub direction:String,pub font_size:u32,pub opacity:f32 }
impl Default for TickerConfig {fn default()->Self{Self{messages:vec!["لا تنسى الفولو".into(),"لا تنسى ذكر الله".into(),"رابط الدعم في البايو".into()],speed_px_sec:110.,direction:"rtl".into(),font_size:32,opacity:1.}}}
