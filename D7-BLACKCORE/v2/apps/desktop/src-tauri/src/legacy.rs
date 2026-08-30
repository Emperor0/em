use serde::Serialize;
use std::{collections::hash_map::DefaultHasher,env,fs,hash::{Hash,Hasher},path::{Path,PathBuf}};

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct ProtocolInspection {
    pub source_available: bool,
    pub source_fingerprint: Option<String>,
    pub line_count: usize,
    pub handler_candidates: Vec<String>,
    pub command_candidates: Vec<String>,
    pub safe_probe_candidates: Vec<String>,
    pub framing_signals: Vec<String>,
    pub protocol_signals: Vec<String>,
    pub encoding: String,
    pub discriminator_field: Option<String>,
    pub framing_mode: String,
    pub confidence: u8,
    pub probe_ready: bool,
    pub probe_reason: String,
}

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct LegacyAgentSnapshot {
    pub source_path: String,
    pub source_available: bool,
    pub pipe_server_source_available: bool,
    pub request_pipe: String,
    pub request_pipe_available: bool,
    pub event_pipe: String,
    pub event_pipe_available: bool,
    pub protocol_hint: String,
    pub protocol: ProtocolInspection,
}

#[cfg(target_os="windows")]
fn named_pipe_available(path:&str)->bool {
    use std::{ffi::OsStr,os::windows::ffi::OsStrExt};
    use windows_sys::Win32::System::Pipes::WaitNamedPipeW;
    let mut wide:Vec<u16>=OsStr::new(path).encode_wide().collect();
    wide.push(0);
    unsafe { WaitNamedPipeW(wide.as_ptr(),0)!=0 }
}
#[cfg(not(target_os="windows"))]
fn named_pipe_available(_path:&str)->bool { false }

