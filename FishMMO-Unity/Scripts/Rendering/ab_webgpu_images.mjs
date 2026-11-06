#!/usr/bin/env node
// Same view, same held world clock: a screenshot before and after a harness setting change (an owner A/B pair).
// Usage: node ab_webgpu_images.mjs <out-dir> <name> <view> "<before settings>" "<after settings>"
//   settings are "name=value;name=value" for WebGpuSmokeProbe.SetSetting; view is forest|forest-down|settlement|settlement-close.
// Writes <out-dir>/<name>-<view>-a.png and -b.png, restores the "before" settings and releases the clock.
import fs from 'node:fs';
import path from 'node:path';
const [outDir, name, view, before = '', after = ''] = process.argv.slice(2);
if (!outDir || !name || !view) throw Error('Usage: ab_webgpu_images.mjs <out-dir> <name> <view> <before> <after>');
const origin = process.env.FISHMMO_WEBGPU_ORIGIN || 'http://127.0.0.1:8090';
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const settle = Number(process.env.FISHMMO_WEBGPU_AB_SETTLE || 4) * 1000;
const views = {
  forest:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427],
  settlement:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056],
  // The forest view pitched down to the ground before it (the near grass and the ground's layers).
  'forest-down':[-1082.901245,513.39331,429.819427,-1082.901245,503.39331,445.819427],
  // The same town from close by (a street-level look at the structures' surfaces).
  'settlement-close':[1885.555176,144.37264,-1515.95056,1850.555176,126.37264,-1480.95056],
};
if (!views[view]) throw Error('Unknown view ' + view);
fs.mkdirSync(outDir, {recursive:true});
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url === origin + '/');
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {ws.onopen=resolve; ws.onerror=reject});
let next = 0; const pending = new Map();
// The page's console while the pair is taken, beside the images (<name>-<view>-console.txt): what the player logged about them.
const consoleLines = [];
ws.onmessage = e => {const m=JSON.parse(e.data);if(m.method==='Runtime.consoleAPICalled'){consoleLines.push((m.params.args||[]).map(a=>a.value??'').join(' '));return}const p=pending.get(m.id);if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}};
const call = (method,params={}) => new Promise((resolve,reject) => {const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error('DevTools timeout: '+method))},120000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}))});
const evaluate = async expression => {const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value};
const send = (method, arg) => evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test',${JSON.stringify(method)},${JSON.stringify(arg)})`);
const apply = async list => { for (const s of list.split(';').filter(Boolean)) await send('SetSetting', s); };
const pause = ms => new Promise(r => setTimeout(r, ms));
const shot = async file => { const r = await call('Page.captureScreenshot',{format:'png'}); fs.writeFileSync(file, Buffer.from(r.data,'base64')); };
try {
  await call('Runtime.enable');
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  await call('Emulation.setDeviceMetricsOverride',{width:2560,height:1440,deviceScaleFactor:1,mobile:false});
  await evaluate(`(()=>{const c=document.querySelector('canvas');c.style.width='2560px';c.style.height='1440px';c.width=2560;c.height=1440;dispatchEvent(new Event('resize'));})()`);
  await send('SetView', views[view].join(','));
  // Optional fixed world clock (a fraction of the day, as SetTime takes) so pairs from two players share it.
  if (process.env.FISHMMO_WEBGPU_AB_TIME) { await send('SetTime', process.env.FISHMMO_WEBGPU_AB_TIME); await pause(8000); }
  await pause(15000);
  await send('SetSetting', 'paused=1');
  await apply(before); await pause(settle);
  const a = path.join(outDir, `${name}-${view}-a.png`), b = path.join(outDir, `${name}-${view}-b.png`);
  await shot(a);
  await send('SetSetting', 'shadowCacheLog=1');
  await apply(after); await pause(settle);
  await shot(b);
  await send('SetSetting', 'shadowCacheLog=1');
  await pause(500);
  await apply(before);
  await send('SetSetting', 'paused=0');
  const state = JSON.parse(await evaluate('JSON.stringify(window.fishWebGpuTestStatus)'));
  console.log(JSON.stringify({name, view, before, after, a, b, monitor:state}));
} finally {
  fs.writeFileSync(path.join(outDir, `${name}-${view}-console.txt`), consoleLines.join('\n'));
  // The canvas back to the page's own size (it fills the window), so the tab is usable at its real size afterwards.
  await evaluate(`(()=>{const c=document.querySelector('canvas');if(c){c.style.width='';c.style.height='';}})()`).catch(()=>{}); await call('Emulation.clearDeviceMetricsOverride').catch(()=>{}); ws.close();
}
