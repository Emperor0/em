import {registerRoot, Composition} from "remotion";
import React from "react";
import {ViralShort} from "./Video";

const Root=()=>{
  const props=(globalThis as any).__D7_PROPS__ || {voice:"",assets:[],title:"",script:""};
  const frames=Math.max(300,Math.round(props.assets.reduce((a:any,x:any)=>a+Number(x.duration||2),0)*30));
  return <Composition id="ViralShort" component={ViralShort} width={1080} height={1920} fps={30} durationInFrames={frames} defaultProps={props}/>;
};
registerRoot(Root);
