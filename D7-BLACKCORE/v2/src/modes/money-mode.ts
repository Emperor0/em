export interface Opportunity{id:string;title:string;buyerEvidence:number;paymentLikelihood:number;deliveryFit:number;competition:number;speedToCash:number;reachability:number;}
export function opportunityScore(o:Opportunity):number{const positive=o.buyerEvidence*.25+o.paymentLikelihood*.2+o.deliveryFit*.2+o.speedToCash*.2+o.reachability*.15;return Math.round(Math.max(0,Math.min(100,positive-o.competition*.18))*100)/100;}
export function shouldValidateBeforeBuild(o:Opportunity):boolean{return o.buyerEvidence<75||o.paymentLikelihood<60||o.reachability<55;}
