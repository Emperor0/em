use serde::Serialize;
use std::path::Path;

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LegacyAgentSnapshot {
    pub source_path: String,
    pub source_available: bool,
    pub pipe_server_source_available: bool,
    pub request_pipe: String,
    pub request_pipe_available: bool,
    pub event_pipe: String,
    pub event_pipe_available: bool,
    pub protocol_hint: String,
}

#[cfg(target_os = "windows")]
fn named_pipe_available(path: &str) -> bool {
    use std::{ffi::OsStr, os::windows::ffi::OsStrExt};
    use windows_sys::Win32::System::Pipes::WaitNamedPipeW;
    let mut wide: Vec<u16> = OsStr::new(path).encode_wide().collect();
    wide.push(0);
    unsafe { WaitNamedPipeW(wide.as_ptr(), 0) != 0 }
}

#[cfg(not(target_os = "windows"))]
fn named_pipe_available(_path: &str) -> bool { false }

pub fn probe_legacy_agent() -> LegacyAgentSnapshot {
    let source_path = r"C:\D7 Agent\D7-Agent";
    let pipe_server = r"C:\D7 Agent\D7-Agent\src\d7_agent\core\pipe_server.py";
    let request_pipe = r"\\.\pipe\d7_agent_core";
    let event_pipe = r"\\.\pipe\d7_agent_events";
    LegacyAgentSnapshot {
        source_path: source_path.into(),
        source_available: Path::new(source_path).exists(),
        pipe_server_source_available: Path::new(pipe_server).exists(),
        request_pipe: request_pipe.into(),
        request_pipe_available: named_pipe_available(request_pipe),
        event_pipe: event_pipe.into(),
        event_pipe_available: named_pipe_available(event_pipe),
        protocol_hint: "v2-duplex".into(),
    }
}
