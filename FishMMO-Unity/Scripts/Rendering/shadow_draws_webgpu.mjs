#!/usr/bin/env node
// Draws per frame into the main light's shadow atlas and into the cached-shadow atlas, on the owned isolated WebGPU test page.
// A depth-only 16-bit render pass the size of the main-light atlas is a shadow pass; the one that also draws the cache's tile
// clear (FishShadowCacheMerge's "Clear") is the cache pass, the one URP clears is URP's own (where the merge draws too).
// Navigates the tab freshly (the hooks must see pipeline creation), applies the settings, samples per animation frame.
// Usage: node shadow_draws_webgpu.mjs <out.json> [seconds] [view]   (view: forest|settlement; FISHMMO_WEBGPU_SETTINGS)
import fs from 'node:fs';
const origin = process.env.FISHMMO_WEBGPU_ORIGIN || 'http://127.0.0.1:8090';
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const file = process.argv[2] || 'Library/WebGpuPerformance/shadow-draws.json';
const seconds = Number(process.argv[3] || 10);
const viewName = process.argv[4] || 'settlement';
const views = {
  forest:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427],
  settlement:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056],
};
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url.startsWith(origin + '/'));
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {ws.onopen=resolve; ws.onerror=reject});
let next = 0; const pending = new Map();
ws.onmessage = e => {const m=JSON.parse(e.data),p=pending.get(m.id);if(p){pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}};
const call = (method,params={}) => new Promise((resolve,reject) => {const id=++next;pending.set(id,{resolve,reject});ws.send(JSON.stringify({id,method,params}))});
const evaluate = async expression => {const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value};
const pause = ms => new Promise(r => setTimeout(r, ms));
const hooks = `
(()=>{if(!globalThis.GPUDevice)return;
const S=window.fishShadowDraws={modules:new WeakMap(),pipes:new WeakMap(),views:new WeakMap(),enc:new WeakMap(),frame:{urp:0,cache:0,merge:0,clear:0},frames:[],counting:false};
const D=GPUDevice.prototype;
const sm=D.createShaderModule;D.createShaderModule=function(d){const m=sm.call(this,d);S.modules.set(m,(d.code||'').split('\\n')[0]);return m};
const tag=(p,d)=>S.pipes.set(p,[d.fragment&&d.fragment.module,d.vertex&&d.vertex.module].some(m=>m&&(S.modules.get(m)||'').includes('FishShadowCacheMerge'))?'cache':'other');
const rp=D.createRenderPipeline;D.createRenderPipeline=function(d){const p=rp.call(this,d);tag(p,d);return p};
const rpa=D.createRenderPipelineAsync;D.createRenderPipelineAsync=function(d){return rpa.call(this,d).then(p=>{tag(p,d);return p})};
const cv=GPUTexture.prototype.createView;GPUTexture.prototype.createView=function(d){const v=cv.call(this,d);S.views.set(v,{format:this.format,w:this.width,h:this.height});return v};
const ce=GPUCommandEncoder.prototype,brp=ce.beginRenderPass;ce.beginRenderPass=function(d){const e=brp.call(this,d);const a=d.depthStencilAttachment;const v=a&&S.views.get(a.view);
  if(v&&v.format==='depth16unorm'&&!(d.colorAttachments||[]).some(c=>c))S.enc.set(e,{load:a.depthLoadOp,draws:0,special:0});return e};
const E=GPURenderPassEncoder.prototype,cur=new WeakMap();
const sp=E.setPipeline;E.setPipeline=function(p){cur.set(this,S.pipes.get(p));return sp.call(this,p)};
for(const n of ['draw','drawIndexed','drawIndirect','drawIndexedIndirect']){const o=E[n];E[n]=function(...a){const pass=S.enc.get(this);if(pass){if(cur.get(this)==='other')pass.draws++;else pass.special++}return o.apply(this,a)}}
const end=E.end;E.end=function(){const pass=S.enc.get(this);if(pass&&S.counting){if(pass.load==='clear'){S.frame.urp+=pass.draws;S.frame.merge+=pass.special}else{S.frame.cache+=pass.draws;S.frame.clear+=pass.special}}return end.call(this)};
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
  for (const s of (process.env.FISHMMO_WEBGPU_SETTINGS || '').split(';').filter(Boolean))
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetSetting',${JSON.stringify(s)})`);
  await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetView',${JSON.stringify(views[viewName].join(','))})`);
  await pause(20000);
  const data = JSON.parse(await evaluate(`(async()=>{const S=window.fishShadowDraws;S.frames=[];S.frame={urp:0,cache:0,merge:0,clear:0};S.counting=true;
    await new Promise(r=>{const t=performance.now()+${seconds*1000};(function tick(){S.frames.push(S.frame);S.frame={urp:0,cache:0,merge:0,clear:0};performance.now()<t?requestAnimationFrame(tick):r()})()});
    S.counting=false;const f=S.frames.slice(1);const sum=k=>f.reduce((a,x)=>a+x[k],0);
    const refresh=f.filter(x=>x.cache>0),steady=f.filter(x=>x.cache===0);const mean=(l,k)=>l.length?l.reduce((a,x)=>a+x[k],0)/l.length:0;
    return JSON.stringify({frames:f.length,refreshFrames:refresh.length,urpDrawsPerFrame:sum('urp')/f.length,urpDrawsSteady:mean(steady,'urp'),urpDrawsOnRefresh:mean(refresh,'urp'),
      cacheDrawsOnRefresh:mean(refresh,'cache'),cacheDrawsPerFrame:sum('cache')/f.length,maxShadowDraws:Math.max(...f.map(x=>x.urp+x.cache)),mergeDrawsPerFrame:sum('merge')/f.length,monitor:window.fishWebGpuTestStatus})})()`));
  data.view = viewName; data.settings = process.env.FISHMMO_WEBGPU_SETTINGS || '';
  fs.writeFileSync(file, JSON.stringify(data,null,2)+'\n');
  console.log(JSON.stringify(data));
  await call('Page.removeScriptToEvaluateOnNewDocument',{identifier});
} finally { ws.close(); }