fn fingerprint(raw:&str)->String {
    let mut h=DefaultHasher::new();
    raw.hash(&mut h);
    format!("{:016x}",h.finish())
}
fn unique_push(out:&mut Vec<String>,value:String) {
    let v=value.trim().to_string();
    if !v.is_empty() && !out.iter().any(|x|x==&v) { out.push(v) }
}
fn quoted_tokens(line:&str)->Vec<String> {
    let mut out=vec![];
    for quote in ['\'','"'] {
        let mut rest=line;
        loop {
            let Some(a)=rest.find(quote) else { break };
            let tail=&rest[a+1..];
            let Some(b)=tail.find(quote) else { break };
            let token=tail[..b].trim();
            if !token.is_empty() { out.push(token.to_string()) }
            rest=&tail[b+1..];
        }
    }
    out
}
fn looks_like_command(v:&str)->bool {
    let x=v.trim();
    !x.is_empty() && x.len()<=80 && x.chars().all(|c|c.is_ascii_alphanumeric()||matches!(c,'_'|'-'|'.'|':'))
}
fn safe_probe(v:&str)->bool {
    matches!(v.to_ascii_lowercase().as_str(),
        "ping"|"health"|"status"|"get_status"|"info"|"version"|"capabilities"|
        "get_capabilities"|"list_tools"|"tools.list"|"system.info")
}
fn is_framing_signal(low:&str)->bool {
    let transport=["\\\\.\\pipe\\","namedpipe","message_mode","byte_mode","recv(","readfile","writefile","send(","write("].iter().any(|k|low.contains(k));
    let boundary=["\\n","newline","delimiter","length_prefix","length prefix","prefix","struct.pack","struct.unpack","readline","splitlines"].iter().any(|k|low.contains(k));
    transport||boundary
}
fn infer_discriminator(raw:&str)->Option<String> {
    let lower=raw.to_ascii_lowercase();
    for field in ["action","command","method","op","request_type","message_type","type"] {
        let a=format!("get('{}')",field);
        let b=format!("get(\"{}\")",field);
        let c=format!("['{}']",field);
        let d=format!("[\"{}\"]",field);
        if lower.contains(&a)||lower.contains(&b)||lower.contains(&c)||lower.contains(&d) {
            return Some(field.to_string());
        }
    }
    None
}
fn infer_encoding(raw:&str)->String {
    let low=raw.to_ascii_lowercase();
    let loads=low.contains("json.loads");
    let dumps=low.contains("json.dumps");
    if loads&&dumps { "json".into() } else if loads||dumps { "json-partial".into() } else { "unknown".into() }
}
fn infer_framing(raw:&str)->String {
    let low=raw.to_ascii_lowercase();
    let newline=low.contains("b'\\n'")||low.contains("b\"\\n\"")||low.contains("readline(")||low.contains("splitlines(")||low.contains("newline");
    let length=(low.contains("struct.pack")&&low.contains("struct.unpack"))||low.contains("length_prefix")||low.contains("length prefix");
    let message=low.contains("message_mode")||low.contains("pipe_type_message")||low.contains("readmode_message");
    if newline { "newline-json".into() } else if length { "length-prefix".into() } else if message { "message-json".into() } else { "unknown".into() }
}
fn protocol_gate(encoding:&str,discriminator:&Option<String>,framing:&str,safe_count:usize)->(u8,bool,String) {
    let mut confidence=10u8;
    if discriminator.is_some() { confidence+=25; }
    if encoding=="json" { confidence+=25; } else if encoding=="json-partial" { confidence+=10; }
    if safe_count>0 { confidence+=20; }
    if framing=="newline-json" { confidence+=20; } else if framing!="unknown" { confidence+=10; }
    let ready=confidence>=90 && encoding=="json" && discriminator.is_some() && framing=="newline-json" && safe_count>0;
    let reason=if ready {
        "Verified local source proves JSON request/response, discriminator, newline framing and a whitelisted read-only command.".into()
    } else {
        let mut missing=vec![];
        if encoding!="json" { missing.push("full-json"); }
        if discriminator.is_none() { missing.push("discriminator"); }
        if framing!="newline-json" { missing.push("newline-framing"); }
        if safe_count==0 { missing.push("safe-command"); }
        format!("Fail-closed: missing or ambiguous {}.",missing.join(", "))
    };
    (confidence.min(100),ready,reason)
}
fn empty_inspection()->ProtocolInspection {
    ProtocolInspection {
        source_available:false,source_fingerprint:None,line_count:0,handler_candidates:vec![],command_candidates:vec![],safe_probe_candidates:vec![],framing_signals:vec![],protocol_signals:vec![],
        encoding:"unknown".into(),discriminator_field:None,framing_mode:"unknown".into(),confidence:0,probe_ready:false,probe_reason:"Fail-closed: pipe_server.py source is unavailable.".into(),
    }
}
fn inspect_source_at(path:&Path)->ProtocolInspection {
    let raw=match fs::read_to_string(path) { Ok(v)=>v, Err(_)=>return empty_inspection() };
    let mut handlers=vec![];
    let mut commands=vec![];
    let mut framing=vec![];
    let mut signals=vec![];
    for line in raw.lines() {
        let t=line.trim();
        let low=t.to_ascii_lowercase();
        if low.starts_with("def ")||low.starts_with("async def ") {
            if let Some(name)=t.split_whitespace().nth(if low.starts_with("async"){2}else{1}) {
                unique_push(&mut handlers,name.split('(').next().unwrap_or(name).to_string())
            }
        }
        let command_line=["action","method","command","op","request_type","message_type","type"].iter().any(|k|low.contains(k))
            && (low.contains("==")||low.contains(" in ")||low.contains("match ")||low.contains("case "));
        if command_line {
            for q in quoted_tokens(t) { if looks_like_command(&q) { unique_push(&mut commands,q) } }
        }
        if ["json.loads","json.dumps","recv(","send(","readfile","writefile","struct.pack","struct.unpack","splitlines","readline","newline","length_prefix","message_type","request_id","action","method","command","event"].iter().any(|k|low.contains(k)) {
            unique_push(&mut signals,t.chars().take(220).collect())
        }
        if is_framing_signal(&low) { unique_push(&mut framing,t.chars().take(220).collect()) }
    }
    let safe:Vec<String>=commands.iter().filter(|x|safe_probe(x)).cloned().collect();
    let encoding=infer_encoding(&raw);
    let discriminator=infer_discriminator(&raw);
    let framing_mode=infer_framing(&raw);
    let (confidence,probe_ready,probe_reason)=protocol_gate(&encoding,&discriminator,&framing_mode,safe.len());
    ProtocolInspection {
        source_available:true,source_fingerprint:Some(fingerprint(&raw)),line_count:raw.lines().count(),
        handler_candidates:handlers.into_iter().take(60).collect(),command_candidates:commands.into_iter().take(100).collect(),safe_probe_candidates:safe,
        framing_signals:framing.into_iter().take(60).collect(),protocol_signals:signals.into_iter().take(120).collect(),
        encoding,discriminator_field:discriminator,framing_mode,confidence,probe_ready,probe_reason,
    }
}
fn candidate_roots()->Vec<PathBuf> {
    let mut out=vec![PathBuf::from(r"C:\D7 Agent\D7-Agent"),PathBuf::from(r"C:\D7Agent"),PathBuf::from(r"C:\D7_Agent")];
    if let Ok(home)=env::var("USERPROFILE") {
        for suffix in [r"Desktop\D7-Agent",r"Desktop\D7 Agent\D7-Agent",r"Documents\D7-Agent",r"Downloads\D7-Agent"] { out.push(Path::new(&home).join(suffix)) }
    }
    out
}
fn locate_source()->(PathBuf,PathBuf) {
    for root in candidate_roots() {
        let pipe=root.join(r"src\d7_agent\core\pipe_server.py");
        if pipe.exists() { return (root,pipe) }
    }
    let root=PathBuf::from(r"C:\D7 Agent\D7-Agent");
    let pipe=root.join(r"src\d7_agent\core\pipe_server.py");
    (root,pipe)
}
pub fn probe_legacy_agent()->LegacyAgentSnapshot {
    let (root,pipe_server)=locate_source();
    let request_pipe=r"\\.\pipe\d7_agent_core";
    let event_pipe=r"\\.\pipe\d7_agent_events";
    let protocol=inspect_source_at(&pipe_server);
    let hint=if protocol.probe_ready { "source-verified-newline-json" } else { "unverified" };
    LegacyAgentSnapshot {
        source_path:root.display().to_string(),source_available:root.exists(),pipe_server_source_available:pipe_server.exists(),
        request_pipe:request_pipe.into(),request_pipe_available:named_pipe_available(request_pipe),event_pipe:event_pipe.into(),event_pipe_available:named_pipe_available(event_pipe),
        protocol_hint:hint.into(),protocol,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn write_fixture(name:&str,raw:&str)->PathBuf {
        let p=env::temp_dir().join(format!("blackcore-{name}-{}.py",std::process::id()));
        fs::write(&p,raw).unwrap();
        p
    }
    #[test]
    fn inspector_proves_safe_newline_json_protocol() {
        let raw=r#"PIPE_NAME = r'\\.\pipe\d7_agent_core'
def handle_request(msg):
    action = msg.get('action')
    if action == 'ping':
        return {'ok': True}
    elif action == 'run_tool':
        return {'ok': True}
    payload = json.loads(data.decode('utf-8'))
    conn.send(json.dumps({'event':'done'}).encode('utf-8') + b'\n')
"#;
        let p=write_fixture("pipe-ready",raw);
        let x=inspect_source_at(&p);
        assert!(x.source_available);
        assert_eq!(x.discriminator_field.as_deref(),Some("action"));
        assert_eq!(x.encoding,"json");
        assert_eq!(x.framing_mode,"newline-json");
        assert!(x.command_candidates.iter().any(|v|v=="ping"));
        assert!(x.command_candidates.iter().any(|v|v=="run_tool"));
        assert!(x.safe_probe_candidates.iter().any(|v|v=="ping"));
        assert!(!x.safe_probe_candidates.iter().any(|v|v=="run_tool"));
        assert!(x.probe_ready);
        assert!(x.confidence>=90);
        let _=fs::remove_file(p);
    }
    #[test]
    fn gate_refuses_ambiguous_protocol() {
        let raw="def handle_request(data):\n    return data\n";
        let p=write_fixture("pipe-ambiguous",raw);
        let x=inspect_source_at(&p);
        assert!(!x.probe_ready);
        assert_eq!(x.encoding,"unknown");
        assert_eq!(x.framing_mode,"unknown");
        assert!(x.probe_reason.starts_with("Fail-closed"));
        let _=fs::remove_file(p);
    }
}
