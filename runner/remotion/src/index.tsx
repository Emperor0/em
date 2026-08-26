import {registerRoot, Composition} from "remotion";
import React from "react";
import {ViralShort} from "./Video";

const defaultProps={voice:"",assets:[],title:"",script:""};

const Root=()=>{
  return <Composition
    id="ViralShort"
    component={ViralShort}
    width={1080}
    height={1920}
    fps={30}
    durationInFrames={300}
    defaultProps={defaultProps}
    calculateMetadata={({props}:any)=>({
      durationInFrames: Math.max(
        30,
        Math.round((props.assets || []).reduce((sum:number,x:any)=>sum + Number(x.duration || 2),0) * 30)
      )
    })}
  />;
};

registerRoot(Root);
