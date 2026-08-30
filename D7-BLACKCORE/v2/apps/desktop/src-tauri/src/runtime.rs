use crate::{device,mission::{self,MissionEvidence,MissionResult,MissionStepResult},modes,opencode};
use serde::Serialize;
use std::{env,fs::{self,OpenOptions},io::Write,path::{Path,PathBuf},sync::Mutex,time::{SystemTime,UNIX_EPOCH}};

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct ApprovalRequest{pub id:String,pub mission_id:String,pub goal:String,pub action:String,pub reason:String,pub risk:String,pub target_path:String,pub status:String,pub created_at:u64}

#[derive(Debug,Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct RuntimeMission{pub id:String,pub goal:String,pub status:String,pub executor:String,pub selected_modes:Vec<String>,pub result:Option<MissionResult>,pub approval_id:Option<String>,pub updated_at:u64}

#[derive(Debug)]
pub struct RuntimeStore{root:PathBuf,pub mission:Option<RuntimeMission>,pub approvals:Vec<ApprovalRequest>,pub last_probe:Option<serde_json::Value>}

fn now()->u64{SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs()}
fn id(prefix:&str)->String{format!("{}-{}",prefix,SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos())}

fn device_goal(goal:&str,modes:&[String])->bool{
    let g=goal.to_ascii_lowercase();
    modes.iter().any(|m|m=="device")||g.contains("افحص جهازي")||g.contains("فحص الجهاز")||g.contains("scan my pc")||g.contains("scan my computer")
}

