import React, {useEffect, useMemo, useState} from 'react';
import { invoke } from '@tauri-apps/api/core';

export type MediaInput = {
  name:string;
  uuid:string;
  kind:string;
  unversioned_kind:string;
  muted:boolean|null;
};

type Props = {
  currentScene:string;
  mediaConnected:boolean;
  ensureMedia:()=>Promise<boolean>;
  onMessage:(message:string)=>void;
};

type QuickSource = {
  label:string;
  aliases:string[];
  defaultName:string;
  settings:Record<string,unknown>;
};

const quickSources:QuickSource[] = [
  {label:'Game Capture',aliases:['game_capture'],defaultName:'Game Capture',settings:{}},
  {label:'Window Capture',aliases:['window_capture'],defaultName:'Window Capture',settings:{}},
  {label:'Display Capture',aliases:['monitor_capture','display_capture'],defaultName:'Display Capture',settings:{}},
  {label:'Camera',aliases:['dshow_input'],defaultName:'Camera',settings:{}},
  {label:'Microphone',aliases:['wasapi_input_capture'],defaultName:'Microphone',settings:{}},
  {label:'Desktop Audio',aliases:['wasapi_output_capture'],defaultName:'Desktop Audio',settings:{}},
  {label:'Browser',aliases:['browser_source'],defaultName:'Browser',settings:{url:'about:blank',width:1080,height:1920}},
  {label:'Image',aliases:['image_source'],defaultName:'Image',settings:{}},
  {label:'Media',aliases:['ffmpeg_source'],defaultName:'Media',settings:{local_file:''}},
  {label:'Text',aliases:['text_gdiplus_v3','text_gdiplus'],defaultName:'Text',settings:{text:'d7kt'}}
];

function readableKind(kind:string){
  return kind.replace(/_v\d+$/,'').replaceAll('_',' ').replace(/\b\w/g,c=>c.toUpperCase());
}

