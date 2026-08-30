import{invoke}from"@tauri-apps/api/core";import type{ApprovalRequest,RuntimeMission,RuntimeOverview,RuntimeStatus}from"./types";
export const getOverview=()=>invoke<RuntimeOverview>("get_runtime_overview");
export const setPaused=(paused:boolean)=>invoke<RuntimeStatus>("set_runtime_paused",{paused});
export const submitMission=(goal:string)=>invoke<RuntimeMission>("submit_mission",{goal});
export const getActiveMission=()=>invoke<RuntimeMission|null>("get_active_mission");
export const getPendingApprovals=()=>invoke<ApprovalRequest[]>("get_pending_approvals");
export const decideApproval=(approvalId:string,approve:boolean)=>invoke<RuntimeMission>("decide_approval",{approvalId,approve});
