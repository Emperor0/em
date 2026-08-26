import React from "react";
import {
  AbsoluteFill,
  Audio,
  Img,
  OffthreadVideo,
  Sequence,
  interpolate,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";

type Asset={type:"video"|"image";path:string;duration:number};
type Props={voice:string;assets:Asset[];title:string;script:string};

const Shot:React.FC<{asset:Asset;index:number}>=({asset,index})=>{
  const frame=useCurrentFrame();
  const frames=Math.max(24,Math.round(asset.duration*30));
  const opacity=interpolate(frame,[0,4,frames-4,frames],[0,1,1,0],{extrapolateLeft:"clamp",extrapolateRight:"clamp"});
  const scale=asset.type==="image"
    ? interpolate(frame,[0,frames],[1.08,1.20],{extrapolateRight:"clamp"})
    : interpolate(frame,[0,frames],[1.025,1.07],{extrapolateRight:"clamp"});
  const x=asset.type==="image" ? interpolate(frame,[0,frames],index%2===0?-18:18,index%2===0?18:-18) : 0;
  return <AbsoluteFill style={{opacity,overflow:"hidden",backgroundColor:"#05070d"}}>
    {asset.type==="video" ?
      <OffthreadVideo
        src={"file://"+asset.path}
        muted
        style={{width:"100%",height:"100%",objectFit:"cover",transform:`scale(${scale})`}}
      /> :
      <Img
        src={"file://"+asset.path}
        style={{width:"100%",height:"100%",objectFit:"cover",transform:`translateX(${x}px) scale(${scale})`}}
      />
    }
    <AbsoluteFill style={{background:"linear-gradient(180deg,rgba(0,0,0,.05) 0%,rgba(0,0,0,.02) 52%,rgba(0,0,0,.62) 100%)"}}/>
  </AbsoluteFill>;
};

const Hook:React.FC<{title:string}>=({title})=>{
  const frame=useCurrentFrame();
  if(frame>58) return null;
  const y=interpolate(frame,[0,10],[55,0],{extrapolateRight:"clamp"});
  const opacity=interpolate(frame,[0,7,48,58],[0,1,1,0],{extrapolateRight:"clamp"});
  return <div style={{
    position:"absolute",top:190,left:70,right:70,
    color:"white",fontFamily:"Arial, sans-serif",fontWeight:950,
    fontSize:76,lineHeight:0.98,letterSpacing:-3,textTransform:"uppercase",
    textAlign:"center",opacity,transform:`translateY(${y}px)`,
    textShadow:"0 7px 28px rgba(0,0,0,.88)"
  }}>{title}</div>;
};

const DynamicCaptions:React.FC<{script:string}>=({script})=>{
  const frame=useCurrentFrame();
  const {durationInFrames}=useVideoConfig();
  const words=script.trim().split(/\s+/).filter(Boolean);
  const chunks:string[][]=[];
  for(let i=0;i<words.length;i+=5) chunks.push(words.slice(i,i+5));
  if(chunks.length===0) return null;
  const per=Math.max(1,durationInFrames/chunks.length);
  const idx=Math.min(chunks.length-1,Math.floor(frame/per));
  const chunk=chunks[idx];
  const local=(frame-idx*per)/per;
  const pop=interpolate(local,[0,.18],[.92,1],{extrapolateRight:"clamp"});
  return <div style={{
    position:"absolute",bottom:185,left:72,right:72,textAlign:"center",
    fontFamily:"Arial, sans-serif",fontWeight:950,fontSize:62,lineHeight:1.02,
    letterSpacing:-1.7,color:"white",transform:`scale(${pop})`,
    textShadow:"0 5px 22px #000,0 1px 3px #000",
  }}>
    {chunk.map((w,i)=><span key={i} style={{
      display:"inline-block",marginRight:15,
      color:i===chunk.length-1?"#FFD84A":"white"
    }}>{w}</span>)}
  </div>;
};

const Progress:React.FC=()=>{
  const frame=useCurrentFrame();
  const {durationInFrames}=useVideoConfig();
  const pct=Math.min(100,(frame/Math.max(1,durationInFrames))*100);
  return <div style={{position:"absolute",bottom:0,left:0,right:0,height:10,background:"rgba(255,255,255,.13)"}}>
    <div style={{width:`${pct}%`,height:"100%",background:"white"}}/>
  </div>;
};

export const ViralShort:React.FC<Props>=({voice,assets,title,script})=>{
  let start=0;
  return <AbsoluteFill style={{backgroundColor:"#05070d"}}>
    {assets.map((a,i)=>{
      const frames=Math.max(24,Math.round(a.duration*30));
      const s=start; start+=frames;
      return <Sequence key={i} from={s} durationInFrames={frames} premountFor={15}>
        <Shot asset={a} index={i}/>
      </Sequence>;
    })}
    <Audio src={"file://"+voice}/>
    <Hook title={title}/>
    <DynamicCaptions script={script}/>
    <Progress/>
  </AbsoluteFill>;
};
