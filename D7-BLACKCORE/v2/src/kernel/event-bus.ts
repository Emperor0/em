import type { BlackcoreEvent } from "./types.js";
import { id, nowIso } from "./utils.js";
export type EventHandler<T=unknown>=(event:BlackcoreEvent<T>)=>void|Promise<void>;
interface HandlerEntry { priority:number; handler:EventHandler; }
export class EventBus {
  #handlers=new Map<string,HandlerEntry[]>(); #history:BlackcoreEvent[]=[];
  constructor(private readonly maxHistory=500){}
  on<T=unknown>(type:string,handler:EventHandler<T>,priority=0):()=>void { const list=this.#handlers.get(type)??[]; const entry:HandlerEntry={priority,handler:handler as EventHandler}; list.push(entry); list.sort((a,b)=>b.priority-a.priority); this.#handlers.set(type,list); return()=>this.#handlers.set(type,(this.#handlers.get(type)??[]).filter(x=>x!==entry)); }
  async emit<T>(type:string,source:string,payload:T,correlationId?:string):Promise<BlackcoreEvent<T>> { const event:BlackcoreEvent<T>={id:id(),type,at:nowIso(),source,payload,...(correlationId?{correlationId}:{})}; this.#history.push(event as BlackcoreEvent); if(this.#history.length>this.maxHistory)this.#history.splice(0,this.#history.length-this.maxHistory); for(const {handler} of this.#handlers.get(type)??[])await handler(event); for(const {handler} of this.#handlers.get("*")??[])await handler(event); return event; }
  history(type?:string):readonly BlackcoreEvent[]{ return Object.freeze(type?this.#history.filter(e=>e.type===type):[...this.#history]); }
}