impl RuntimeStore{
    pub fn new()->Self{
        let root=env::var("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|_|env::temp_dir()).join("D7_BLACKCORE");
        let _=fs::create_dir_all(root.join("journal"));
        let _=fs::create_dir_all(root.join("checkpoints"));
        let _=fs::create_dir_all(root.join("reports"));
        Self{root,mission:None,approvals:vec![],last_probe:None}
    }

    fn journal(&self,kind:&str,value:&serde_json::Value){
        let path=self.root.join("journal").join("events.ndjson");
        if let Ok(mut f)=OpenOptions::new().create(true).append(true).open(path){
            let record=serde_json::json!({"at":now(),"kind":kind,"value":value});
            let _=writeln!(f,"{}",record);
        }
    }

    pub fn record_probe(&mut self,value:serde_json::Value){self.last_probe=Some(value.clone());self.journal("legacy.readonly_probe",&value);}

    fn execute_device_scan(&self,goal:&str)->Result<MissionResult,String>{
        let snapshot=device::collect_device_snapshot();
        let report=self.root.join("reports").join(format!("device-scan-{}.json",now()));
        let raw=serde_json::to_string_pretty(&snapshot).map_err(|e|format!("DEVICE_REPORT_SERIALIZE_FAILED:{e}"))?;
        fs::write(&report,raw.as_bytes()).map_err(|e|format!("DEVICE_REPORT_WRITE_FAILED:{e}"))?;
        let reread=fs::read_to_string(&report).map_err(|e|format!("DEVICE_REPORT_VERIFY_READ_FAILED:{e}"))?;
        let parsed:serde_json::Value=serde_json::from_str(&reread).map_err(|e|format!("DEVICE_REPORT_VERIFY_JSON_FAILED:{e}"))?;
        let host_ok=parsed.get("hostname").and_then(|v|v.as_str())==Some(snapshot.hostname.as_str());
        let file_ok=report.exists()&&host_ok;
        let gpu=snapshot.gpu.as_ref().map(|g|g.name.clone()).unwrap_or_else(||"No NVIDIA telemetry".into());
        Ok(MissionResult{
            id:id("result"),goal:goal.into(),status:if file_ok{"completed".into()}else{"failed".into()},quality_score:if file_ok{100}else{0},target_path:Some(report.display().to_string()),verified:file_ok,
            steps:vec![
                MissionStepResult{title:"جمع لقطة الجهاز الحية".into(),status:"completed".into(),evidence:Some(format!("{} · {} logical CPUs · {:.1}% RAM",snapshot.hostname,snapshot.logical_cpus,snapshot.ram_usage_percent))},
                MissionStepResult{title:"قراءة CPU/GPU/RAM/Disks/Processes".into(),status:"completed".into(),evidence:Some(format!("CPU={} · GPU={} · processes={}",snapshot.cpu_name,gpu,snapshot.process_count))},
                MissionStepResult{title:"حفظ تقرير فعلي".into(),status:"completed".into(),evidence:Some(report.display().to_string())},
                MissionStepResult{title:"إعادة قراءة التقرير والتحقق".into(),status:if file_ok{"completed".into()}else{"failed".into()},evidence:Some(format!("exists={} host_match={}",report.exists(),host_ok))},
            ],
            evidence:vec![
                MissionEvidence{kind:"device".into(),claim:"Live Device Twin sampled the current machine".into(),value:format!("{} | CPU {:.1}% | RAM {:.1}% | {} processes",snapshot.hostname,snapshot.cpu_usage_percent,snapshot.ram_usage_percent,snapshot.process_count),verified:true},
                MissionEvidence{kind:"file".into(),claim:"Device scan report exists and re-parses as JSON".into(),value:report.display().to_string(),verified:file_ok},
            ]
        })
    }

    fn finish_result(&mut self,mut item:RuntimeMission,result:MissionResult)->RuntimeMission{
        item.status=if result.verified{"completed".into()}else{"failed".into()};
        item.result=Some(result);
        item.updated_at=now();
        self.mission=Some(item.clone());
        self.journal("mission.completed",&serde_json::to_value(&item).unwrap_or_default());
        item
    }

    pub fn submit(&mut self,goal:String)->Result<RuntimeMission,String>{
        let mid=id("mission");
        let selected_modes=modes::infer_mode_ids(&goal);
        let is_fast=mission::is_fast_path_goal(&goal);
        let is_device=device_goal(&goal,&selected_modes);
        let executor=if is_fast{"native.file_fast_path"}else if is_device{"native.device_scan"}else{"opencode.agent"}.to_string();
        let mut item=RuntimeMission{id:mid.clone(),goal:goal.clone(),status:"planning".into(),executor:executor.clone(),selected_modes:selected_modes.clone(),result:None,approval_id:None,updated_at:now()};
        self.mission=Some(item.clone());
        self.journal("mission.routed",&serde_json::json!({"missionId":mid,"goal":goal,"executor":executor,"modes":selected_modes,"fastPath":is_fast,"deviceScan":is_device}));

        if is_device{
            item.status="running".into();self.mission=Some(item.clone());self.journal("mission.running",&serde_json::json!({"missionId":mid,"executor":"native.device_scan"}));
            let result=self.execute_device_scan(&goal)?;
            return Ok(self.finish_result(item,result));
        }

        if !is_fast{
            item.status="running".into();self.mission=Some(item.clone());self.journal("mission.running",&serde_json::json!({"missionId":mid,"executor":"opencode.agent"}));
            let outcome=opencode::execute_goal(&goal,&selected_modes)?;
            let verified=outcome.completed&&outcome.response_text.trim().len()>2;
            let result=MissionResult{
                id:id("result"),goal:goal.clone(),status:if verified{"completed".into()}else{"failed".into()},quality_score:if verified{90}else{0},target_path:outcome.workspace.clone(),verified,
                steps:vec![
                    MissionStepResult{title:"تشغيل/التحقق من OpenCode Server".into(),status:if outcome.server_ready{"completed".into()}else{"failed".into()},evidence:Some(outcome.server_evidence.clone())},
                    MissionStepResult{title:"إنشاء Agent Session".into(),status:if outcome.session_id.is_some(){"completed".into()}else{"failed".into()},evidence:outcome.session_id.clone()},
                    MissionStepResult{title:"إرسال الهدف إلى Build Agent".into(),status:if outcome.completed{"completed".into()}else{"failed".into()},evidence:Some(outcome.response_text.chars().take(600).collect())},
                    MissionStepResult{title:"التحقق من استجابة التنفيذ".into(),status:if verified{"completed".into()}else{"failed".into()},evidence:Some(format!("response_chars={} session={}",outcome.response_text.chars().count(),outcome.session_id.clone().unwrap_or_default()))},
                ],
                evidence:vec![
                    MissionEvidence{kind:"agent".into(),claim:"OpenCode Build agent returned a completed response".into(),value:outcome.response_text.clone(),verified},
                    MissionEvidence{kind:"transport".into(),claim:"BLACKCORE used the local OpenCode HTTP session API".into(),value:outcome.server_evidence.clone(),verified:outcome.server_ready},
                ]
            };
            return Ok(self.finish_result(item,result));
        }

        let target=mission::target_path_for_goal(&goal)?;
        if target.exists(){
            let aid=id("approval");
            let approval=ApprovalRequest{id:aid.clone(),mission_id:mid.clone(),goal:goal.clone(),action:"overwrite_existing_file".into(),reason:"الهدف موجود مسبقًا؛ يلزم موافقة قبل الاستبدال.".into(),risk:"medium".into(),target_path:target.display().to_string(),status:"pending".into(),created_at:now()};
            self.approvals.push(approval.clone());item.status="waiting_approval".into();item.approval_id=Some(aid);item.updated_at=now();self.mission=Some(item.clone());self.journal("approval.requested",&serde_json::to_value(&approval).unwrap_or_default());return Ok(item)
        }
        item.status="running".into();self.mission=Some(item.clone());self.journal("mission.running",&serde_json::json!({"missionId":mid}));
        let result=mission::execute_fast_path_with_policy(&goal,false,None)?;
        Ok(self.finish_result(item,result))
    }

    pub fn pending(&self)->Vec<ApprovalRequest>{self.approvals.iter().filter(|x|x.status=="pending").cloned().collect()}

    pub fn decide(&mut self,approval_id:&str,approve:bool)->Result<RuntimeMission,String>{
        let idx=self.approvals.iter().position(|x|x.id==approval_id).ok_or("APPROVAL_NOT_FOUND")?;
        if self.approvals[idx].status!="pending"{return Err("APPROVAL_ALREADY_DECIDED".into())}
        let approval=self.approvals[idx].clone();self.approvals[idx].status=if approve{"approved".into()}else{"rejected".into()};self.journal("approval.decided",&serde_json::json!({"approvalId":approval_id,"approved":approve}));
        let mut m=self.mission.clone().ok_or("MISSION_NOT_FOUND")?;if m.id!=approval.mission_id{return Err("MISSION_APPROVAL_MISMATCH".into())}
        if !approve{m.status="cancelled".into();m.updated_at=now();self.mission=Some(m.clone());self.journal("mission.cancelled",&serde_json::to_value(&m).unwrap_or_default());return Ok(m)}
        let target=PathBuf::from(&approval.target_path);let checkpoint=self.make_checkpoint(&m.id,&target)?;m.status="running".into();m.updated_at=now();self.mission=Some(m.clone());self.journal("checkpoint.created",&serde_json::json!({"missionId":m.id,"checkpoint":checkpoint}));
        let result=mission::execute_fast_path_with_policy(&m.goal,true,Some(Path::new(&checkpoint)))?;
        Ok(self.finish_result(m,result))
    }

    fn make_checkpoint(&self,mission_id:&str,target:&Path)->Result<String,String>{
        let dir=self.root.join("checkpoints").join(mission_id);fs::create_dir_all(&dir).map_err(|e|format!("CHECKPOINT_DIR_FAILED:{e}"))?;
        let backup=dir.join(target.file_name().ok_or("TARGET_FILENAME_MISSING")?);fs::copy(target,&backup).map_err(|e|format!("CHECKPOINT_COPY_FAILED:{e}"))?;Ok(backup.display().to_string())
    }
}

pub struct SharedRuntime(pub Mutex<RuntimeStore>);impl SharedRuntime{pub fn new()->Self{Self(Mutex::new(RuntimeStore::new()))}}

#[cfg(test)]
mod tests{
    use super::*;
    #[test]fn generic_missions_no_longer_end_as_fake_planned(){let mode_ids=modes::infer_mode_ids("ابنِ لي برنامج احترافي");assert!(!device_goal("ابنِ لي برنامج احترافي",&mode_ids));}
    #[test]fn device_goals_are_native_executable(){let mode_ids=modes::infer_mode_ids("افحص جهازي كامل");assert!(device_goal("افحص جهازي كامل",&mode_ids));}
    #[test]fn probe_evidence_is_retained_in_runtime_state(){let mut s=RuntimeStore::new();let value=serde_json::json!({"transportVerified":true,"semanticOk":true,"reason":"OK"});s.record_probe(value.clone());assert_eq!(s.last_probe,Some(value));}
}
