#!/usr/bin/env node
// Which uniform buffers each live render pipeline binds, which of them Unity's WebGPU backend gives a dynamic offset
// (the rest take a dedicated buffer per update), and how often each pipeline draws in the forest and settlement views.
// Navigates the owned isolated test tab freshly (the hooks must see pipeline creation). Never a normal browser profile.
// Usage: node audit_webgpu_uniforms.mjs <out-dir> [window-seconds]
import fs from 'node:fs';
import path from 'node:path';
const origin = process.env.FISHMMO_WEBGPU_ORIGIN || 'http://127.0.0.1:8090';
const output = path.resolve(process.argv[2] || 'Library/WebGpuPerformance/uniform-audit');
const seconds = Number(process.argv[3] || 5);
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const views = [
  {name:'forest', coordinates:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427]},
  {name:'settlement', coordinates:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056]},
];
fs.mkdirSync(output, {recursive:true});
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url.startsWith(origin + '/'));
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {ws.onopen=resolve; ws.onerror=reject});
let next = 0;
const pending = new Map();
ws.onmessage = e => {const m=JSON.parse(e.data),p=pending.get(m.id);if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}};
const call = (method,params={}) => new Promise((resolve,reject) => {
  const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('DevTools timeout: '+method))},180000);
  pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));
});
const evaluate = async expression => {
  const r = await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if (r.exceptionDetails) throw Error(JSON.stringify(r.exceptionDetails));
  return r.result.value;
};
const pause = ms => new Promise(r => setTimeout(r, ms));
const hooks = `
(()=>{if(!globalThis.GPUDevice)return;
const A=window.fishUniformAudit={modules:new WeakMap(),layouts:new WeakMap(),pipelineLayouts:new WeakMap(),pipelines:new WeakMap(),list:[],counting:false,draws:new Map(),uniformBuffers:0,uniformBytes:0,writes:0,writeBytes:0};
const D=GPUDevice.prototype;
const sm=D.createShaderModule;D.createShaderModule=function(d){const m=sm.call(this,d);A.modules.set(m,d.code);return m};
const bgl=D.createBindGroupLayout;D.createBindGroupLayout=function(d){const l=bgl.call(this,d);A.layouts.set(l,(d.entries||[]).map(e=>({binding:e.binding,visibility:e.visibility,type:e.buffer?(e.buffer.type||'uniform'):e.texture?'texture':e.sampler?'sampler':e.storageTexture?'storageTexture':'other',dynamic:!!(e.buffer&&e.buffer.hasDynamicOffset)})));return l};
const pl=D.createPipelineLayout;D.createPipelineLayout=function(d){const p=pl.call(this,d);A.pipelineLayouts.set(p,(d.bindGroupLayouts||[]).map(l=>A.layouts.get(l)||null));return p};
function remember(p,d){const info={id:A.list.length,label:d.label||'',groups:d.layout&&d.layout!=='auto'?A.pipelineLayouts.get(d.layout)||null:null,vertex:A.modules.get(d.vertex&&d.vertex.module)||'',fragment:A.modules.get(d.fragment&&d.fragment.module)||''};A.list.push(info);A.pipelines.set(p,info);}
const rp=D.createRenderPipeline;D.createRenderPipeline=function(d){const p=rp.call(this,d);remember(p,d);return p};
const rpa=D.createRenderPipelineAsync;D.createRenderPipelineAsync=function(d){return rpa.call(this,d).then(p=>{remember(p,d);return p})};
const cb=D.createBuffer;D.createBuffer=function(d){if(A.counting&&(d.usage&GPUBufferUsage.UNIFORM)){A.uniformBuffers++;A.uniformBytes+=d.size}return cb.call(this,d)};
const wb=GPUQueue.prototype.writeBuffer;GPUQueue.prototype.writeBuffer=function(b,o,data,...r){if(A.counting){A.writes++;A.writeBytes+=(r[1]!==undefined?r[1]:(data.byteLength||0))}return wb.call(this,b,o,data,...r)};
const E=GPURenderPassEncoder.prototype,cur=new WeakMap(),sp=E.setPipeline;
E.setPipeline=function(p){const i=A.pipelines.get(p);if(i&&!i.label&&p.label)i.label=p.label;cur.set(this,i);return sp.call(this,p)};
for(const n of ['draw','drawIndexed','drawIndirect','drawIndexedIndirect']){const o=E[n];E[n]=function(...a){if(A.counting){const i=cur.get(this);if(i)A.draws.set(i.id,(A.draws.get(i.id)||0)+1)}return o.apply(this,a)}}
})();`;
try {
  await call('Page.enable'); await call('Runtime.enable');
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  const {identifier} = await call('Page.addScriptToEvaluateOnNewDocument',{source:hooks});
  await call('Page.navigate',{url:origin+'/'});
  await pause(Number(process.env.FISHMMO_WEBGPU_SETTLE || 90)*1000);
  if (process.env.FISHMMO_WEBGPU_RES) {
    const [w,h] = process.env.FISHMMO_WEBGPU_RES.split('x').map(Number);
    await call('Emulation.setDeviceMetricsOverride',{width:w,height:h,deviceScaleFactor:1,mobile:false});
    await evaluate(`(()=>{const c=document.querySelector('canvas');c.style.width='${w}px';c.style.height='${h}px';c.width=${w};c.height=${h};dispatchEvent(new Event('resize'));})()`);
  }
  // Optional harness settings ("name=value;...") applied before the views, e.g. cachedShadows=1.
  for (const setting of (process.env.FISHMMO_WEBGPU_SETTINGS || '').split(';').filter(Boolean)) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetSetting',${JSON.stringify(setting)})`);
  }
  const result = {origin, seconds, settings:process.env.FISHMMO_WEBGPU_SETTINGS||'', views:{}};
  for (const view of views) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetView',${JSON.stringify(view.coordinates.join(','))})`);
    await pause(20000);
    const data = JSON.parse(await evaluate(`(async()=>{const A=window.fishUniformAudit;A.draws.clear();A.uniformBuffers=A.uniformBytes=A.writes=A.writeBytes=0;A.counting=true;
      const f0=performance.now();let frames=0;await new Promise(r=>{const t=performance.now()+${seconds*1000};(function tick(){frames++;performance.now()<t?requestAnimationFrame(tick):r()})()});A.counting=false;
      const names=(code)=>{const m={};for(const x of (code||'').matchAll(/@group\\((\\d+)\\)\\s*@binding\\((\\d+)\\)\\s*var<uniform>\\s*\\w+\\s*:\\s*(\\w+)/g))m[x[1]+':'+x[2]]=x[3];return m};
      const rows=[];for(const [id,count] of A.draws){const p=A.list[id];const vn=names(p.vertex),fn=names(p.fragment);const uniforms=[];
        (p.groups||[]).forEach((g,gi)=>(g||[]).forEach(e=>{if(e.type!=='uniform')return;const key=gi+':'+e.binding;uniforms.push({group:gi,binding:e.binding,name:fn[key]||vn[key]||'?',vertex:key in vn,fragment:key in fn,visibility:e.visibility,dynamic:e.dynamic})}));
        rows.push({label:p.label,source:((p.vertex||'').split('\\n')[0]||'').replace(/^\\/\\//,''),draws:count,uniforms,uniformCount:uniforms.length,dynamicCount:uniforms.filter(u=>u.dynamic).length,staticNames:uniforms.filter(u=>!u.dynamic).map(u=>u.name)})}
      rows.sort((a,b)=>b.draws-a.draws);
      return JSON.stringify({rafFrames:frames,uniformBuffersCreated:A.uniformBuffers,uniformBytesCreated:A.uniformBytes,writeBuffers:A.writes,writeBytes:A.writeBytes,pipelines:rows,monitor:window.fishWebGpuTestStatus})})()`));
    result.views[view.name] = data;
    fs.writeFileSync(path.join(output,'audit.json'),JSON.stringify(result,null,2)+'\n');
    console.log(view.name, 'pipelines', data.pipelines.length, 'uniform buffers created', data.uniformBuffersCreated, 'writes', data.writeBuffers);
  }
  await call('Page.removeScriptToEvaluateOnNewDocument',{identifier});
} finally {ws.close()}