export default function SourcePanel({currentScene,mediaConnected,ensureMedia,onMessage}:Props){
  const [inputs,setInputs]=useState<MediaInput[]>([]);
  const [kinds,setKinds]=useState<string[]>([]);
  const [selected,setSelected]=useState<string>('');
  const [busy,setBusy]=useState('');
  const [showAdd,setShowAdd]=useState(false);
  const [customKind,setCustomKind]=useState('');
  const [customName,setCustomName]=useState('');
  const [settingsText,setSettingsText]=useState('{}');

  const selectedInput=useMemo(()=>inputs.find(i=>i.name===selected)??null,[inputs,selected]);

  async function refresh(){
    if(!mediaConnected) return;
    try{
      const [nextInputs,nextKinds]=await Promise.all([
        invoke<MediaInput[]>('media_inputs_list'),
        invoke<string[]>('media_input_kinds')
      ]);
      setInputs(nextInputs);setKinds(nextKinds);
      if(nextInputs.length && !nextInputs.some(x=>x.name===selected))setSelected(nextInputs[0].name);
    }catch(error){onMessage(`Sources: ${String(error)}`)}
  }

  useEffect(()=>{refresh()},[mediaConnected,currentScene]);

  function findKind(aliases:string[]){
    return aliases.map(a=>kinds.find(k=>k===a)||kinds.find(k=>k.includes(a))).find(Boolean)??null;
  }

  async function createQuick(item:QuickSource){
    setBusy(`add:${item.label}`);
    try{
      if(!mediaConnected && !await ensureMedia())return;
      const kind=findKind(item.aliases);
      if(!kind){throw new Error(`${item.label} is not available in this OBS runtime`)}
      let name=item.defaultName;
      let index=2;
      while(inputs.some(x=>x.name===name)){name=`${item.defaultName} ${index++}`}
      await invoke('media_input_create',{scene:currentScene,name,kind,settings:item.settings});
      await refresh();setSelected(name);setShowAdd(false);onMessage(`${item.label} added to ${currentScene}`);
    }catch(error){onMessage(`Add source: ${String(error)}`)}finally{setBusy('')}
  }

  async function createCustom(){
    const name=customName.trim();const kind=customKind.trim();
    if(!name||!kind){onMessage('Choose an OBS source type and enter a source name.');return}
    setBusy('custom');
    try{
      if(!mediaConnected && !await ensureMedia())return;
      let settings:Record<string,unknown>={};
      try{settings=JSON.parse(settingsText)}catch{throw new Error('Source settings JSON is invalid')}
      await invoke('media_input_create',{scene:currentScene,name,kind,settings});
      await refresh();setSelected(name);setShowAdd(false);onMessage(`Source created: ${name}`);
    }catch(error){onMessage(String(error))}finally{setBusy('')}
  }

  async function removeSelected(){
    if(!selectedInput)return;
    if(!window.confirm(`Remove source “${selectedInput.name}”?`))return;
    setBusy('remove');
    try{await invoke('media_input_remove',{name:selectedInput.name});setSelected('');await refresh();onMessage('Source removed.')}catch(error){onMessage(String(error))}finally{setBusy('')}
  }

  async function renameSelected(){
    if(!selectedInput)return;
    const next=window.prompt('Source name',selectedInput.name)?.trim();if(!next||next===selectedInput.name)return;
    try{await invoke('media_input_rename',{name:selectedInput.name,newName:next});setSelected(next);await refresh();onMessage(`Source renamed to ${next}`)}catch(error){onMessage(String(error))}
  }

  async function toggleMute(){
    if(!selectedInput)return;
    try{const muted=await invoke<boolean>('media_input_toggle_mute',{name:selectedInput.name});await refresh();onMessage(`${selectedInput.name}: ${muted?'muted':'unmuted'}`)}catch(error){onMessage(String(error))}
  }

  async function setVolume(db:number){
    if(!selectedInput)return;
    try{await invoke('media_input_set_volume_db',{name:selectedInput.name,db})}catch(error){onMessage(String(error))}
  }

  async function openSettings(){
    if(!selectedInput)return;
    try{
      const value=await invoke<Record<string,unknown>>('media_input_settings',{name:selectedInput.name});
      const next=window.prompt('OBS input settings JSON',JSON.stringify(value,null,2));
      if(next==null)return;
      const parsed=JSON.parse(next);
      await invoke('media_input_set_settings',{name:selectedInput.name,settings:parsed,overlay:false});
      onMessage(`${selectedInput.name} settings updated.`);
    }catch(error){onMessage(`Settings: ${String(error)}`)}
  }

  return <aside className="panel right">
    <div className="panelTitle"><h3>Sources</h3><div><button className="iconBtn" onClick={refresh}>↻</button><button className="iconBtn" onClick={()=>setShowAdd(v=>!v)}>＋</button></div></div>

    {showAdd&&<div className="section sourceAdd">
      <h3>Add Source · {currentScene}</h3>
      <div className="testGrid">{quickSources.map(q=><button key={q.label} className="mini" disabled={busy.startsWith('add:')} onClick={()=>createQuick(q)}>{q.label}</button>)}</div>
      <label>Advanced source type<select value={customKind} onChange={e=>setCustomKind(e.target.value)}><option value="">Select OBS input kind…</option>{kinds.map(k=><option key={k} value={k}>{readableKind(k)} · {k}</option>)}</select></label>
      <label>Name<input value={customName} onChange={e=>setCustomName(e.target.value)} placeholder="Source name"/></label>
      <label>Settings JSON<textarea rows={4} value={settingsText} onChange={e=>setSettingsText(e.target.value)}/></label>
      <button className="wide red" disabled={busy==='custom'} onClick={createCustom}>Create Source</button>
    </div>}

    {!mediaConnected&&<div className="empty">Start the D7 media engine to enumerate real capture/audio sources.</div>}
    {mediaConnected&&inputs.length===0&&<div className="empty">No sources yet. Press ＋ to add Game Capture, Camera, Audio, Browser, Image or Media.</div>}
    {inputs.map(input=><button className={selected===input.name?'source selected':'source'} key={input.uuid||input.name} onClick={()=>setSelected(input.name)}><span className="eye">{input.muted===true?'◌':'◉'}</span><span><b>{input.name}</b><small>{readableKind(input.unversioned_kind||input.kind)}</small></span><span className="dots">•••</span></button>)}

    {selectedInput&&<div className="section property"><h3>Properties · {selectedInput.name}</h3>
      <div className="kv"><span>OBS kind</span><b>{selectedInput.kind}</b></div>
      <div className="kv"><span>Audio</span><b>{selectedInput.muted==null?'N/A':selectedInput.muted?'Muted':'Live'}</b></div>
      {selectedInput.muted!=null&&<><label>Volume (dB)<input type="range" min="-60" max="10" step="1" defaultValue="0" onChange={e=>setVolume(Number(e.target.value))}/></label><button className="wide" onClick={toggleMute}>{selectedInput.muted?'Unmute':'Mute'}</button></>}
      <button className="wide" onClick={openSettings}>Advanced OBS Properties</button>
      <div className="two"><button onClick={renameSelected}>Rename</button><button disabled={busy==='remove'} onClick={removeSelected}>Remove</button></div>
    </div>}
  </aside>
}
