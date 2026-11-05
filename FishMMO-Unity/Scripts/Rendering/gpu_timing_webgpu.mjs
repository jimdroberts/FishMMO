#!/usr/bin/env node
// GPU time per frame on the owned isolated WebGPU test page, from timestamp queries injected into every render and compute
// pass Unity begins (the device must have 'timestamp-query'). Attributes each pass to the pipelines drawn/dispatched in it.
// Measurement only: the hooks add resolve/readback work, so never run it during a frame-time sample. All hooks are restored.
// Launch the isolated profile with FISHMMO_WEBGPU_DEV_FEATURES=1 for unquantized timestamps (Chrome rounds to 100 µs otherwise).
// Usage: node gpu_timing_webgpu.mjs <out.json> [seconds] [view]   (view: forest|settlement|current)
import fs from 'node:fs';
import path from 'node:path';
const origin = process.env.FISHMMO_WEBGPU_ORIGIN || 'http://127.0.0.1:8090';
const file = path.resolve(process.argv[2] || 'Library/WebGpuPerformance/gpu-timing.json');
const seconds = Number(process.argv[3] || 5);
const view = process.argv[4] || 'current';
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const views = {
  forest:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427],
  settlement:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056],
  // The forest view pitched down to the ground before it (the near grass and the ground's layers).
  'forest-down':[-1082.901245,513.39331,429.819427,-1082.901245,503.39331,445.819427],
  'settlement-close':[1885.555176,144.37264,-1515.95056,1850.555176,126.37264,-1480.95056],
};
fs.mkdirSync(path.dirname(file), {recursive:true});
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url === origin + '/');
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {ws.onopen=resolve; ws.onerror=reject});
let next = 0; const pending = new Map();
ws.onmessage = e => {const m=JSON.parse(e.data),p=pending.get(m.id);if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}};
const call = (method,params={}) => new Promise((resolve,reject) => {const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('DevTools timeout: '+method))},120000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}))});
const evaluate = async expression => {const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value};
try {
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  const resolution = process.env.FISHMMO_WEBGPU_RES?.split('x').map(Number);
  if (resolution) {
    await call('Emulation.setDeviceMetricsOverride',{width:resolution[0],height:resolution[1],deviceScaleFactor:1,mobile:false});
    await evaluate(`(()=>{const c=document.querySelector('canvas');c.style.width='${resolution[0]}px';c.style.height='${resolution[1]}px';c.width=${resolution[0]};c.height=${resolution[1]};dispatchEvent(new Event('resize'));})()`);
    await new Promise(r => setTimeout(r, 3000));
  }
  for (const setting of (process.env.FISHMMO_WEBGPU_SETTINGS || '').split(';').filter(Boolean)) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetSetting',${JSON.stringify(setting)})`);
  }
  if (views[view]) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetView',${JSON.stringify(views[view].join(','))})`);
    await new Promise(r => setTimeout(r, 20000));
  }
  const result = JSON.parse(await evaluate(`(async()=>{
    const CAP=4096, D=GPUDevice.prototype, Q=GPUQueue.prototype, E=GPUCommandEncoder.prototype, R=GPURenderPassEncoder.prototype, C=GPUComputePassEncoder.prototype;
    const orig={cce:D.createCommandEncoder,brp:E.beginRenderPass,bcp:E.beginComputePass,submit:Q.submit,rsp:R.setPipeline,csp:C.setPipeline,gct:GPUCanvasContext.prototype.getCurrentTexture};
    let device=null,qs=null,index=0,open=[],frames=0,passCount=0,busyNs=0n,spanNs=0n,skipped=0,readbacks=[],errors=0;
    const byGroup=new Map(), passOf=new WeakMap();
    function ensure(dev){if(!device){device=dev;qs=device.createQuerySet({type:'timestamp',count:CAP})}}
    D.createCommandEncoder=function(...a){ensure(this);return orig.cce.apply(this,a)};
    function wrap(kind,o){return function(desc={}){if(!qs||desc.timestampWrites||index+2>CAP){skipped++;return o.call(this,desc)}
      const depthOnly=kind==='render'&&!(desc.colorAttachments||[]).some(Boolean);const rec={kind:depthOnly?'render (depth only)':kind,label:desc.label||'',i:index,pipes:new Map()};index+=2;open.push(rec);
      const p=o.call(this,{...desc,timestampWrites:{querySet:qs,beginningOfPassWriteIndex:rec.i,endOfPassWriteIndex:rec.i+1}});passOf.set(p,rec);return p}}
    E.beginRenderPass=wrap('render',orig.brp);E.beginComputePass=wrap('compute',orig.bcp);
    function tally(o){return function(p){const r=passOf.get(this);if(r){const l=p.label||'?';r.pipes.set(l,(r.pipes.get(l)||0)+1)}return o.call(this,p)}}
    R.setPipeline=tally(orig.rsp);C.setPipeline=tally(orig.csp);
    GPUCanvasContext.prototype.getCurrentTexture=function(...a){frames++;return orig.gct.apply(this,a)};
    Q.submit=function(cbs){
      if(!open.length||!device)return orig.submit.call(this,cbs);
      const recs=open;open=[];const n=index;index=0;
      const size=n*8, resolve=device.createBuffer({size,usage:GPUBufferUsage.QUERY_RESOLVE|GPUBufferUsage.COPY_SRC}), read=device.createBuffer({size,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ});
      const enc=orig.cce.call(device);enc.resolveQuerySet(qs,0,n,resolve,0);enc.copyBufferToBuffer(resolve,0,read,0,size);
      const r=orig.submit.call(this,[...cbs,enc.finish()]);
      readbacks.push(read.mapAsync(GPUMapMode.READ).then(()=>{const t=new BigUint64Array(read.getMappedRange());let lo=null,hi=null;
        for(const rec of recs){const b=t[rec.i],e=t[rec.i+1];if(!(e>=b)||b===0n){errors++;continue}const d=e-b;busyNs+=d;passCount++;if(lo===null||b<lo)lo=b;if(hi===null||e>hi)hi=e;
          const top=[...rec.pipes.entries()].sort((x,y)=>y[1]-x[1])[0];const key=rec.kind+': '+(top?top[0]:(rec.label||'(no pipeline)'));
          const g=byGroup.get(key)||{ns:0n,passes:0};g.ns+=d;g.passes++;byGroup.set(key,g)}
        if(lo!==null)spanNs+=hi-lo;read.unmap();read.destroy();resolve.destroy()}).catch(()=>{errors++}));
      return r};
    const f0=frames;await new Promise(r=>setTimeout(r,${seconds*1000}));const fr=frames-f0;
    D.createCommandEncoder=orig.cce;E.beginRenderPass=orig.brp;E.beginComputePass=orig.bcp;Q.submit=orig.submit;R.setPipeline=orig.rsp;C.setPipeline=orig.csp;GPUCanvasContext.prototype.getCurrentTexture=orig.gct;
    await Promise.all(readbacks);if(qs)qs.destroy();
    const ms=ns=>Number(ns)/1e6;
    const groups=[...byGroup.entries()].map(([k,g])=>({group:k,msPerFrame:ms(g.ns)/fr,passesPerFrame:g.passes/fr})).sort((a,b)=>b.msPerFrame-a.msPerFrame);
    return JSON.stringify({seconds:${seconds},frames:fr,frameMsWall:${seconds*1000}/fr,gpuBusyMsPerFrame:ms(busyNs)/fr,gpuSubmitSpanMsPerFrame:ms(spanNs)/fr,passesPerFrame:passCount/fr,skipped,errors,features:device?[...device.features].includes('timestamp-query'):null,groups:groups.slice(0,40),monitor:window.fishWebGpuTestStatus})})()`));
  result.view = view; result.origin = origin;
  result.canvas = JSON.parse(await evaluate(`JSON.stringify({w:document.querySelector('canvas').width,h:document.querySelector('canvas').height})`));
  fs.writeFileSync(file, JSON.stringify(result,null,2)+'\n');
  console.log(JSON.stringify({view, frames:result.frames, frameMsWall:result.frameMsWall, gpuBusyMsPerFrame:result.gpuBusyMsPerFrame, passesPerFrame:result.passesPerFrame, errors:result.errors, skipped:result.skipped}));
} finally {ws.close()}
