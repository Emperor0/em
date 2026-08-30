use reqwest::blocking::Client;
use serde::Serialize;
use serde_json::{json,Value};
use std::{env,fs,path::{Path,PathBuf},process::{Command,Stdio},thread,time::Duration};
#[cfg(target_os="windows")]use std::os::windows::process::CommandExt;

const ENDPOINT:&str="http://127.0.0.1:8082";

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct AgentOutcome{
    pub server_ready:bool,
    pub server_evidence:String,
    pub session_id:Option<String>,
    pub completed:bool,
    pub response_text:String,
    pub workspace:Option<String>,
    pub diff_count:usize,
    pub changed_paths:Vec<String>,
}

fn client(timeout:Duration)->Result<Client,String>{
    Client::builder().timeout(timeout).build().map_err(|e|format!("OPENCODE_HTTP_CLIENT_FAILED:{e}"))
}

fn health()->Result<String,String>{
    let r=client(Duration::from_secs(2))?.get(format!("{ENDPOINT}/global/health")).send().map_err(|e|format!("OPENCODE_HEALTH_FAILED:{e}"))?;
    if !r.status().is_success(){return Err(format!("OPENCODE_HEALTH_HTTP:{}",r.status()))}
    let v:Value=r.json().map_err(|e|format!("OPENCODE_HEALTH_JSON_FAILED:{e}"))?;
    if v.get("healthy").and_then(|x|x.as_bool())!=Some(true){return Err("OPENCODE_NOT_HEALTHY".into())}
    Ok(v.get("version").and_then(|x|x.as_str()).unwrap_or("unknown").to_string())
}

fn workspace_root()->Result<PathBuf,String>{
    let base=env::var("USERPROFILE").map(PathBuf::from).or_else(|_|env::var("LOCALAPPDATA").map(PathBuf::from)).map_err(|_|"OPENCODE_WORKSPACE_ROOT_NOT_FOUND".to_string())?;
    let p=base.join("Documents").join("D7_BLACKCORE_WORKSPACE");
    fs::create_dir_all(&p).map_err(|e|format!("OPENCODE_WORKSPACE_CREATE_FAILED:{e}"))?;
    Ok(p)
}

fn find_executable()->Option<PathBuf>{
    if let Ok(home)=env::var("USERPROFILE"){
        let home=PathBuf::from(home);
        for p in [home.join(".opencode").join("bin").join("opencode.exe"),home.join(".local").join("bin").join("opencode.exe"),home.join(".local").join("bin").join("tcc-opencode.exe")]{if p.is_file(){return Some(p)}}
    }
    None
}

fn spawn_server(workspace:&Path)->Result<String,String>{
    if let Ok(v)=health(){return Ok(format!("existing local server healthy v{v} at {ENDPOINT}"))}
    let exe=find_executable().ok_or("OPENCODE_EXE_NOT_FOUND")?;
    let mut c=Command::new(&exe);
    c.args(["serve","--hostname","127.0.0.1","--port","8082"]).current_dir(workspace).stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
    #[cfg(target_os="windows")]c.creation_flags(0x08000000);
    c.spawn().map_err(|e|format!("OPENCODE_SERVER_START_FAILED:{e}"))?;
    for _ in 0..40{
        thread::sleep(Duration::from_millis(250));
        if let Ok(v)=health(){return Ok(format!("BLACKCORE started OpenCode v{v} from {} in {}",exe.display(),workspace.display()))}
    }
    Err("OPENCODE_SERVER_START_TIMEOUT".into())
}

fn text_parts(v:&Value)->String{
    v.get("parts").and_then(|x|x.as_array()).map(|parts|{
        parts.iter().filter_map(|p|{
            let kind=p.get("type").and_then(|x|x.as_str()).unwrap_or("");
            if kind=="text"{p.get("text").and_then(|x|x.as_str()).map(str::to_owned)}else{None}
        }).collect::<Vec<_>>().join("\n")
    }).unwrap_or_default()
}

