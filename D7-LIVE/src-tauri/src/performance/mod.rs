use serde::{Deserialize, Serialize};
use sysinfo::{CpuRefreshKind, MemoryRefreshKind, RefreshKind, System};

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct PerformanceSnapshot {
    pub cpu_percent: f32,
    pub gpu_percent: Option<f32>,
    pub vram_used_mb: Option<u64>,
    pub ram_used_mb: u64,
    pub ram_total_mb: u64,
    pub render_fps: f32,
    pub dropped_frames: u64,
    pub frame_time_ms: f32,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub enum PerformanceMode {
    Quality,
    Balanced,
    Performance,
}

pub struct PerformanceMonitor {
    system: System,
}

impl Default for PerformanceMonitor {
    fn default() -> Self {
        let mut system = System::new_with_specifics(
            RefreshKind::nothing().with_cpu(CpuRefreshKind::everything()).with_memory(MemoryRefreshKind::everything()),
        );
        system.refresh_cpu_all();
        system.refresh_memory();
        Self { system }
    }
}

impl PerformanceMonitor {
    pub fn snapshot(&mut self) -> PerformanceSnapshot {
        self.system.refresh_cpu_usage();
        self.system.refresh_memory();
        let cpu = if self.system.cpus().is_empty() { 0.0 } else { self.system.cpus().iter().map(|c| c.cpu_usage()).sum::<f32>() / self.system.cpus().len() as f32 };
        PerformanceSnapshot {
            cpu_percent: cpu,
            gpu_percent: None,
            vram_used_mb: None,
            ram_used_mb: self.system.used_memory() / 1024 / 1024,
            ram_total_mb: self.system.total_memory() / 1024 / 1024,
            render_fps: 0.0,
            dropped_frames: 0,
            frame_time_ms: 0.0,
        }
    }
}
