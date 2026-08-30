use serde::Serialize;
use std::{collections::hash_map::DefaultHasher,fs,hash::{Hash,Hasher},path::Path};

#[derive(Debug,Clone,Serialize)]#[serde(rename_all="camelCase")]pub struct ProtocolInspection{pub source_available:bool,pub source_fingerprint:Option<String>,pub line_count:usize,pub handler_candidates:Vec<String>,pub framing_signals:Vec<String>,pub protocol_signals:Vec<String>}
#[derive(Debug,Clone,Serialize)]#[serde(rename_all="camelCase")]pub struct LegacyAgentSnapshot{pub source_path:String,pub source_available:bool,pub pipe_server_source_available:bool,pub request_pipe:String,pub request_pipe_available:bool,pub event_pipe:String,pub event_pipe_available:bool,pub protocol_hint:String,pub protocol:ProtocolInspection}

#[cfg(target_os="windows")]
fn named_pipe_available(path:&str)->bool{use std::{ffi::OsStr,os::windows::ffi::OsStrExt};use windows_sys::Win32::System::Pipes::WaitNamedPipeW;let mut wide:Vec<u16>=OsStr::new(path).encode_wide().collect();wide.push(0);unsafe{WaitNamedPipeW(wide.as_ptr(),0)!=0}}
#[cfg(not(target_os="windows"))]fn named_pipe_available(_path:&str)->bool{false}

fn fingerprint(raw:&str)->String{let mut h=DefaultHasher::new();raw.hash(&mut h);format!("{:016x}",h.finish())}
fn unique_push(out:&mut Vec<String>,value:String){if!value.trim().is_empty()&&!out.iter().any(|x|x==&value){out.push(value)}}
fn quoted_tokens(line:&str)->Vec<String>{let mut out=vec![];for quote in ['\'','"']{let mut rest=line;loop{let Some(a)=rest.find(quote)else{break};let tail=&rest[a+1..];let Some(b)=tail.find(quote)else{break};let token=tail[..b].trim();if!token.is_empty(){out.push(token.to_string())}rest=&tail[b+1..];}}out}
fn inspect_source_at(path:&Path)->ProtocolInspection{let raw=match fs::read_to_string(path){Ok(v)=>v,Err(_)=>return ProtocolInspection{source_available:false,source_fingerprint:None,line_count:0,handler_candidates:vec![],framing_signals:vec![],protocol_signals:vec![]}};let mut handlers=vec![];let mut framing=vec![];let mut signals=vec![];for line in raw.lines(){let t=line.trim();let low=t.to_ascii_lowercase();if low.starts_with("def ")||low.starts_with("async def "){if let Some(name)=t.split_whitespace().nth(if low.starts_with("async"){2}else{1}){unique_push(&mut handlers,name.split('(').next().unwrap_or(name).to_string())}}
if ["json.loads","json.dumps","recv(","send(","readfile","writefile","struct.pack","struct.unpack","splitlines","readline","newline","length_prefix","message_type","request_id","action","method","command","event"].iter().any(|k|low.contains(k)){unique_push(&mut signals,t.chars().take(220).collect())}
if ["\\\\.\\pipe\\","namedpipe","message_mode","byte_mode","\n","newline","length","prefix","delimiter","recv(","readfile"].iter().any(|k|low.contains(&k.to_ascii_lowercase())){unique_push(&mut framing,t.chars().take(220).collect())}
if low.contains("pipe")||low.contains("handler")||low.contains("command")||low.contains("action")||low.contains("method"){for q in quoted_tokens(t){if q.len()<=120&&(q.contains("d7_")||q.contains("pipe")||q.contains("command")||q.contains("action")||q.contains("method")||q.contains("event")){unique_push(&mut signals,q)}}}}
ProtocolInspection{source_available:true,source_fingerprint:Some(fingerprint(&raw)),line_count:raw.lines().count(),handler_candidates:handlers.into_iter().take(40).collect(),framing_signals:framing.into_iter().take(40).collect(),protocol_signals:signals.into_iter().take(80).collect()}}

pub fn probe_legacy_agent()->LegacyAgentSnapshot{let source_path=r"C:\D7 Agent\D7-Agent";let pipe_server=r"C:\D7 Agent\D7-Agent\src\d7_agent\core\pipe_server.py";let request_pipe=r"\\.\pipe\d7_agent_core";let event_pipe=r"\\.\pipe\d7_agent_events";LegacyAgentSnapshot{source_path:source_path.into(),source_available:Path::new(source_path).exists(),pipe_server_source_available:Path::new(pipe_server).exists(),request_pipe:request_pipe.into(),request_pipe_available:named_pipe_available(request_pipe),event_pipe:event_pipe.into(),event_pipe_available:named_pipe_available(event_pipe),protocol_hint:"v2-duplex".into(),protocol:inspect_source_at(Path::new(pipe_server))}}

#[cfg(test)]mod tests{use super::*;use std::env;#[test]fn protocol_inspector_extracts_python_signals(){let p=env::temp_dir().join(format!("blackcore-pipe-inspector-{}.py",std::process::id()));let raw=r#"PIPE_NAME = r'\\.\pipe\d7_agent_core'
def handle_request(msg):
    action = msg.get('action')
    payload = json.loads(data.decode('utf-8'))
    conn.send(json.dumps({'event':'done'}).encode('utf-8') + b'\n')
"#;fs::write(&p,raw).unwrap();let x=inspect_source_at(&p);assert!(x.source_available);assert!(x.line_count>=4);assert!(x.handler_candidates.iter().any(|v|v=="handle_request"));assert!(x.protocol_signals.iter().any(|v|v.contains("json.loads")));assert!(x.framing_signals.iter().any(|v|v.contains("send")));assert!(x.source_fingerprint.is_some());let _=fs::remove_file(p);}}
