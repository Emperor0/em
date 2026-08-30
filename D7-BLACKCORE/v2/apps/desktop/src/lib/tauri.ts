import { invoke } from "@tauri-apps/api/core";
import type { MissionResult, RuntimeOverview, RuntimeStatus } from "./types";

export const getOverview = () => invoke<RuntimeOverview>("get_runtime_overview");
export const setPaused = (paused:boolean) => invoke<RuntimeStatus>("set_runtime_paused", { paused });
export const runFastPathMission = (goal:string) => invoke<MissionResult>("run_fast_path_mission", { goal });
