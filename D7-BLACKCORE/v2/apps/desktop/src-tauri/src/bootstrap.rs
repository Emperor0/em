use crate::{legacy,opencode};
use std::{env,fs::{self,OpenOptions},io::Write,path::{Path,PathBuf},process::{Command,Stdio},thread,time::Duration};
#[cfg(target_os="windows")]
use std::os::windows::process::CommandExt;

const CREATE_NO_WINDOW:u32=0x08000000;

fn log_line(message:&str){
    let root=env::var("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|_|env::temp_dir()).join("D7_BLACKCORE");
    let _=fs::create_dir_all(&root);
    let path=root.join("bootstrap.log");
    if let Ok(mut f)=OpenOptions::new().create(true).append(true).open(path){
        let now=std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_secs();
        let _=writeln!(f,"{} {}",now,message);
    }
}

fn hidden_command(program:&str)->Command{
    let mut c=Command::new(program);
    c.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
    #[cfg(target_os="windows")]
    c.creation_flags(CREATE_NO_WINDOW);
    c
}

fn start_batch(path:&Path)->Result<String,String>{
    let mut c=hidden_command("cmd.exe");
    c.arg("/C").arg(path).current_dir(path.parent().unwrap_or_else(||Path::new(".")));
    c.spawn().map_err(|e|format!("D7_AGENT_BATCH_START_FAILED:{e}"))?;
    Ok(format!("started {}",path.display()))
}

fn start_python_module(root:&Path)->Result<String,String>{
    let src=root.join("src");
    let package=src.join("d7_agent");
    if !package.is_dir(){return Err("D7_AGENT_PYTHON_PACKAGE_NOT_FOUND".into())}
    let module_entry=package.join("__main__.py");
    let script_entry=package.join("main.py");
    if !module_entry.is_file()&&!script_entry.is_file(){return Err("D7_AGENT_PYTHON_ENTRY_NOT_FOUND".into())}
    let mut last=String::new();
    for python in ["pythonw.exe","python.exe","py.exe"]{
        let mut c=hidden_command(python);
        if python.eq_ignore_ascii_case("py.exe"){c.arg("-3");}
        if module_entry.is_file(){c.args(["-m","d7_agent"]);}else{c.arg(&script_entry);}
        c.current_dir(root).env("PYTHONPATH",&src);
        match c.spawn(){
            Ok(_)=>return Ok(if module_entry.is_file(){format!("started python module d7_agent with {}",python)}else{format!("started {} with {}",script_entry.display(),python)}),
            Err(e)=>last=format!("{}: {}",python,e)
        }
    }
    Err(format!("D7_AGENT_PYTHON_START_FAILED:{last}"))
}

fn ensure_d7_agent()->Result<String,String>{
    let before=legacy::probe_legacy_agent();
    if before.request_pipe_available&&before.event_pipe_available{
        return Ok(format!("D7 Agent already online: {} + {}",before.request_pipe,before.event_pipe));
    }
    if !before.source_available{return Err(format!("D7_AGENT_SOURCE_NOT_FOUND:{}",before.source_path))}
    let root=PathBuf::from(&before.source_path);
    let mut launch_evidence=String::new();
    for name in ["START_D7_AGENT.bat","START_AGENT.bat","start_agent.bat","run_agent.bat"]{
        let p=root.join(name);
        if p.is_file(){launch_evidence=start_batch(&p)?;break}
    }
    if launch_evidence.is_empty(){launch_evidence=start_python_module(&root)?;}
    for _ in 0..40{
        thread::sleep(Duration::from_millis(250));
        let now=legacy::probe_legacy_agent();
        if now.request_pipe_available&&now.event_pipe_available{
            return Ok(format!("{}; pipes online: {} + {}",launch_evidence,now.request_pipe,now.event_pipe));
        }
    }
    let after=legacy::probe_legacy_agent();
    Err(format!("D7_AGENT_START_TIMEOUT:{}; request_online={} event_online={} request={} event={}",launch_evidence,after.request_pipe_available,after.event_pipe_available,after.request_pipe,after.event_pipe))
}

pub fn launch_background(){
    thread::spawn(||{
        log_line("BOOTSTRAP_BEGIN");
        match opencode::ensure_server(){Ok(v)=>log_line(&format!("OPENCODE_OK {v}")),Err(e)=>log_line(&format!("OPENCODE_FAIL {e}"))}
        match ensure_d7_agent(){Ok(v)=>log_line(&format!("D7_AGENT_OK {v}")),Err(e)=>log_line(&format!("D7_AGENT_FAIL {e}"))}
        log_line("BOOTSTRAP_END");
    });
}

#[cfg(test)]
mod tests{
    use super::*;
    #[test]fn bootstrap_log_root_is_resolvable(){let root=env::var("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|_|env::temp_dir()).join("D7_BLACKCORE");assert!(!root.as_os_str().is_empty());}
    #[test]fn python_entry_selection_is_fail_closed(){let root=env::temp_dir().join("d7-blackcore-bootstrap-test");let package=root.join("src").join("d7_agent");let _=fs::remove_dir_all(&root);fs::create_dir_all(&package).unwrap();assert!(start_python_module(&root).unwrap_err().contains("ENTRY_NOT_FOUND"));let _=fs::remove_dir_all(root);}
}
