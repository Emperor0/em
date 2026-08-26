import fs from "fs";
import path from "path";
import {bundle} from "@remotion/bundler";
import {renderMedia, selectComposition} from "@remotion/renderer";

const manifestPath=process.argv[2];
if(!manifestPath) throw new Error("manifest path required");
const m=JSON.parse(fs.readFileSync(manifestPath,"utf8"));
const entry=path.resolve("remotion/src/index.tsx");

// Inject props via tiny generated entry file so render is deterministic.
const temp=path.resolve("remotion/src/generated-entry.tsx");
const props={voice:m.voice,assets:m.assets,title:m.title,script:m.script};
fs.writeFileSync(temp,`
(globalThis).__D7_PROPS__=${JSON.stringify(props)};
import "./index";
`);
const serveUrl=await bundle({entryPoint:temp,webpackOverride:(c)=>c});
const composition=await selectComposition({serveUrl,id:"ViralShort",inputProps:props});
await renderMedia({
  composition,serveUrl,codec:"h264",outputLocation:m.output,inputProps:props,
  crf:17,videoBitrate:"8M",audioBitrate:"192k",pixelFormat:"yuv420p"
});
console.log(m.output);
