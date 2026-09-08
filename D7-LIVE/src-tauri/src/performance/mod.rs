use serde::{Serialize,Deserialize};
#[derive(Debug,Clone,Serialize,Deserialize,Default)]
pub struct PerformanceSnapshot { pub cpu_percent:f32,pub gpu_percent:f32,pub vram_used_mb:u64,pub ram_used_mb:u64,pub render_fps:f32,pub dropped_frames:u64,pub frame_time_ms:f32 }
#[derive(Debug,Clone,Copy,Serialize,Deserialize)] pub enum PerformanceMode { Quality,Balanced,Performance }
