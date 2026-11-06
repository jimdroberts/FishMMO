#!/usr/bin/env node
// A timed screenshot sequence of one view on the owned isolated test page (e.g. a fast clock, to see shadow stepping).
// Usage: node capture_webgpu_sequence.mjs <out-dir> <name> <view> <frames> <interval-ms> ["setting=value;..."]
import fs from 'node:fs';
import path from 'node:path';
const [outDir, name, view, frames = '12', interval = '250', settings = ''] = process.argv.slice(2);
const origin = process.env.FISHMMO_WEBGPU_ORIGIN || 'http://127.0.0.1:8090';
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const views = {
  forest:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427],
  settlement:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056],
};
if (!outDir || !name || !views[view]) throw Error('Usage: capture_webgpu_sequence.mjs <out-dir> <name> <forest|settlement> <frames> <interval-ms> [settings]');
fs.mkdirSync(outDir, {recursive:true});
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url === origin + '/');
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {ws.onopen=resolve; ws.onerror=reject});
let next = 0; const pending = new Map();
ws.onmessage = e => {const m=JSON.parse(e.data),p=pending.get(m.id);if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}};
const call = (method,params={}) => new Promise((resolve,reject) => {const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('DevTools timeout: '+method))},120000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}))});
const evaluate = async expression => {const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value};
const send = (method, arg) => evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test',${JSON.stringify(method)},${JSON.stringify(arg)})`);
const pause = ms => new Promise(r => setTimeout(r, ms));
try {
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  await call('Emulation.setDeviceMetricsOverride',{width:2560,height:1440,deviceScaleFactor:1,mobile:false});
  await evaluate(`(()=>{const c=document.querySelector('canvas');c.style.width='2560px';c.style.height='1440px';c.width=2560;c.height=1440;dispatchEvent(new Event('resize'));})()`);
  await send('SetView', views[view].join(','));
  // Optional start time (a fraction of the day, as SetTime takes), as ab_webgpu_images.mjs.
  if (process.env.FISHMMO_WEBGPU_AB_TIME) { await send('SetTime', process.env.FISHMMO_WEBGPU_AB_TIME); await pause(8000); }
  for (const s of settings.split(';').filter(Boolean)) await send('SetSetting', s);
  await pause(15000);
  const files = [];
  for (let i = 0; i < Number(frames); i++) {
    const r = await call('Page.captureScreenshot',{format:'png'});
    const file = path.join(outDir, `${name}-${view}-${String(i).padStart(2,'0')}.png`);
    fs.writeFileSync(file, Buffer.from(r.data,'base64')); files.push(file);
    await pause(Number(interval));
  }
  console.log(JSON.stringify({name, view, frames:files.length, monitor:JSON.parse(await evaluate('JSON.stringify(window.fishWebGpuTestStatus)'))}));
} finally {
  // The canvas back to the page's own size (it fills the window), so the tab is usable at its real size afterwards.
  await evaluate(`(()=>{const c=document.querySelector('canvas');if(c){c.style.width='';c.style.height='';}})()`).catch(()=>{}); await call('Emulation.clearDeviceMetricsOverride').catch(()=>{}); ws.close();
}
