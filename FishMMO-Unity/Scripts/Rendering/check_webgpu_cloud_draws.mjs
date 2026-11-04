#!/usr/bin/env node
// Count real cloud draw executions outside frame-time sampling; restore all hooks.
import fs from 'node:fs';
import path from 'node:path';
// Loopback origin of the owned test page (default 8090; another port serves a second player beside it).
const origin=(process.env.FISHMMO_WEBGPU_ORIGIN||'http://127.0.0.1:8090');
const file = process.argv[2] || 'Library/WebGpuPerformance/cloud-draw-check.json';
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
fs.mkdirSync(path.dirname(file),{recursive:true});
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t=>t.type==='page' && t.url===origin+'/');
if (!tab) throw Error('Missing isolated localhost WebGPU test page');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject});
let next=0;
const pending=new Map();
ws.onmessage=e=>{
  const m=JSON.parse(e.data),p=pending.get(m.id);
  if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}
};
const call=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('DevTools timeout: '+method))},30000);
  pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));
});
try {
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  const result=await call('Runtime.evaluate',{awaitPromise:true,returnByValue:true,expression:`(async()=>{
    const proto=GPURenderPassEncoder.prototype, originalSet=proto.setPipeline;
    const current=new WeakMap(),counts={},originals={};
    const methods=['draw','drawIndexed','drawIndirect','drawIndexedIndirect'];
    proto.setPipeline=function(p){
      current.set(this,p.label==='Hidden/FishMMO/Weather/Clouds'?p.label:null);
      return originalSet.call(this,p);
    };
    for(const name of methods){
      originals[name]=proto[name];
      proto[name]=function(...args){
        const label=current.get(this);if(label)counts[label]=(counts[label]||0)+1;
        return originals[name].apply(this,args);
      };
    }
    try {
      await new Promise(resolve=>setTimeout(resolve,5000));
      return JSON.stringify({seconds:5,cloudDraws:counts,total:Object.values(counts).reduce((a,b)=>a+b,0),monitor:window.fishWebGpuTestStatus,visibility:document.visibilityState});
    } finally {
      proto.setPipeline=originalSet;for(const name of methods)proto[name]=originals[name];
    }
  })()`});
  if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));
  const data=JSON.parse(result.result.value);
  data.valid=data.total>0 && data.visibility==='visible' && data.monitor?.device==='created' && data.monitor.gpuErrors===0 && data.monitor.runtimeErrors===0;
  fs.writeFileSync(file,JSON.stringify(data,null,2)+'\n');console.log(data);
  if(!data.valid)process.exitCode=2;
} finally {ws.close()}