fn diff_paths(v:&Value)->Vec<String>{
    let mut out=Vec::new();
    if let Some(items)=v.as_array(){
        for item in items{
            for key in ["file","path","newPath","oldPath"]{
                if let Some(s)=item.get(key).and_then(|x|x.as_str()){
                    if !s.is_empty()&&!out.iter().any(|x|x==s){out.push(s.to_string())}
                }
            }
        }
    }
    out
}

pub fn execute_goal(goal:&str,modes:&[String])->Result<AgentOutcome,String>{
    let workspace=workspace_root()?;
    let server_evidence=spawn_server(&workspace)?;
    let http=client(Duration::from_secs(180))?;
    let session=http.post(format!("{ENDPOINT}/session")).json(&json!({"title":"D7 BLACKCORE mission"})).send().map_err(|e|format!("OPENCODE_SESSION_CREATE_FAILED:{e}"))?;
    if !session.status().is_success(){return Err(format!("OPENCODE_SESSION_CREATE_HTTP:{}",session.status()))}
    let session_json:Value=session.json().map_err(|e|format!("OPENCODE_SESSION_CREATE_JSON_FAILED:{e}"))?;
    let session_id=session_json.get("id").and_then(|x|x.as_str()).ok_or("OPENCODE_SESSION_ID_MISSING")?.to_string();
    let mode_hint=if modes.is_empty(){"general".to_string()}else{modes.join(", ")};
    let proof_file=format!("BLACKCORE_RESULT_{}.md",session_id.replace(|c:char|!c.is_ascii_alphanumeric(),"_"));
    let prompt=format!(r#"You are the execution worker inside D7 BLACKCORE. Execute the user's goal, do not merely plan it.

Strict boundaries:
- Work only inside the current BLACKCORE workspace unless the goal explicitly requires a read-only inspection elsewhere.
- Never send messages, publish content, buy/pay, expose credentials, disable security, delete important user data, or make irreversible external changes.
- Do not access secret/env credential files.
- Prefer deterministic tools and verify what you create.
- For a broad request, produce the strongest useful local artifact/prototype/report now instead of stopping at a plan.
- Before finishing, create {proof_file} in the current workspace containing: original goal, work performed, files created/changed, tests/checks run, remaining blockers. This proof file must reflect actual work.

BLACKCORE mode routing: {mode_hint}
User goal: {goal}
"#);
    let message=http.post(format!("{ENDPOINT}/session/{session_id}/message")).json(&json!({"agent":"build","parts":[{"type":"text","text":prompt}]})).send().map_err(|e|format!("OPENCODE_MESSAGE_FAILED:{e}"))?;
    if !message.status().is_success(){return Err(format!("OPENCODE_MESSAGE_HTTP:{}",message.status()))}
    let message_json:Value=message.json().map_err(|e|format!("OPENCODE_MESSAGE_JSON_FAILED:{e}"))?;
    let response_text=text_parts(&message_json);
    let diff=http.get(format!("{ENDPOINT}/session/{session_id}/diff")).send().map_err(|e|format!("OPENCODE_DIFF_FAILED:{e}"))?;
    let diff_json:Value=if diff.status().is_success(){diff.json().unwrap_or_else(|_|json!([]))}else{json!([])};
    let changed_paths=diff_paths(&diff_json);
    let proof_exists=workspace.join(&proof_file).is_file();
    let completed=!response_text.trim().is_empty()&&(proof_exists||!changed_paths.is_empty());
    Ok(AgentOutcome{server_ready:true,server_evidence,session_id:Some(session_id),completed,response_text,workspace:Some(workspace.display().to_string()),diff_count:diff_json.as_array().map(|x|x.len()).unwrap_or(0),changed_paths})
}

#[cfg(test)]
mod tests{
    use super::*;
    #[test]fn text_parser_only_accepts_text_parts(){let v=json!({"parts":[{"type":"text","text":"done"},{"type":"tool","text":"ignore"}]});assert_eq!(text_parts(&v),"done");}
    #[test]fn diff_parser_deduplicates_paths(){let v=json!([{"file":"a.txt"},{"path":"a.txt"},{"path":"b.txt"}]);assert_eq!(diff_paths(&v),vec!["a.txt".to_string(),"b.txt".to_string()]);}
}
