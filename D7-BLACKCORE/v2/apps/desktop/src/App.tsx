import { useCallback, useEffect, useMemo, useState } from "react";
import { getOverview, runFastPathMission, setPaused } from "./lib/tauri";
import type { CapabilitySnapshot, MissionResult, RuntimeOverview } from "./lib/types";

const formatPct=(v?:number|null)=>v==null?"—":`${Math.round(v)}%`;
const formatTemp=(v?:number|null)=>v==null?"—":`${Math.round(v)}°`;

function CapabilityPill({item}:{item:CapabilitySnapshot}){
  return <div className={`capability capability-${item.state}`} title={item.evidence}>
    <span className="capability-dot"/><span>{item.label}</span><b>{item.state.toUpperCase()}</b>
  </div>;
}

function MissionPanel({mission}:{mission:MissionResult}){
  return <article className="glass capabilities-panel mission-proof">
    <div className="section-title"><div><span>MISSION PROOF</span><h3>{mission.verified?"تم التنفيذ والتحقق":"المهمة غير متحققة"}</h3></div><b>{mission.qualityScore}%</b></div>
    <div className="runtime-rows">
      {mission.steps.map((s,i)=><div key={i}><span>{s.title}</span><b className={s.status==="completed"?"ok":"muted"}>{s.status.toUpperCase()}</b></div>)}
      <div><span>المسار</span><b>{mission.targetPath??"—"}</b></div>
      {mission.evidence.map((e,i)=><div key={`e-${i}`}><span>{e.claim}</span><b className={e.verified?"ok":"muted"}>{e.verified?"VERIFIED":"FAILED"}</b></div>)}
    </div>
  </article>;
}

