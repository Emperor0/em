import React, {useEffect, useMemo, useState} from 'react';
import {createRoot} from 'react-dom/client';
import {invoke} from '@tauri-apps/api/core';
import SourcePanel from './SourcePanel';
import './styles.css';

type ConnectorState='disconnected'|'connecting'|'connected'|'reconnecting'|'error';
type DeviceState='installed'|'running'|'unavailable'|'repair_required';
type Health={app_version:string,mode:string,tiktok:ConnectorState,virtual_camera:DeviceState,virtual_audio:DeviceState};
type Performance={cpu_percent:number,gpu_percent:number|null,vram_used_mb:number|null,ram_used_mb:number,ram_total_mb:number,render_fps:number,dropped_frames:number,frame_time_ms:number};
type MediaStatus={installed:boolean,process_running:boolean,connected:boolean,engine_version:string,obs_version:string|null,websocket_version:string|null,recording:boolean,streaming:boolean,replay_buffer:boolean,virtual_camera:boolean,current_scene:string|null,fps:number,obs_cpu_percent:number,obs_memory_mb:number,frame_time_ms:number,render_skipped_frames:number,output_skipped_frames:number,last_error:string|null};
type LiveEvent={id:string,provider:string,event_type:string,username:string,display_name:string,timestamp:string,data:Record<string,unknown>};
type AlertJob={id:string,priority:number,template:string,text:string,username:string,duration_ms:number};
type UpdateCheck={current_version:string,latest_version:string,available:boolean,notes:string,package_url:string};
type Scene={id:string,name:string,sources:unknown[]};
type AppConfig={general:{language:string,theme:string,creator_name:string,start_minimized:boolean},profile:{name:string,width:number,height:number,fps:number,encoder:string,bitrate_kbps:number,audio_bitrate_kbps:number,orientation:string},scenes:Scene[],active_scene_id:string|null,ticker:{messages:string[],speed_px_sec:number,direction:string,font_family:string,font_size:number,weight:number,opacity:number,separator:string,background:string,foreground:string,glow:boolean},rules:unknown[],updates:{check_on_startup:boolean,cadence:string,install_after_stream:boolean,endpoint:string|null}};

const fallbackConfig:AppConfig={general:{language:'ar',theme:'D7 Horror',creator_name:'d7kt',start_minimized:false},profile:{name:'TikTok Gaming',width:1080,height:1920,fps:60,encoder:'NVENC H.264',bitrate_kbps:6000,audio_bitrate_kbps:160,orientation:'vertical'},scenes:[{id:'fallback-gaming',name:'TikTok Gaming',sources:[]},{id:'fallback-chat',name:'Just Chatting',sources:[]},{id:'fallback-brb',name:'BRB / Panic',sources:[]},{id:'fallback-horror',name:'Horror',sources:[]},{id:'fallback-cod',name:'COD',sources:[]}],active_scene_id:'fallback-gaming',ticker:{messages:['لا تنسى الفولو','لا تنسى ذكر الله','رابط الدعم في البايو'],speed_px_sec:110,direction:'rtl',font_family:'Segoe UI',font_size:32,weight:700,opacity:1,separator:'•',background:'#A7191E',foreground:'#FFFFFF',glow:false},rules:[],updates:{check_on_startup:true,cadence:'daily',install_after_stream:false,endpoint:null}};

const label=(v:string)=>v.replaceAll('_',' ').replace(/\b\w/g,m=>m.toUpperCase());
const pct=(n:number|null|undefined)=>n==null?'--':`${Math.round(n)}%`;
const errorText=(e:unknown)=>e instanceof Error?e.message:String(e);

