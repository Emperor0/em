import React from "react";
import {AbsoluteFill, Audio, Img, OffthreadVideo, Sequence, interpolate, useCurrentFrame, staticFile} from "remotion";

type Asset={type:"video"|"image";path:string;duration:number};
type Props={voice:string;assets:Asset[];title:string;script:string};

const Caption=({text}:{text:string})=>{
  const f=useCurrentFrame();
  const scale=interpolate(f,[0,8],[0.92,1],{extrapolateRight:"clamp"});
  return <div style={{
    position:"absolute",bottom:170,left:60,right:60,textAlign:"center",
    fontSize:58,fontWeight:900,lineHeight:1.05,color:"white",
    textShadow:"0 4px 20px #000,0 0 4px #000",
    transform:`scale(${scale})`
  }}>{text}</div>
}

export const ViralShort:React.FC<Props>=({voice,assets,title})=>{
  let start=0;
  return <AbsoluteFill style={{backgroundColor:"#05070d"}}>
    {assets.map((a,i)=>{
      const frames=Math.max(24,Math.round(a.duration*30));
      const s=start; start+=frames;
      return <Sequence key={i} from={s} durationInFrames={frames}>
        <AbsoluteFill>
          {a.type==="video"
            ? <OffthreadVideo src={"file://"+a.path} style={{width:"100%",height:"100%",objectFit:"cover"}} muted/>
            : <Img src={"file://"+a.path} style={{width:"100%",height:"100%",objectFit:"cover"}}/>
          }
          <AbsoluteFill style={{background:"linear-gradient(180deg,rgba(0,0,0,.08),rgba(0,0,0,.08) 55%,rgba(0,0,0,.5))"}}/>
        </AbsoluteFill>
      </Sequence>
    })}
    <Audio src={"file://"+voice}/>
    <Caption text={title}/>
  </AbsoluteFill>
}
