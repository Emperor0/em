import fs from "node:fs";
const required=["docs/MASTER_SPEC.md","docs/ARCHITECTURE.md","src/kernel/blackcore.ts","src/kernel/state-engine.ts","src/kernel/event-bus.ts","src/kernel/permission-engine.ts","src/kernel/evidence-engine.ts","src/kernel/model-gateway.ts","src/builder/builder-studio.ts","apps/desktop-preview/index.html"];
const missing=required.filter(p=>!fs.existsSync(new URL(`../${p}`,import.meta.url)));
if(missing.length){console.error("Missing:",missing);process.exit(1);}console.log(`STRUCTURE_OK ${required.length}/${required.length}`);
