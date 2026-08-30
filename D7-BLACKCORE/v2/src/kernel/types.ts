export type Id = string;
export type RiskLevel = "safe" | "low" | "medium" | "high" | "critical";
export type ApprovalDecision = "auto" | "approve" | "deny";
export type CapabilityStatus = "verified" | "partial" | "untested" | "unavailable";
export type MissionStatus = "queued" | "planning" | "running" | "waiting_approval" | "verifying" | "completed" | "failed" | "cancelled";
export type StepStatus = "pending" | "running" | "completed" | "failed" | "cancelled" | "skipped";
export type AutonomyLevel = 0 | 1 | 2 | 3 | 4;
export type ModelPurpose = "planning" | "coding" | "vision" | "research" | "fast" | "reasoning";
export type PrivacyMode = "auto" | "local_only" | "max_quality" | "low_cost" | "custom";
export interface BlackcoreEvent<T = unknown> { id: Id; type: string; at: string; source: string; payload: T; correlationId?: Id; }
export interface MissionStep { id: Id; title: string; status: StepStatus; startedAt?: string; finishedAt?: string; timeoutMs?: number; evidenceIds: Id[]; error?: string; }
export interface Mission { id: Id; goal: string; modeIds: string[]; status: MissionStatus; createdAt: string; updatedAt: string; successCriteria: string[]; steps: MissionStep[]; evidenceIds: Id[]; qualityTarget: number; qualityScore?: number; }
export interface EvidenceRecord { id: Id; missionId: Id; stepId?: Id; kind: "file"|"metric"|"process"|"api"|"screenshot"|"test"|"user"|"other"; claim: string; value: unknown; at: string; verified: boolean; verifier: string; }
export interface CapabilityRecord { id: string; status: CapabilityStatus; score: number; lastVerifiedAt?: string; evidenceIds: Id[]; notes?: string; }
export interface ModeDefinition { id: string; name: string; purpose: string; priority: number; requiredCapabilities: string[]; conflictsWith?: string[]; }
export interface ModelProvider { id: string; label: string; local: boolean; enabled: boolean; purposes: ModelPurpose[]; quality: number; speed: number; cost: number; privacy: number; health: "online"|"offline"|"unknown"; }
export interface ModelRouteRequest { purpose: ModelPurpose; privacyMode: PrivacyMode; minQuality?: number; maxCost?: number; preferredProviderId?: string; }
export interface BlackcoreState { revision: number; autonomyLevel: AutonomyLevel; activeMissionId: Id|null; activeModes: string[]; systemStatus: "idle"|"thinking"|"acting"|"verifying"|"waiting"|"error"; approvalsPending: number; lastEventAt: string|null; }
