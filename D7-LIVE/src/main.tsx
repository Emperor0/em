import React, {useEffect, useMemo, useState} from 'react';
import { createRoot } from 'react-dom/client';
import { invoke } from '@tauri-apps/api/core';
import './styles.css';

type ConnectorState = 'disconnected'|'connecting'|'connected'|'reconnecting'|'error';
type DeviceState = 'installed'|'running'|'unavailable'|'repair_required';
type Health = {app_version:string, mode:string, tiktok:ConnectorState, virtual_camera:DeviceState, virtual_audio:DeviceState};
type Performance = {cpu_percent:number, gpu_percent:number|null, vram_used_mb:number|null, ram_used_mb:number, ram_total_mb:number, render_fps:number, dropped_frames:number, frame_time_ms:number};
type LiveEvent = {id:string, provider:string, event_type:string, username:string, display_name:string, timestamp:string, data:Record<string,unknown>};
type AlertJob = {id:string, priority:number, template:string, text:string, username:string, duration_ms:number};
type UpdateCheck = {current_version:string, latest_version:string, available:boolean, notes:string, package_url:string};
type Scene = {id:string, name:string, sources:unknown[]};
type AppConfig = {
  general:{language:string, theme:string, creator_name:string, start_minimized:boolean},
  profile:{name:string,width:number,height:number,fps:number,encoder:string,bitrate_kbps:number,audio_bitrate_kbps:number,orientation:string},
  scenes:Scene[],
  active_scene_id:string|null,
  ticker:{messages:string[],speed_px_sec:number,direction:string,font_family:string,font_size:number,weight:number,opacity:number,separator:string,background:string,foreground:string,glow:boolean},
  rules:unknown[],
  updates:{check_on_startup:boolean,cadence:string,install_after_stream:boolean,endpoint:string|null}
};

const fallbackConfig:AppConfig={
  general:{language:'ar',theme:'D7 Horror',creator_name:'d7kt',start_minimized:false},
  profile:{name:'TikTok Gaming',width:1080,height:1920,fps:60,encoder:'NVENC H.264',bitrate_kbps:6000,audio_bitrate_kbps:160,orientation:'vertical'},
  scenes:[{id:'fallback-gaming',name:'TikTok Gaming',sources:[]},{id:'fallback-chat',name:'Just Chatting',sources:[]},{id:'fallback-brb',name:'BRB / Panic',sources:[]},{id:'fallback-horror',name:'Horror',sources:[]},{id:'fallback-cod',name:'COD',sources:[]}],
  active_scene_id:'fallback-gaming',
  ticker:{messages:['لا تنسى الفولو','لا تنسى ذكر الله','رابط الدعم في البايو'],speed_px_sec:110,direction:'rtl',font_family:'Segoe UI',font_size:32,weight:700,opacity:1,separator:'•',background:'#A7191E',foreground:'#FFFFFF',glow:false},
  rules:[],
  updates:{check_on_startup:true,cadence:'daily',install_after_stream:false,endpoint:null}
};

function stateLabel(value:string){ return value.replaceAll('_',' ').replace(/\b\w/g,m=>m.toUpperCase()); }
function pct(n:number|null|undefined){return n==null?'--':`${Math.round(n)}%`}
function mb(n:number|null|undefined){return n==null?'--':`${Math.round(n)} MB`}