function App(){
  const [health,setHealth]=useState<Health|null>(null);
  const [perf,setPerf]=useState<Performance|null>(null);
  const [media,setMedia]=useState<MediaStatus|null>(null);
  const [mediaScenes,setMediaScenes]=useState<string[]>([]);
  const [events,setEvents]=useState<LiveEvent[]>([]);
  const [alerts,setAlerts]=useState<AlertJob[]>([]);
  const [config,setConfig]=useState<AppConfig>(fallbackConfig);
  const [update,setUpdate]=useState<UpdateCheck|null>(null);
  const [updateMsg,setUpdateMsg]=useState('');
  const [systemMsg,setSystemMsg]=useState('Starting D7 media engine…');
  const [busy,setBusy]=useState('');
  const [rtmpServer,setRtmpServer]=useState('');
  const [rtmpKey,setRtmpKey]=useState('');

  const activeScene=useMemo(()=>config.scenes.find(s=>s.id===config.active_scene_id)??config.scenes[0],[config]);
  const tickerText=useMemo(()=>config.ticker.messages.join(` ${config.ticker.separator} `),[config.ticker]);
  const scenes=mediaScenes.length?mediaScenes:config.scenes.map(s=>s.name);
  const currentScene=media?.current_scene??activeScene?.name??'TikTok Gaming';

  async function refreshBase(){await Promise.allSettled([invoke<Health>('health_snapshot').then(setHealth),invoke<Performance>('performance_snapshot').then(setPerf),invoke<LiveEvent[]>('event_history').then(setEvents),invoke<AlertJob[]>('alert_queue').then(setAlerts),invoke<AppConfig>('get_config').then(setConfig)])}
  async function refreshMedia(){try{const status=await invoke<MediaStatus>('media_status');setMedia(status);if(status.connected)setMediaScenes(await invoke<string[]>('media_scenes'))}catch(e){setSystemMsg(`Media status: ${errorText(e)}`)}}
  async function launchMedia(){setBusy('media-launch');setSystemMsg('Starting verified OBS media engine…');try{const status=await invoke<MediaStatus>('media_launch');setMedia(status);setMediaScenes(await invoke<string[]>('media_scenes'));setSystemMsg(`Media engine ready · OBS ${status.obs_version??'connected'}`);return true}catch(e){setSystemMsg(`Media engine failed: ${errorText(e)}`);return false}finally{setBusy('')}}

  useEffect(()=>{refreshBase();launchMedia();const id=window.setInterval(()=>{invoke<Performance>('performance_snapshot').then(setPerf).catch(()=>{});invoke<Health>('health_snapshot').then(setHealth).catch(()=>{});invoke<MediaStatus>('media_status').then(setMedia).catch(()=>{})},1500);return()=>window.clearInterval(id)},[]);

  async function testEvent(kind:string){setBusy(kind);try{await invoke('inject_test_event',{kind});const [history,queue]=await Promise.all([invoke<LiveEvent[]>('event_history'),invoke<AlertJob[]>('alert_queue')]);setEvents(history);setAlerts(queue)}catch(e){setSystemMsg(errorText(e))}finally{setBusy('')}}
  async function connect(){setBusy('connect');try{await invoke('connect_mock_tiktok');await refreshBase()}catch(e){setSystemMsg(errorText(e))}finally{setBusy('')}}
  async function disconnect(){setBusy('disconnect');try{await invoke('disconnect_mock_tiktok');await refreshBase()}catch(e){setSystemMsg(errorText(e))}finally{setBusy('')}}

  async function selectScene(name:string){setBusy('scene');try{if(!media?.connected&&!await launchMedia())return;await invoke('media_set_scene',{name});const matched=config.scenes.find(s=>s.name===name);if(matched){const next={...config,active_scene_id:matched.id};setConfig(next);await invoke('save_config',{config:next})}await refreshMedia()}catch(e){setSystemMsg(`Scene: ${errorText(e)}`)}finally{setBusy('')}}
  async function addScene(){const name=window.prompt('New scene name')?.trim();if(!name)return;try{if(!media?.connected&&!await launchMedia())return;await invoke('media_create_scene',{name});await refreshMedia();setSystemMsg(`Scene created: ${name}`)}catch(e){setSystemMsg(errorText(e))}}

  async function toggleRecording(){setBusy('record');try{if(!media?.connected&&!await launchMedia())return;if(media?.recording){const path=await invoke<string>('media_record_stop');setSystemMsg(`Recording saved: ${path}`)}else{await invoke('media_record_start');setSystemMsg('Recording started')}await refreshMedia()}catch(e){setSystemMsg(`Recording: ${errorText(e)}`)}finally{setBusy('')}}
  async function replayAction(){setBusy('replay');try{if(!media?.connected&&!await launchMedia())return;if(media?.replay_buffer){const path=await invoke<string|null>('media_replay_save');setSystemMsg(path?`Replay saved: ${path}`:'Replay save requested')}else{await invoke('media_replay_start');setSystemMsg('Replay Buffer started')}await refreshMedia()}catch(e){setSystemMsg(`Replay: ${errorText(e)}`)}finally{setBusy('')}}
  async function stopReplay(){try{await invoke('media_replay_stop');await refreshMedia();setSystemMsg('Replay Buffer stopped')}catch(e){setSystemMsg(errorText(e))}}
  async function toggleVirtualCamera(){setBusy('vcam');try{if(!media?.connected&&!await launchMedia())return;if(media?.virtual_camera){await invoke('media_virtual_camera_stop');setSystemMsg('Virtual Camera stopped')}else{await invoke('media_virtual_camera_start');setSystemMsg('Virtual Camera running — select OBS Virtual Camera in TikTok LIVE Studio')}await refreshMedia()}catch(e){setSystemMsg(`Virtual Camera: ${errorText(e)}`)}finally{setBusy('')}}
  async function installVirtualCamera(){setBusy('vcam-install');try{if(!media?.connected&&!await launchMedia())return;await invoke('media_install_virtual_camera');setSystemMsg('Virtual Camera component installed.')}catch(e){setSystemMsg(`Virtual Camera install: ${errorText(e)}`)}finally{setBusy('')}}
  async function saveRtmp(){if(!rtmpServer.trim()||!rtmpKey.trim()){setSystemMsg('Enter both RTMP/RTMPS server and stream key.');return}setBusy('rtmp');try{if(!media?.connected&&!await launchMedia())return;await invoke('media_set_rtmp',{server:rtmpServer.trim(),key:rtmpKey});setRtmpKey('');setSystemMsg('RTMP destination configured in the local media engine.')}catch(e){setSystemMsg(`RTMP: ${errorText(e)}`)}finally{setBusy('')}}
  async function toggleStream(){setBusy('stream');try{if(!media?.connected&&!await launchMedia())return;if(media?.streaming){await invoke('media_stream_stop');setSystemMsg('Stream stopped')}else{await invoke('media_stream_start');setSystemMsg('LIVE output started')}await refreshMedia()}catch(e){setSystemMsg(`Streaming: ${errorText(e)}`)}finally{setBusy('')}}
  async function checkUpdates(){setUpdateMsg('Checking secure update channel…');setUpdate(null);try{const result=await invoke<UpdateCheck>('check_for_updates');setUpdate(result);setUpdateMsg(result.available?`D7 LIVE ${result.latest_version} is available`:`Latest version installed (${result.current_version})`)}catch(e){setUpdateMsg(errorText(e))}}

  const isArabic=config.general.language==='ar';
  async function setLanguage(lang:'ar'|'en'){const next={...config,general:{...config.general,language:lang}};setConfig(next);document.documentElement.dir=lang==='ar'?'rtl':'ltr';try{await invoke('save_config',{config:next})}catch{}}

  return <div className="app" dir="ltr">
    <header className="topbar">
      <div className="brand"><span className="mark">D7</span><div><b>D7 LIVE</b><small>by d7kt · v{health?.app_version??'0.9.0-alpha.2'}</small></div></div>
      <div className="top-status"><span className={`pill ${media?.connected?'ok':''}`}>Media · {media?.connected?'Connected':'Offline'}</span><span className="pill">OBS {media?.obs_version??'--'} · {media?.fps?media.fps.toFixed(0):'--'} FPS</span><span className={`pill ${health?.tiktok==='connected'?'ok':''}`}>TikTok · {label(health?.tiktok??'disconnected')}</span></div>
      <div className="controls"><button className={media?.recording?'primary':'ghost'} disabled={busy==='record'} onClick={toggleRecording}>{media?.recording?'Stop Record':'Record'}</button><button className={media?.replay_buffer?'primary':'ghost'} disabled={busy==='replay'} onClick={replayAction}>{media?.replay_buffer?'Save Replay':'Start Replay'}</button><button className={media?.virtual_camera?'primary':'ghost'} disabled={busy==='vcam'} onClick={toggleVirtualCamera}>{media?.virtual_camera?'Stop Camera':'Virtual Camera'}</button><button className={media?.streaming?'danger primary':'primary'} disabled={busy==='stream'} onClick={toggleStream}>{media?.streaming?'STOP LIVE':'Go Live'}</button></div>
    </header>

    <main className="workspace">
      <aside className="panel left">
        <div className="panelTitle"><h3>Scenes</h3><button className="iconBtn" onClick={addScene}>＋</button></div>{scenes.map(name=><button key={name} disabled={busy==='scene'} className={currentScene===name?'scene active':'scene'} onClick={()=>selectScene(name)}><span className="sceneDot"/>{name}</button>)}
        <div className="section"><div className="panelTitle"><h3>Test Events</h3><span className="tiny">OFFLINE SAFE</span></div><div className="testGrid">{['FOLLOW','1000_LIKES','GIFT','SUBSCRIPTION','COMMENT','SHARE'].map(k=><button disabled={busy===k} className="mini" key={k} onClick={()=>testEvent(k)}>{busy===k?'…':k.replaceAll('_',' ')}</button>)}</div></div>
        <div className="section"><h3>TikTok Events</h3><div className="connectorCard"><div><span className={`statusDot ${health?.tiktok==='connected'?'online':''}`}/><b>{label(health?.tiktok??'disconnected')}</b></div><small>Offline test provider is available now. Production LIVE events remain isolated behind the approved connector boundary.</small>{health?.tiktok==='connected'?<button className="wide" onClick={disconnect}>Disconnect Test Provider</button>:<button className="wide red" onClick={connect}>Connect Test Provider</button>}</div></div>
      </aside>

      <section className="center">
        <div className="canvasHeader"><div><b>{currentScene}</b><small>PROGRAM · D7 MEDIA ENGINE</small></div><div className="canvasTools"><button>Fit</button><button>100%</button><button>Guides</button></div></div>
        <div className="canvasStage"><div className="preview"><div className="previewBadge">D7 LIVE · {config.profile.width}×{config.profile.height} · {config.profile.fps} FPS</div><div className="mockGame"><div className="noise"/><div className="liveTag">{media?.streaming?'LIVE':'PREVIEW'}</div><div className="mockAlert">{alerts[0]?<><small>{alerts[0].template.toUpperCase()}</small><strong>{alerts[0].text}</strong></>:<><small>D7 ALERT ENGINE</small><strong>{media?.connected?'READY':'STARTING'}</strong></>}</div><div className="d7logo">D<span>7</span>KT</div><div className="sub">SURVIVE THE NIGHT</div><div className="goal"><span>LIKE GOAL</span><b>0 / 1,000</b><i><em/></i></div></div><div className="ticker" style={{background:config.ticker.background,color:config.ticker.foreground}}><div>{tickerText}　{config.ticker.separator}　{tickerText}　{config.ticker.separator}　{tickerText}</div></div></div></div>
        <div className="statusbar"><span><b>OBS FPS</b> {media?.fps?media.fps.toFixed(0):'--'}</span><span><b>CPU</b> {pct(perf?.cpu_percent)}</span><span><b>OBS CPU</b> {media?`${media.obs_cpu_percent.toFixed(1)}%`:'--'}</span><span><b>OBS RAM</b> {media?`${media.obs_memory_mb.toFixed(0)} MB`:'--'}</span><span><b>Frame</b> {media?`${media.frame_time_ms.toFixed(2)} ms`:'--'}</span><span><b>Dropped</b> {(media?.render_skipped_frames??0)+(media?.output_skipped_frames??0)}</span></div>
      </section>

      <SourcePanel currentScene={currentScene} mediaConnected={!!media?.connected} ensureMedia={launchMedia} onMessage={setSystemMsg}/>
    </main>

    <section className="bottom">
      <div className="mixerArea"><div className="bottomHead"><h3>Audio</h3><span>Real OBS input mute/volume controls are in Sources</span></div><div className="connectorCard"><b>D7 Audio Engine</b><small>Add Microphone, Desktop Audio, Discord/application audio or Media sources from the Sources ＋ menu, then control mute and gain directly from their properties.</small><div className="kv"><span>Media process</span><b>{media?.connected?'Connected':'Offline'}</b></div><div className="kv"><span>Recording</span><b>{media?.recording?'Active':'Ready'}</b></div><div className="kv"><span>Replay Buffer</span><b>{media?.replay_buffer?'Active':'Stopped'}</b></div></div></div>
      <div className="events"><div className="bottomHead"><h3>Event History</h3><span>{events.length} events</span></div>{events.length===0?<div className="empty">No LIVE events yet. Use Test Events.</div>:events.slice(0,16).map(e=><div className="event" key={e.id}><time>{new Date(e.timestamp).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'})}</time><b>{e.event_type.replaceAll('_',' ').toUpperCase()}</b><span>{e.display_name}</span></div>)}</div>
      <div className="utility"><div className="bottomHead"><h3>System / Output</h3><span>D7 CORE</span></div><p className="updateMsg">{systemMsg}</p>{media?.last_error&&<p className="updateMsg">{media.last_error}</p>}<div className="tabs"><button className="active">Output</button><button onClick={refreshMedia}>Pre-flight</button><button onClick={checkUpdates}>Update</button></div><label className="rtmpLabel">RTMP / RTMPS server<input type="text" value={rtmpServer} onChange={e=>setRtmpServer(e.target.value)} placeholder="rtmps://..." autoComplete="off"/></label><label className="rtmpLabel">Stream key<input type="password" value={rtmpKey} onChange={e=>setRtmpKey(e.target.value)} placeholder="Not stored in D7 config" autoComplete="new-password"/></label><button className="wide red" disabled={busy==='rtmp'} onClick={saveRtmp}>Set Direct Stream Destination</button>{media?.replay_buffer&&<button className="wide" onClick={stopReplay}>Stop Replay Buffer</button>}<button className="wide" disabled={busy==='vcam-install'} onClick={installVirtualCamera}>Install / Repair Virtual Camera</button><button className="wide" onClick={checkUpdates}>Check for Updates</button><p className="updateMsg">{updateMsg||'Signed update channel is separate from LIVE output.'}</p>{update?.available&&<div className="updateCard"><b>v{update.latest_version}</b><p>{update.notes}</p></div>}<div className="language"><span>Language</span><button className={isArabic?'active':''} onClick={()=>setLanguage('ar')}>العربية</button><button className={!isArabic?'active':''} onClick={()=>setLanguage('en')}>English</button></div></div>
    </section>
  </div>
}

createRoot(document.getElementById('root')!).render(<App/>);