export default function App(){
  const [overview,setOverview]=useState<RuntimeOverview|null>(null);
  const [error,setError]=useState<string|null>(null);
  const [loading,setLoading]=useState(true);
  const [command,setCommand]=useState("");
  const [lastAction,setLastAction]=useState("يراقب الجهاز بصمت");
  const [mission,setMission]=useState<MissionResult|null>(null);
  const [missionBusy,setMissionBusy]=useState(false);

  const refresh=useCallback(async()=>{try{setOverview(await getOverview());setError(null);}catch(e){setError(e instanceof Error?e.message:String(e));}finally{setLoading(false);}},[]);
  useEffect(()=>{refresh();const t=window.setInterval(refresh,2500);return()=>window.clearInterval(t);},[refresh]);

  const device=overview?.device; const runtime=overview?.runtime;
  const cDisk=device?.disks.find(d=>d.mount.toUpperCase().startsWith("C:"))??device?.disks[0];
  const diskUsedPct=cDisk?((cDisk.totalGb-cDisk.freeGb)/cDisk.totalGb)*100:null;
  const readiness=useMemo(()=>{if(!device)return 0;let s=100;if(device.ramUsagePercent>85)s-=15;if(device.cpuUsagePercent>90)s-=10;if((device.gpu?.temperatureC??0)>85)s-=15;if(cDisk&&cDisk.freeGb<20)s-=10;return Math.max(0,s);},[device,cDisk]);

  const togglePause=async()=>{if(!runtime)return;const next=!runtime.paused;await setPaused(next);setLastAction(next?"تم إيقاف الأتمتة بأمان":"تم استئناف Runtime");await refresh();};

  const runCommand=async()=>{
    const value=command.trim(); if(!value||missionBusy)return;
    setError(null);
    if(/أنشئ|انشئ|create/i.test(value)&&/ملف|file/i.test(value)){
      setMissionBusy(true);setLastAction("ينفذ Fast Path محلي ويتحقق من النتيجة");
      try{const result=await runFastPathMission(value);setMission(result);setLastAction(result.verified?"اكتملت المهمة بدليل فعلي":"انتهت المهمة بدون تحقق");}
      catch(e){setError(e instanceof Error?e.message:String(e));setLastAction("المهمة توقفت عند بوابة الأمان/التحقق");}
      finally{setMissionBusy(false);await refresh();}
    }else if(/افحص|جهاز|حراره|حرارة|رام|gpu|cpu/i.test(value)){setLastAction("يفحص حالة الجهاز الآن");await refresh();}
    else if(/وقف|إيقاف|توقف/i.test(value)){await setPaused(true);setLastAction("تم تفعيل الإيقاف الآمن");await refresh();}
    else setLastAction("تم استلام الهدف — هذا النوع ينتظر Agent Router في الـmilestone التالي");
    setCommand("");
  };

  return <main className="shell">
    <div className="ambient ambient-a"/><div className="ambient ambient-b"/>
    <header className="topbar"><div className="brand"><div className="brand-orb"/><div><b>D7 BLACKCORE</b><small>PERSONAL AUTONOMOUS OS · 2.0 dev.5</small></div></div><div className="top-actions"><span className="status-chip native"><i/> NATIVE RUNTIME</span><span className="status-chip elite">ELITE</span><button className={`stop ${runtime?.paused?"paused":""}`} onClick={togglePause} disabled={!runtime}>{runtime?.paused?"استئناف":"إيقاف"}</button></div></header>

    <section className="hero glass"><div className="hero-copy"><span className="eyebrow">NO CLAIM WITHOUT PROOF</span><h1>وش تبي أسوي؟</h1><p>{device?`${device.hostname} · ${device.os} · Runtime ${runtime?.version}`:"جارٍ ربط Runtime الحقيقي..."}</p></div>
      <div className="command-row"><input value={command} onChange={e=>setCommand(e.target.value)} onKeyDown={e=>e.key==="Enter"&&runCommand()} placeholder="مثال: أنشئ مجلد على سطح المكتب باسم BLACKCORE_TEST، وداخله ملف result.txt واكتب فيه: D7 BLACKCORE WORKS"/><button onClick={runCommand} disabled={missionBusy}>{missionBusy?"ينفذ...":"ابدأ"}</button></div>
      <div className="quick-actions"><button onClick={()=>setCommand("افحص جهازي كامل")}>⚡ افحص الجهاز</button><button onClick={()=>setCommand("أنشئ مجلد على سطح المكتب باسم BLACKCORE_TEST، وداخله ملف result.txt واكتب فيه: D7 BLACKCORE WORKS")}>✅ اختبار التنفيذ الحقيقي</button><button onClick={()=>setCommand("ابنِ لي برنامج")}>🛠 ابنِ برنامج</button><button onClick={()=>setCommand("أبي أطلع دخل")}>💰 اصنع دخل</button><button className="autopilot">● Autopilot</button></div>
      <div className="metrics-strip"><div><span>CPU</span><b>{formatPct(device?.cpuUsagePercent)}</b></div><div><span>RAM</span><b>{formatPct(device?.ramUsagePercent)}</b></div><div><span>GPU</span><b>{formatPct(device?.gpu?.utilizationPercent)}</b></div><div><span>TEMP</span><b>{formatTemp(device?.gpu?.temperatureC)}</b></div><div><span>C:</span><b>{diskUsedPct==null?"—":`${Math.round(diskUsedPct)}% مستخدم`}</b></div></div>
    </section>

    {error&&<div className="error-banner">{error}</div>}
    <section className="cards-grid"><article className="glass card active-card"><span className="card-label">وش يسوي الآن؟</span><h2>{runtime?.paused?"متوقف بأمان":lastAction}</h2><p>{runtime?.paused?"لا توجد أتمتة تنفذ حتى الاستئناف.":"Runtime حي؛ الكتابة العامة مقفلة إلا عبر adapters مقيدة ومتحققة."}</p><div className="pulse-line"><span/></div></article><article className="glass card"><span className="card-label">الجهاز</span><h2>{loading?"يفحص...":`${readiness}% جاهز`}</h2><p>{device?`${device.cpuName} · RAM ${device.ramTotalGb.toFixed(1)} GB · ${device.gpu?.name??"GPU غير مقروء"}`:"بانتظار Runtime"}</p><span className="mini-tag">LIVE DEVICE TWIN</span></article><article className="glass card"><span className="card-label">الموافقات</span><h2>0</h2><p>الملف الموجود مسبقًا يُحجب ولا يُستبدل تلقائيًا.</p><span className="mini-tag">APPROVAL FIREWALL</span></article><article className="glass card"><span className="card-label">الدخل</span><h2>$0 مؤكد</h2><p>Revenue لا يُحتسب إلا بإثبات دفع.</p><span className="mini-tag">PAYMENT PROOF</span></article></section>

    {mission&&<section className="lower-grid mission-grid"><MissionPanel mission={mission}/><article className="glass runtime-panel"><div className="section-title"><div><span>EVIDENCE</span><h3>آخر دليل</h3></div></div><div className="runtime-rows">{mission.evidence.map((e,i)=><div key={i}><span>{e.kind}</span><b>{e.value}</b></div>)}</div></article></section>}

    <section className="lower-grid"><article className="glass capabilities-panel"><div className="section-title"><div><span>CAPABILITY TRUTH</span><h3>وش يقدر BLACKCORE يسوي الآن فعليًا؟</h3></div><button onClick={refresh}>تحديث</button></div><div className="capabilities-list">{overview?.capabilities.map(item=><CapabilityPill key={item.id} item={item}/>)}</div></article><article className="glass runtime-panel"><div className="section-title"><div><span>LIVE SYSTEM</span><h3>Runtime + D7 القديم</h3></div></div><div className="runtime-rows"><div><span>OpenCode</span><b className={device?.openCode.available?"ok":"muted"}>{device?.openCode.available?`ONLINE ${device.openCode.version??""}`:"OFFLINE"}</b></div><div><span>D7 Governor</span><b className={device?.governor.available?"ok":"muted"}>{device?.governor.available?device.governor.state??"CONNECTED":"NOT FOUND"}</b></div><div><span>Processes</span><b>{device?.processCount??"—"}</b></div><div><span>نشط</span><b>{device?.runningApps.join(" · ")||"—"}</b></div></div></article></section>
    <footer>BLACKCORE 2.0 dev.5 · Native Mission Fast Path · NO CLAIM WITHOUT PROOF</footer>
  </main>;
}
