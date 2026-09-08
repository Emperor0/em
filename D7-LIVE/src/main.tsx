import React, {useEffect, useState} from 'react';
import { createRoot } from 'react-dom/client';
import { invoke } from '@tauri-apps/api/core';
import './styles.css';

type Health = {app_version:string, mode:string, tiktok:string, virtual_camera:string, virtual_audio:string};

type EventRow = {time:string, kind:string, detail:string};

function App(){
  const [health,setHealth]=useState<Health|null>(null);
  const [events,setEvents]=useState<EventRow[]>([
    {time:'--:--',kind:'SYSTEM',detail:'D7 LIVE ready'},
  ]);
  const [ticker,setTicker]=useState('لا تنسى الفولو • لا تنسى ذكر الله • رابط الدعم في البايو');
  const [activeScene,setActiveScene]=useState('TikTok Gaming');
  const [updateMsg,setUpdateMsg]=useState('');

  useEffect(()=>{ invoke<Health>('health_snapshot').then(setHealth).catch(()=>setHealth({app_version:'1.0.0',mode:'UI Preview',tiktok:'Disconnected',virtual_camera:'Unavailable',virtual_audio:'Unavailable'})); },[]);

  async function testEvent(kind:string){
    try { await invoke('inject_test_event',{kind}); } catch {}
    const now=new Date().toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});
    setEvents(v=>[{time:now,kind,detail:`Test ${kind} event`},...v].slice(0,12));
  }

  async function checkUpdates(){
    setUpdateMsg('Checking…');
    try { const msg=await invoke<string>('check_for_updates'); setUpdateMsg(msg); }
    catch { setUpdateMsg('Update service not configured yet'); }
  }

  return <div className="app">
    <header className="topbar">
      <div className="brand"><span className="mark">D7</span><b>D7 LIVE</b><small>by d7kt</small></div>
      <div className="controls">
        <button className="ghost">Record</button><button className="ghost">Virtual Camera</button><button className="ghost">Bridge Mode</button><button className="primary">Go Live</button>
      </div>
    </header>

    <main className="workspace">
      <aside className="panel left">
        <h3>Scenes</h3>
        {['TikTok Gaming','Just Chatting','BRB / Panic','Horror','COD'].map(s=><button key={s} className={activeScene===s?'scene active':'scene'} onClick={()=>setActiveScene(s)}>{s}</button>)}
        <div className="section"><h3>Test Events</h3>
          {['FOLLOW','1000_LIKES','GIFT','SUBSCRIPTION','COMMENT','SHARE'].map(k=><button className="mini" key={k} onClick={()=>testEvent(k)}>{k.replace('_',' ')}</button>)}
        </div>
      </aside>

      <section className="center">
        <div className="preview">
          <div className="previewBadge">LIVE PREVIEW · 1080×1920 · 60 FPS</div>
          <div className="mockGame"><div className="d7logo">D<span>7</span>KT</div><div className="sub">SURVIVE THE NIGHT</div></div>
          <div className="ticker"><div>{ticker} • {ticker} • {ticker}</div></div>
        </div>
        <div className="statusbar">
          <span>FPS 60</span><span>CPU --</span><span>GPU --</span><span>VRAM --</span><span>RAM --</span><span>Dropped 0</span><span>Encoder NVENC</span>
        </div>
      </section>

      <aside className="panel right">
        <h3>Sources</h3>
        {['Game Capture','Camera','D7 Ticker','Alerts','Microphone','Desktop Audio'].map(x=><div className="source" key={x}><span>◉</span>{x}<span className="dots">•••</span></div>)}
        <div className="section"><h3>Connection</h3>
          <div className="kv"><span>TikTok</span><b>{health?.tiktok??'...'}</b></div>
          <div className="kv"><span>D7 Camera</span><b>{health?.virtual_camera??'...'}</b></div>
          <div className="kv"><span>D7 Audio</span><b>{health?.virtual_audio??'...'}</b></div>
        </div>
        <div className="section"><h3>Updates</h3><button className="wide" onClick={checkUpdates}>Check for Updates</button><p className="muted">{updateMsg}</p></div>
      </aside>
    </main>

    <section className="bottom">
      <div className="mixer">
        {['Mic','Game','Discord','Alerts'].map((x,i)=><div className="channel" key={x}><b>{x}</b><div className="meter"><i style={{height:`${42+i*11}%`}}/></div><input type="range" min="0" max="100" defaultValue={i===0?82:70}/></div>)}
      </div>
      <div className="events"><h3>Event History</h3>{events.map((e,i)=><div className="event" key={i}><time>{e.time}</time><b>{e.kind}</b><span>{e.detail}</span></div>)}</div>
    </section>
  </div>
}
createRoot(document.getElementById('root')!).render(<App/>);
