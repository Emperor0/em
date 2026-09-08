use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct TickerConfig {
    pub messages: Vec<String>,
    pub speed_px_sec: f32,
    pub direction: String,
    pub font_family: String,
    pub font_size: u32,
    pub weight: u16,
    pub opacity: f32,
    pub separator: String,
    pub background: String,
    pub foreground: String,
    pub glow: bool,
}

impl Default for TickerConfig {
    fn default() -> Self {
        Self {
            messages: vec!["لا تنسى الفولو".into(), "لا تنسى ذكر الله".into(), "رابط الدعم في البايو".into()],
            speed_px_sec: 110.0,
            direction: "rtl".into(),
            font_family: "Segoe UI".into(),
            font_size: 32,
            weight: 700,
            opacity: 1.0,
            separator: "•".into(),
            background: "#A7191E".into(),
            foreground: "#FFFFFF".into(),
            glow: false,
        }
    }
}
