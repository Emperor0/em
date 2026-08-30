import { CapabilityRegistry } from "./capability-registry.js";
import { EvidenceEngine } from "./evidence-engine.js";
import { EventBus } from "./event-bus.js";
import { MissionEngine } from "./mission-engine.js";
import { ModeRegistry } from "./mode-registry.js";
import { ModelGateway } from "./model-gateway.js";
import { PermissionEngine } from "./permission-engine.js";
import { StateEngine } from "./state-engine.js";
export class BlackcoreKernel { readonly state=new StateEngine(); readonly events=new EventBus(); readonly permissions=new PermissionEngine(); readonly evidence=new EvidenceEngine(); readonly capabilities=new CapabilityRegistry(); readonly models=new ModelGateway(); readonly modes=new ModeRegistry(); readonly missions=new MissionEngine(this.evidence); constructor(){this.events.on("mission.started",async event=>{const payload=event.payload as {missionId:string;modes:string[]};this.state.update({activeMissionId:payload.missionId,activeModes:payload.modes,systemStatus:"acting",lastEventAt:event.at});});this.events.on("mission.completed",async event=>{this.state.update({activeMissionId:null,activeModes:[],systemStatus:"idle",lastEventAt:event.at});});} }
