import { invoke } from "@tauri-apps/api/core";
import type { RuntimeOverview, RuntimeStatus } from "./types";

export const getOverview = () => invoke<RuntimeOverview>("get_runtime_overview");
export const setPaused = (paused: boolean) => invoke<RuntimeStatus>("set_runtime_paused", { paused });
