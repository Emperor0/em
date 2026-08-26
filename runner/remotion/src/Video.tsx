import React from "react";
import {
  AbsoluteFill,
  Audio,
  Img,
  OffthreadVideo,
  Sequence,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";

type Asset={
  type:"video"|"image"|"motion";
  path?:string;
  duration:number;
  caption?:string;
  voice?:string;
  motion?:"push_in"|"pan_left"|"pan_right"|"punch_zoom"|"parallax"|"none";
  transition?:"cut"|"whip"|"flash"|"zoom";
  visual_prompt?:string;
};
type Props={voice:string;assets:Asset[];title:string;script:string};

const MotionGraphic:React.FC<{index:number;caption:string}>=({index,caption})=>{
  const frame=useCurrentFrame();
  const {fps}=useVideoConfig();
  const enter=spring({frame,fps,config:{damping:16,stiffness:155,mass:.75}});
  const rotate=interpolate(frame,[0,80],[-8,12]);
  const slide=interpolate(frame,[0,80],[90,-80]);
  const pulse=1+Math.sin(frame/5)*.035;
  return <AbsoluteFill style={{
    overflow:"hidden",
    background:"radial-gradient(circle at 30% 20%, #1d3d6f 0%, #090d1b 42%, #03050a 100%)"
  }}>
    <div style={{position:"absolute",width:950,height:950,borderRadius:9999,left:-330,top:120,
      border:"3px solid rgba(105,168,255,.34)",transform:`translateX(${slide*.25}px) rotate(${rotate}deg) scale(${pulse})`}}/>
    <div style={{position:"absolute",width:720,height:720,borderRadius:120,right:-180,bottom:150,
      background:"linear-gradient(135deg,rgba(73,109,255,.22),rgba(0,220,255,.04))",
      border:"2px solid rgba(255,255,255,.14)",transform:`rotate(${-rotate*.55}deg) translateY(${slide*.18}px)`}}/>
    {[0,1,2,3].map((n)=><div key={n} style={{
      position:"absolute",left:120+n*95,top:640+n*95,width:430,height:105,borderRadius:24,
      background:"rgba(9,15,30,.78)",border:"1px solid rgba(145,188,255,.28)",
      boxShadow:"0 20px 70px rgba(0,0,0,.35)",
      transform:`translateX(${(1-enter)*(n%2===0?-380:380)}px) scale(${.96+enter*.04})`,
      opacity:enter
    }}/>) }
    <div style={{position:"absolute",left:90,right:90,top:400,
      fontFamily:"Arial, sans-serif",fontWeight:950,fontSize:88,lineHeight:.94,letterSpacing:-3,
      color:"white",textTransform:"uppercase",textAlign:"center",
      textShadow:"0 10px 40px rgba(0,0,0,.65)",transform:`scale(${.9+enter*.1})`,opacity:enter}}>
      {caption}
    </div>
  </AbsoluteFill>;
};

const KineticCaption:React.FC<{text:string;durationFrames:number}>=({text,durationFrames})=>{
  const frame=useCurrentFrame();
  const words=(text||"").trim().split(/\s+/).filter(Boolean).slice(0,8);
  if(words.length===0) return null;
  const current=Math.min(words.length-1,Math.floor((frame/Math.max(1,durationFrames))*words.length));
  const pop=interpolate(frame%8,[0,4,8],[.94,1.035,1],{extrapolateLeft:"clamp",extrapolateRight:"clamp"});
  return <div style={{
    position:"absolute",bottom:205,left:68,right:68,textAlign:"center",
    fontFamily:"Arial, sans-serif",fontWeight:950,fontSize:66,lineHeight:1.02,
    letterSpacing:-2,color:"white",transform:`scale(${pop})`,
    textShadow:"0 6px 24px rgba(0,0,0,.98),0 2px 5px #000"
  }}>
    {words.map((w,i)=><span key={i} style={{
      display:"inline-block",marginRight:14,
      color:i===current?"#FFE45A":"white",
      transform:i===current?"translateY(-3px) scale(1.06)":"none"
    }}>{w}</span>)}
  </div>;
};

const Shot:React.FC<{asset:Asset;index:number}>=({asset,index})=>{
  const frame=useCurrentFrame();
  const frames=Math.max(21,Math.round(asset.duration*30));
  const transition=asset.transition||"cut";
  const fadeFrames=transition==="cut"?2:5;
  const opacity=interpolate(frame,[0,fadeFrames,frames-fadeFrames,frames],[0,1,1,0],{
    extrapolateLeft:"clamp",extrapolateRight:"clamp"
  });
  const baseScale=asset.type==="image"?1.10:1.025;
  let scale=interpolate(frame,[0,frames],[baseScale,baseScale+.055],{extrapolateRight:"clamp"});
  let x=0;
  let y=0;
  if(asset.motion==="pan_left") x=interpolate(frame,[0,frames],[35,-35]);
  if(asset.motion==="pan_right") x=interpolate(frame,[0,frames],[-35,35]);
  if(asset.motion==="parallax") {x=interpolate(frame,[0,frames],[-20,20]);y=interpolate(frame,[0,frames],[15,-15]);}
  if(asset.motion==="punch_zoom") scale=interpolate(frame,[0,Math.min(8,frames),frames],[1.02,1.11,1.07],{extrapolateRight:"clamp"});
  if(asset.motion==="none") scale=1.035;
  if(transition==="whip" && frame<6) x+=interpolate(frame,[0,6],[index%2===0?170:-170,0]);
  if(transition==="zoom" && frame<7) scale+=interpolate(frame,[0,7],[.12,0]);
  const flash=transition==="flash"?interpolate(frame,[0,2,6],[.85,.20,0],{extrapolateRight:"clamp"}):0;
  const caption=asset.caption||asset.voice||"";

  return <AbsoluteFill style={{opacity,overflow:"hidden",backgroundColor:"#04060c"}}>
    {asset.type==="motion" ?
      <MotionGraphic index={index} caption={caption}/> : asset.type==="video" ?
      <OffthreadVideo
        src={asset.path||""}
        muted
        style={{width:"100%",height:"100%",objectFit:"cover",transform:`translate(${x}px,${y}px) scale(${scale})`}}
      /> :
      <Img
        src={asset.path||""}
        style={{width:"100%",height:"100%",objectFit:"cover",transform:`translate(${x}px,${y}px) scale(${scale})`}}
      />
    }
    <AbsoluteFill style={{background:"linear-gradient(180deg,rgba(0,0,0,.04) 0%,rgba(0,0,0,.02) 52%,rgba(0,0,0,.64) 100%)"}}/>
    {flash>0&&<AbsoluteFill style={{background:"white",opacity:flash}}/>}
    <KineticCaption text={caption} durationFrames={frames}/>
  </AbsoluteFill>;
};

const Hook:React.FC<{title:string}>=({title})=>{
  const frame=useCurrentFrame();
  if(frame>45) return null;
  const y=interpolate(frame,[0,8],[70,0],{extrapolateRight:"clamp"});
  const opacity=interpolate(frame,[0,5,34,45],[0,1,1,0],{extrapolateRight:"clamp"});
  const scale=interpolate(frame,[0,8],[.88,1],{extrapolateRight:"clamp"});
  return <div style={{
    position:"absolute",top:175,left:64,right:64,
    color:"white",fontFamily:"Arial, sans-serif",fontWeight:950,
    fontSize:78,lineHeight:.95,letterSpacing:-3.2,textTransform:"uppercase",
    textAlign:"center",opacity,transform:`translateY(${y}px) scale(${scale})`,
    textShadow:"0 8px 32px rgba(0,0,0,.92)"
  }}>{title}</div>;
};

const Progress:React.FC=()=>{
  const frame=useCurrentFrame();
  const {durationInFrames}=useVideoConfig();
  const pct=Math.min(100,(frame/Math.max(1,durationInFrames))*100);
  return <div style={{position:"absolute",bottom:0,left:0,right:0,height:8,background:"rgba(255,255,255,.10)"}}>
    <div style={{width:`${pct}%`,height:"100%",background:"white"}}/>
  </div>;
};

export const ViralShort:React.FC<Props>=({voice,assets,title})=>{
  let start=0;
  return <AbsoluteFill style={{backgroundColor:"#04060c"}}>
    {assets.map((a,i)=>{
      const frames=Math.max(21,Math.round(a.duration*30));
      const s=start; start+=frames;
      return <Sequence key={i} from={s} durationInFrames={frames} premountFor={20}>
        <Shot asset={a} index={i}/>
      </Sequence>;
    })}
    <Audio src={voice}/>
    <Hook title={title}/>
    <Progress/>
  </AbsoluteFill>;
};
