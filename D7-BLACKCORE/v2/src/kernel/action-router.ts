import { CapabilityRegistry } from "./capability-registry.js";
export type ExecutionChannel="api"|"cli"|"dom"|"uia"|"vision";
export interface ActionRoute{channel:ExecutionChannel;capabilityId:string;score:number;}
const ORDER:ExecutionChannel[]=["api","cli","dom","uia","vision"];
export class ActionRouter{constructor(private readonly capabilities:CapabilityRegistry){}choose(routes:ActionRoute[],minScore=70):ActionRoute|null{const eligible=routes.filter(r=>r.score>=minScore&&this.capabilities.isUsable(r.capabilityId,minScore));eligible.sort((a,b)=>ORDER.indexOf(a.channel)-ORDER.indexOf(b.channel)||b.score-a.score);return eligible[0]??null;}}
