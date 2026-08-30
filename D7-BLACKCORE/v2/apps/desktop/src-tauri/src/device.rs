use serde::Serialize;
use std::{fs, io::{Read, Write}, net::{SocketAddr, TcpStream}, path::Path, process::Command, time::Duration};
use sysinfo::{Disks, System};

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GpuSnapshot {
    pub name: String,
    pub utilization_percent: Option<f32>,
    pub temperature_c: Option<f32>,
    pub memory_used_mb: Option<f32>,
    pub memory_total_mb: Option<f32>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DiskSnapshot { pub mount: String, pub total_gb: f64, pub free_gb: f64 }

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GovernorSnapshot { pub available: bool, pub state: Option<String>, pub source: Option<String> }

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OpenCodeSnapshot { pub available: bool, pub version: Option<String>, pub endpoint: String }

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceSnapshot {
    pub captured_at: String,
    pub hostname: String,
    pub os: String,
    pub cpu_name: String,
    pub cpu_usage_percent: f32,
    pub logical_cpus: usize,
    pub ram_total_gb: f64,
    pub ram_used_gb: f64,
    pub ram_usage_percent: f64,
    pub gpu: Option<GpuSnapshot>,
    pub disks: Vec<DiskSnapshot>,
    pub process_count: usize,
    pub running_apps: Vec<String>,
    pub governor: GovernorSnapshot,
    pub open_code: OpenCodeSnapshot,
}

fn now_iso() -> String {
    format!("{}", std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_secs())
}

fn bytes_to_gb(value: u64) -> f64 { value as f64 / 1024.0 / 1024.0 / 1024.0 }

fn probe_gpu() -> Option<GpuSnapshot> {
    let mut cmd = Command::new("nvidia-smi");
    cmd.args([
        "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total",
        "--format=csv,noheader,nounits",
    ]);
    #[cfg(target_os = "windows")]
    cmd.creation_flags(0x08000000);
    let output = cmd.output().ok()?;
    if !output.status.success() { return None; }
    let line = String::from_utf8_lossy(&output.stdout).lines().next()?.trim().to_string();
    let parts: Vec<_> = line.split(',').map(|x| x.trim()).collect();
    if parts.len() < 5 { return None; }
    Some(GpuSnapshot {
        name: parts[0].to_string(),
        utilization_percent: parts[1].parse().ok(),
        temperature_c: parts[2].parse().ok(),
        memory_used_mb: parts[3].parse().ok(),
        memory_total_mb: parts[4].parse().ok(),
    })
}

fn probe_governor() -> GovernorSnapshot {
    let path = r"C:\ProgramData\D7PerformanceGovernor\status.json";
    if !Path::new(path).exists() {
        return GovernorSnapshot { available: false, state: None, source: None };
    }
    let raw = match fs::read_to_string(path) {
        Ok(v) => v,
        Err(_) => return GovernorSnapshot { available: true, state: Some("READ_ERROR".into()), source: Some(path.into()) },
    };
    let value: serde_json::Value = serde_json::from_str(&raw).unwrap_or(serde_json::Value::Null);
    let state = value.get("state").or_else(|| value.get("mode")).or_else(|| value.get("status"))
        .and_then(|x| x.as_str()).map(|x| x.to_string()).or_else(|| Some("CONNECTED".into()));
    GovernorSnapshot { available: true, state, source: Some(path.into()) }
}

fn probe_opencode() -> OpenCodeSnapshot {
    let endpoint = "http://127.0.0.1:8082".to_string();
    let addr: SocketAddr = "127.0.0.1:8082".parse().unwrap();
    let mut stream = match TcpStream::connect_timeout(&addr, Duration::from_millis(350)) {
        Ok(s) => s,
        Err(_) => return OpenCodeSnapshot { available: false, version: None, endpoint },
    };
    let _ = stream.set_read_timeout(Some(Duration::from_millis(500)));
    let request = b"GET /global/health HTTP/1.1\r\nHost: 127.0.0.1:8082\r\nConnection: close\r\n\r\n";
    if stream.write_all(request).is_err() { return OpenCodeSnapshot { available: false, version: None, endpoint }; }
    let mut response = String::new();
    if stream.read_to_string(&mut response).is_err() { return OpenCodeSnapshot { available: false, version: None, endpoint }; }
    let available = response.contains("200 OK") && response.contains("healthy");
    let version = response.split("\r\n\r\n").nth(1)
        .and_then(|body| serde_json::from_str::<serde_json::Value>(body).ok())
        .and_then(|v| v.get("version").and_then(|x| x.as_str()).map(str::to_owned));
    OpenCodeSnapshot { available, version, endpoint }
}

pub fn collect_device_snapshot() -> DeviceSnapshot {
    let mut sys = System::new_all();
    std::thread::sleep(Duration::from_millis(120));
    sys.refresh_all();

    let total = sys.total_memory();
    let used = sys.used_memory();
    let ram_usage = if total > 0 { used as f64 / total as f64 * 100.0 } else { 0.0 };
    let cpu_name = sys.cpus().first().map(|c| c.brand().to_string()).unwrap_or_else(|| "Unknown CPU".into());
    let cpu_usage = sys.global_cpu_usage();

    let mut running_apps = Vec::new();
    let process_names: Vec<String> = sys.processes().values()
        .map(|p| p.name().to_string_lossy().to_ascii_lowercase()).collect();
    let probes = [
        ("chrome", ["chrome.exe", "chrome"]),
        ("discord", ["discord.exe", "discord"]),
        ("obs", ["obs64.exe", "obs64"]),
        ("tiktok live studio", ["tiktok live studio.exe", "tiktok_live_studio.exe"]),
        ("steam", ["steam.exe", "steam"]),
        ("opencode", ["opencode.exe", "opencode"]),
    ];
    for (label, aliases) in probes {
        if aliases.iter().any(|alias| process_names.iter().any(|p| p == alias)) { running_apps.push(label.to_string()); }
    }

    let disks = Disks::new_with_refreshed_list().iter().map(|d| DiskSnapshot {
        mount: d.mount_point().to_string_lossy().to_string(),
        total_gb: bytes_to_gb(d.total_space()),
        free_gb: bytes_to_gb(d.available_space()),
    }).collect();

    DeviceSnapshot {
        captured_at: now_iso(),
        hostname: System::host_name().unwrap_or_else(|| "Windows PC".into()),
        os: format!("{} {}", System::name().unwrap_or_else(|| "Windows".into()), System::os_version().unwrap_or_default()).trim().to_string(),
        cpu_name,
        cpu_usage_percent: cpu_usage,
        logical_cpus: sys.cpus().len(),
        ram_total_gb: bytes_to_gb(total),
        ram_used_gb: bytes_to_gb(used),
        ram_usage_percent: ram_usage,
        gpu: probe_gpu(),
        disks,
        process_count: sys.processes().len(),
        running_apps,
        governor: probe_governor(),
        open_code: probe_opencode(),
    }
}
