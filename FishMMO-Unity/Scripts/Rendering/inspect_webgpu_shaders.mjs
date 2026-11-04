#!/usr/bin/env node
// Inspect real browser-compiled procedural shaders after capture_webgpu_browser.mjs.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
// Loopback origin of the owned test page (default 8090; another port serves a second player beside it).
const origin=(process.env.FISHMMO_WEBGPU_ORIGIN||'http://127.0.0.1:8090');
const output = path.resolve(process.argv[2] || 'Library/WebGpuPerformance/shaders');
const firstInstance = process.argv[3] !== 'fallback';
// Optional 'brg': also require the twins' BatchRendererGroup (DOTS) programs, within the uniform limit and without the
// material/user metadata cbuffers WebGPU drops (Docs/Rendering/2026-11-01/webgpu-constants).
const expectBrg = process.argv[4] === 'brg';
const twin = /Fish(WeatherLitIndirect|VegetationIndirect|VegetationPrime)\.shader/;
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url === origin+'/');
if (!tab) throw Error('No isolated WebGPU test page');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {ws.onopen=resolve;ws.onerror=reject});
try {
  const result = await new Promise((resolve,reject) => {
    const timer = setTimeout(()=>reject(Error('Shader inspection timed out')),30000);
    ws.onmessage = e => {const m=JSON.parse(e.data);if(m.id===1){clearTimeout(timer);m.error?reject(Error(JSON.stringify(m.error))):resolve(m.result)}};
    ws.send(JSON.stringify({id:1,method:'Runtime.evaluate',params:{returnByValue:true,expression:`JSON.stringify({monitor:window.fishWebGpuTestStatus,device:window.webgpuTest?.devices,shaders:[...pipelineSources.values()].filter(p=>/FishVisibleInstances|unity_DOTSInstanceData/.test(p.vertex||'')||/FishVisibleInstances|unity_DOTSInstanceData/.test(p.fragment||''))})`}}));
  });
  if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));
  const state = JSON.parse(result.result.value);
  fs.mkdirSync(output,{recursive:true});
  const seen = new Set(), shaders = [];
  for(const p of state.shaders)for(const stage of ['vertex','fragment']) {
    const code=p[stage];if(!code)continue;
    const dots=code.includes('unity_DOTSInstanceData')&&twin.test(code.split('\n')[0]);
    if(!code.includes('FishVisibleInstances')&&!dots)continue;
    const hash=crypto.createHash('sha256').update(code).digest('hex');if(seen.has(hash))continue;seen.add(hash);
    const file=`${stage}-${hash.slice(0,12)}.wgsl`;
    fs.writeFileSync(path.join(output,file),code);
    shaders.push({file,sha256:hash,source:code.split('\n')[0],stage,commandID:code.includes('unity_BaseCommandID'),commandBases:code.includes('FishCommandBases'),uniforms:(code.match(/var<uniform>/g)||[]).length,dots,metadataCbuffers:(code.match(/var<uniform>\s+\w+\s*:\s*UnityDOTSInstancing_(Material|User)PropertyMetadata/g)||[]).length});
  }
  const device=state.device?.[0], feature=device?.features.includes('indirect-first-instance');
  const fallbackBindings=shaders.some(s=>s.commandID&&s.commandBases);
  const valid=shaders.length>0 && (firstInstance||fallbackBindings) && feature===firstInstance && state.monitor?.gpuErrors===0 && state.monitor?.runtimeErrors===0 && shaders.every(s=>s.uniforms<=device.limits.uniforms && s.metadataCbuffers===0 && (s.dots || !firstInstance || (!s.commandID&&!s.commandBases))) &&
    (!expectBrg || shaders.some(s=>s.dots));
  const summary={valid,expectedFirstInstance:firstInstance,expectBrg,dotsPrograms:shaders.filter(s=>s.dots).length,device,monitor:state.monitor,shaders};
  fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2)+'\n');
  console.log(JSON.stringify(summary,null,2));
  if(!valid)process.exitCode=2;
} finally {ws.close()}
