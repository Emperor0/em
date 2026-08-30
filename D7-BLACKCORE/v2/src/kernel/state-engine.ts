import type { BlackcoreState } from "./types.js";
import { nowIso } from "./utils.js";
export type StateSubscriber = (state: Readonly<BlackcoreState>, previous: Readonly<BlackcoreState>) => void;
export class StateEngine {
  #state: BlackcoreState; #subscribers = new Set<StateSubscriber>();
  constructor(initial?: Partial<BlackcoreState>) { this.#state = { revision:0, autonomyLevel:2, activeMissionId:null, activeModes:[], systemStatus:"idle", approvalsPending:0, lastEventAt:null, ...initial }; }
  get(): Readonly<BlackcoreState> { return Object.freeze({ ...this.#state, activeModes:[...this.#state.activeModes] }); }
  update(patch: Partial<Omit<BlackcoreState,"revision">>): Readonly<BlackcoreState> { const previous=this.get(); this.#state={...this.#state,...patch,activeModes:patch.activeModes?[...new Set(patch.activeModes)]:this.#state.activeModes,revision:this.#state.revision+1,lastEventAt:patch.lastEventAt??this.#state.lastEventAt??nowIso()}; const current=this.get(); for(const s of this.#subscribers)s(current,previous); return current; }
  subscribe(subscriber: StateSubscriber):()=>void { this.#subscribers.add(subscriber); return()=>this.#subscribers.delete(subscriber); }
}
