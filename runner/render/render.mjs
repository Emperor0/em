import fs from "fs";
import path from "path";
import http from "http";
import crypto from "crypto";
import {bundle} from "@remotion/bundler";
import {renderMedia, selectComposition} from "@remotion/renderer";

const manifestPath=process.argv[2];
if(!manifestPath) throw new Error("manifest path required");
const m=JSON.parse(fs.readFileSync(manifestPath,"utf8"));
const entry=path.resolve("remotion/src/index.tsx");

const mime=(p)=>{
  const ext=path.extname(p).toLowerCase();
  return ({
    ".mp4":"video/mp4",
    ".webm":"video/webm",
    ".mov":"video/quicktime",
    ".png":"image/png",
    ".jpg":"image/jpeg",
    ".jpeg":"image/jpeg",
    ".webp":"image/webp",
    ".mp3":"audio/mpeg",
    ".wav":"audio/wav",
    ".m4a":"audio/mp4",
    ".aac":"audio/aac"
  })[ext]||"application/octet-stream";
};

const files=new Map();
const register=(filePath)=>{
  if(!filePath) return "";
  const abs=path.resolve(filePath);
  if(!fs.existsSync(abs)) throw new Error(`Media file missing: ${abs}`);
  const token=`/${crypto.randomUUID()}${path.extname(abs).toLowerCase()}`;
  files.set(token,abs);
  return token;
};

const voiceToken=register(m.voice);
const assetTokens=(m.assets||[]).map((a)=>a.path?{...a,__token:register(a.path)}:{...a});

const server=http.createServer((req,res)=>{
  try{
    const pathname=new URL(req.url||"/","http://127.0.0.1").pathname;
    const file=files.get(pathname);
    if(!file){res.writeHead(404);res.end("Not found");return;}
    const stat=fs.statSync(file);
    const type=mime(file);
    const range=req.headers.range;
    res.setHeader("Content-Type",type);
    res.setHeader("Accept-Ranges","bytes");
    res.setHeader("Cache-Control","no-store");
    if(range){
      const match=/bytes=(\d*)-(\d*)/.exec(range);
      if(!match){res.writeHead(416);res.end();return;}
      const start=match[1]?Number(match[1]):0;
      const end=match[2]?Math.min(Number(match[2]),stat.size-1):stat.size-1;
      if(start> end || start>=stat.size){
        res.writeHead(416,{"Content-Range":`bytes */${stat.size}`});res.end();return;
      }
      res.writeHead(206,{
        "Content-Range":`bytes ${start}-${end}/${stat.size}`,
        "Content-Length":String(end-start+1)
      });
      fs.createReadStream(file,{start,end}).pipe(res);
    }else{
      res.writeHead(200,{"Content-Length":String(stat.size)});
      fs.createReadStream(file).pipe(res);
    }
  }catch(err){
    res.writeHead(500);res.end(String(err));
  }
});

await new Promise((resolve,reject)=>{
  server.once("error",reject);
  server.listen(0,"127.0.0.1",resolve);
});
const address=server.address();
if(!address || typeof address==="string") throw new Error("Could not start media server");
const base=`http://127.0.0.1:${address.port}`;
const props={
  voice:base+voiceToken,
  assets:assetTokens.map(({__token,...a})=>__token?{...a,path:base+__token}:a),
  title:m.title,
  script:m.script
};

try{
  const serveUrl=await bundle({entryPoint:entry,webpackOverride:(c)=>c});
  const composition=await selectComposition({serveUrl,id:"ViralShort",inputProps:props});
  await renderMedia({
    composition,
    serveUrl,
    codec:"h264",
    outputLocation:m.output,
    inputProps:props,
    videoBitrate:"12M",
    audioBitrate:"256k",
    pixelFormat:"yuv420p",
    x264Preset:"medium"
  });
  console.log(m.output);
} finally {
  await new Promise((resolve)=>server.close(()=>resolve()));
}
