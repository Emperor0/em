export type CapabilityState = "verified" | "partial" | "untested" | "unavailable";

export interface GpuSnapshot {
  name: string;
  utilizationPercent: number | null;
  temperatureC: number | null;
  memoryUsedMb: number | null;
  memoryTotalMb: number | null;
}

export interface DiskSnapshot {
  mount: string;
  totalGb: number;
  freeGb: number;
}

export interface GovernorSnapshot {
  available: boolean;
  state: string | null;
  source: string | null;
}

export interface OpenCodeSnapshot {
  available: boolean;
  version: string | null;
  endpoint: string;
}

export interface DeviceSnapshot {
  capturedAt: string;
  hostname: string;
  os: string;
  cpuName: string;
  cpuUsagePercent: number;
  logicalCpus: number;
  ramTotalGb: number;
  ramUsedGb: number;
  ramUsagePercent: number;
  gpu: GpuSnapshot | null;
  disks: DiskSnapshot[];
  processCount: number;
  runningApps: string[];
  governor: GovernorSnapshot;
  openCode: OpenCodeSnapshot;
}

export interface CapabilitySnapshot {
  id: string;
  label: string;
  state: CapabilityState;
  evidence: string;
}

export interface RuntimeStatus {
  paused: boolean;
  uptimeSeconds: number;
  native: boolean;
  version: string;
}

export interface RuntimeOverview {
  runtime: RuntimeStatus;
  device: DeviceSnapshot;
  capabilities: CapabilitySnapshot[];
}