function App(){
  const [health,setHealth]=useState<Health|null>(null);
  const [perf,setPerf]=useState<Performance|null>(null);
  const [events,setEvents]=useState<LiveEvent[]>([]);
  const [alerts,setAlerts]=useState<AlertJob[]>([]);
  const [config,setConfig]=useState<AppConfig>(fallbackConfig);
  const [update,setUpdate]=useState<UpdateCheck|null>(null);
  const [updateMsg,setUpdateMsg]=useState('');
  const [busy,setBusy]=useState('');
  const [selectedSource,setSelectedSource]=useState('Game Capture');

  const activeScene=useMemo(()=>config.scenes.find(s=>s.id===config.active_scene_id)??config.scenes[0], [config]);
  const tickerText=useMemo(()=>config.ticker.messages.join(` ${config.ticker.separator} `),[config.ticker]);

  async function refresh(){
    const calls=[
      invoke<Health>('health_snapshot').then(setHealth),
      invoke<Performance>('performance_snapshot').then(setPerf),
      invoke<LiveEvent[]>('event_history').then(setEvents),
      invoke<AlertJob[]>('alert_queue').then(setAlerts),
      invoke<AppConfig>('get_config').then(setConfig)
    ];
    await Promise.allSettled(calls);
  }

  useEffect(()=>{
    refresh();
    const id=window.setInterval(()=>{
      invoke<Performance>('performance_snapshot').then(setPerf).catch(()=>{});
      invoke<Health>('health_snapshot').then(setHealth).catch(()=>{});
    },1800);
    return()=>window.clearInterval(id);
  },[]);

  async function testEvent(kind:string){
    setBusy(kind);
    try{
      await invoke('inject_test_event',{kind});
      const [history,queue]=await Promise.all([invoke<LiveEvent[]>('event_history'),invoke<AlertJob[]>('alert_queue')]);
      setEvents(history);setAlerts(queue);
    } finally {setBusy('')}
  }

  async function connect(){
    setBusy('connect');
    try{await invoke('connect_mock_tiktok');await refresh();}finally{setBusy('')}
  }

  async function disconnect(){
    setBusy('disconnect');
    try{await invoke('disconnect_mock_tiktok');await refresh();}finally{setBusy('')}
  }

  async function selectScene(id:string){
    const next={...config,active_scene_id:id};
    setConfig(next);
    try{await invoke('save_config',{config:next});}catch{}
  }

  async function checkUpdates(){
    setUpdateMsg('Checking secure update channel…');setUpdate(null);
    try{
      const result=await invoke<UpdateCheck>('check_for_updates');
      setUpdate(result);
      setUpdateMsg(result.available?`D7 LIVE ${result.latest_version} is available`:`You are on the latest version (${result.current_version})`);
    }catch(error){setUpdateMsg(String(error));}
  }

  const language=config.general.language;
  const isArabic=language==='ar';
  const setLanguage=async(lang:'ar'|'en')=>{
    const next={...config,general:{...config.general,language:lang}};
    setConfig(next);document.documentElement.dir=lang==='ar'?'rtl':'ltr';
    try{await invoke('save_config',{config:next});}catch{}
  };

  return <div className="app" dir="ltr">
    <header className="topbar">
      <div className="brand"><span className="mark">D7</span><div><b>D7 LIVE</b><small>by d7kt · v{health?.app_version??'0.9.0-alpha.1'}</small></div></div>
      <div className="top-status">
        <span className={`pill ${health?.tiktok==='connected'?'ok':''}`}>TikTok · {stateLabel(health?.tiktok??'disconnected')}</span>
        <span className="pill">NVENC · Planned</span>
        <span className="pill">{config.profile.width}×{config.profile.height} · {config.profile.fps} FPS</span>
      </div>
      <div className="controls">
        <button className="ghost">Record</button><button className="ghost">Replay</button><button className="ghost">Virtual Camera</button><button className="primary">Go Live</button>
      </div>
    </header>

    <main className="workspace">
      <aside className="panel left">
        <div className="panelTitle"><h3>Scenes</h3><button className="iconBtn">＋</button></div>
        {config.scenes.map(scene=><button key={scene.id} className={activeScene?.id===scene.id?'scene active':'scene'} onClick={()=>selectScene(scene.id)}><span className="sceneDot"/>{scene.name}</button>)}

        <div className="section"><div className="panelTitle"><h3>Test Events</h3><span className="tiny">OFFLINE SAFE</span></div>
          <div className="testGrid">{['FOLLOW','1000_LIKES','GIFT','SUBSCRIPTION','COMMENT','SHARE'].map(k=><button disabled={busy===k} className="mini" key={k} onClick={()=>testEvent(k)}>{busy===k?'…':k.replaceAll('_',' ')}</button>)}</div>
        </div>

        <div className="section"><h3>TikTok Connector</h3>
          <div className="connectorCard"><div><span className={`statusDot ${health?.tiktok==='connected'?'online':''}`}/><b>{stateLabel(health?.tiktok??'disconnected')}</b></div><small>Mock provider for offline alert development. Production provider is isolated.</small>
            {health?.tiktok==='connected'?<button className="wide" onClick={disconnect}>Disconnect</button>:<button className="wide red" onClick={connect}>Connect Test Provider</button>}
          </div>
        </div>
      </aside>

      <section className="center">
        <div className="canvasHeader"><div><b>{activeScene?.name??'No scene'}</b><small>PROGRAM</small></div><div className="canvasTools"><button>Fit</button><button>100%</button><button>Guides</button></div></div>
        <div className="canvasStage">
          <div className="preview">
            <div className="previewBadge">D7 LIVE · VERTICAL · {config.profile.fps} FPS</div>
            <div className="mockGame">
              <div className="noise"/>
              <div className="liveTag">LIVE</div>
              <div className="mockAlert">{alerts[0]?<><small>{alerts[0].template.toUpperCase()}</small><strong>{alerts[0].text}</strong></>:<><small>D7 ALERT ENGINE</small><strong>READY</strong></>}</div>
              <div className="d7logo">D<span>7</span>KT</div><div className="sub">SURVIVE THE NIGHT</div>
              <div className="goal"><span>LIKE GOAL</span><b>0 / 1,000</b><i><em/></i></div>
            </div>
            <div className="ticker" style={{background:config.ticker.background,color:config.ticker.foreground}}><div>{tickerText}　{config.ticker.separator}　{tickerText}　{config.ticker.separator}　{tickerText}</div></div>
          </div>
        </div>
        <div className="statusbar">
          <span><b>FPS</b> {perf?.render_fps?perf.render_fps.toFixed(0):'--'}</span><span><b>CPU</b> {pct(perf?.cpu_percent)}</span><span><b>GPU</b> {pct(perf?.gpu_percent)}</span><span><b>VRAM</b> {mb(perf?.vram_used_mb)}</span><span><b>RAM</b> {perf?`${Math.round(perf.ram_used_mb/1024*10)/10}/${Math.round(perf.ram_total_mb/1024*10)/10} GB`:'--'}</span><span><b>Dropped</b> {perf?.dropped_frames??0}</span>
        </div>
      </section>

      <aside className="panel right">
        <div className="panelTitle"><h3>Sources</h3><button className="iconBtn">＋</button></div>
        {['Game Capture','Camera','D7 Ticker','Alerts','Microphone','Desktop Audio'].map(x=><button className={selectedSource===x?'source selected':'source'} key={x} onClick={()=>setSelectedSource(x)}><span className="eye">◉</span><span>{x}</span><span className="dots">•••</span></button>)}
        <div className="section property"><h3>Properties · {selectedSource}</h3>
          <label>Opacity <input type="range" min="0" max="100" defaultValue="100"/></label>
          <div className="two"><label>X<input value="0" readOnly/></label><label>Y<input value="0" readOnly/></label></div>
          <div className="two"><label>Width<input value="1080" readOnly/></label><label>Height<input value="1920" readOnly/></label></div>
        </div>
        <div className="section"><h3>Virtual Output</h3>
          <div className="kv"><span>D7 LIVE Camera</span><b>{stateLabel(health?.virtual_camera??'unavailable')}</b></div>
          <div className="kv"><span>D7 LIVE Audio</span><b>{stateLabel(health?.virtual_audio??'unavailable')}</b></div>
        </div>
      </aside>
    </main>

    <section className="bottom">
      <div className="mixerArea">
        <div className="bottomHead"><h3>Audio Mixer</h3><span>Stream / Record / Monitor / Virtual</span></div>
        <div className="mixer">{['Mic','Game','Discord','Music','Alerts'].map((x,i)=><div className="channel" key={x}><div className="channelHead"><b>{x}</b><button>M</button></div><div className="meter"><i style={{height:`${35+i*9}%`}}/></div><input type="range" min="0" max="100" defaultValue={i===0?82:70}/><small>{i===0?'-4.2':'-8.0'} dB</small></div>)}</div>
      </div>
      <div className="events"><div className="bottomHead"><h3>Event History</h3><span>{events.length} events</span></div>{events.length===0?<div className="empty">No LIVE events yet. Use Test Events.</div>:events.slice(0,16).map((e)=><div className="event" key={e.id}><time>{new Date(e.timestamp).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'})}</time><b>{e.event_type.replaceAll('_',' ').toUpperCase()}</b><span>{e.display_name}</span></div>)}</div>
      <div className="utility">
        <div className="bottomHead"><h3>System</h3><span>D7 CORE</span></div>
        <div className="tabs"><button className="active">Update</button><button>Pre-flight</button><button>Profile</button></div>
        <button className="wide red" onClick={checkUpdates}>Check for Updates</button>
        <p className="updateMsg">{updateMsg||'Secure update channel ready for manifest publishing.'}</p>
        {update?.available&&<div className="updateCard"><b>v{update.latest_version}</b><p>{update.notes}</p><button>Download & Install</button></div>}
        <div className="language"><span>Language</span><button className={isArabic?'active':''} onClick={()=>setLanguage('ar')}>العربية</button><button className={!isArabic?'active':''} onClick={()=>setLanguage('en')}>English</button></div>
      </div>
    </section>
  </div>
}

createRoot(document.getElementById('root')!).render(<App/>);
