#!/usr/bin/env node
// Fixed-view Unity frame timings, optional sampled CPU profiles, and validation state.
// Run against the isolated localhost test browser, never a normal browser profile.
import fs from 'node:fs';
import path from 'node:path';
// Loopback origin of the owned test page (default 8090; another port serves a second player beside it).
const origin=(process.env.FISHMMO_WEBGPU_ORIGIN||'http://127.0.0.1:8090');

const output = path.resolve(process.argv[2] || 'Library/WebGpuPerformance/browser');
const seconds = Number(process.argv[3] || 20);
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const cpu = process.env.FISHMMO_WEBGPU_CPU_PROFILE === '1';
const resolution = process.env.FISHMMO_WEBGPU_RES?.split('x').map(Number);
if (resolution && (resolution.length !== 2 || resolution.some(n=>!Number.isInteger(n)||n<64||n>4096))) throw Error('Expected resolution such as 2560x1440');
const views = [
  {name:'forest', coordinates:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427]},
  {name:'settlement', coordinates:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056]},
];
const selectedViews = process.env.FISHMMO_WEBGPU_VIEWS?.split(',') || views.map(v=>v.name);
if (selectedViews.some(name=>!views.some(v=>v.name===name))) throw Error('Expected forest and/or settlement views');
fs.mkdirSync(output, {recursive:true});
const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url === origin+'/');
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const socket = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {socket.onopen=resolve; socket.onerror=reject});
let next = 0;
const pending = new Map();
const gameSamples = new Map();
socket.onmessage = event => {
  const message = JSON.parse(event.data);
  if (message.id) {
    const request = pending.get(message.id);
    if (!request) return;
    pending.delete(message.id); clearTimeout(request.timer);
    message.error ? request.reject(Error(JSON.stringify(message.error))) : request.resolve(message.result);
  } else if (message.method === 'Runtime.consoleAPICalled' || message.method === 'Runtime.exceptionThrown') {
    fs.appendFileSync(path.join(output,'console.jsonl'),JSON.stringify(message)+'\n');
    const text = (message.params.args || []).map(a => a.value || '').join(' ');
    const prefix = '[WebGPU Performance] ';
    if (text.startsWith(prefix)) {
      try { const sample = JSON.parse(text.slice(prefix.length)); gameSamples.set(sample.name,sample); }
      catch { console.error('Unparseable (truncated?) sample line of '+text.length+' characters'); }
    }
  }
};
const call = (method,params={}) => new Promise((resolve,reject) => {
  const id = ++next;
  const timer = setTimeout(() => {pending.delete(id); reject(Error('DevTools timeout: '+method))},180000);
  pending.set(id,{resolve,reject,timer}); socket.send(JSON.stringify({id,method,params}));
});
const evaluate = async expression => {
  const result = await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
};
const pause = ms => new Promise(resolve => setTimeout(resolve,ms));
const summary = [];
try {
  await call('Runtime.enable'); await call('Page.enable');
  const window = await call('Browser.getWindowForTarget',{targetId:tab.id});
  if (window.bounds.windowState === 'minimized') await call('Browser.setWindowBounds',{windowId:window.windowId,bounds:{windowState:'normal'}});
  await call('Page.bringToFront');
  // Desktop automation can minimize the window again. Keep the page active for RAF.
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  // Compilation, mesh pooling and the initial weather-field preparation must settle.
  await pause(Number(process.env.FISHMMO_WEBGPU_SETTLE || 60)*1000);
  if (process.env.FISHMMO_WEBGPU_BATCHING !== undefined) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetBatching',${JSON.stringify(process.env.FISHMMO_WEBGPU_BATCHING)})`);
  }
  if (process.env.FISHMMO_WEBGPU_BRG !== undefined) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetBrg',${JSON.stringify(process.env.FISHMMO_WEBGPU_BRG)})`);
  }
  // Optional A/B candidate: harness settings "name=value;name=value" applied before any view is sampled.
  for (const setting of (process.env.FISHMMO_WEBGPU_SETTINGS || '').split(';').filter(Boolean)) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetSetting',${JSON.stringify(setting)})`);
  }
  if (resolution) {
    await call('Emulation.setDeviceMetricsOverride',{width:resolution[0],height:resolution[1],deviceScaleFactor:1,mobile:false});
    await evaluate(`(()=>{const c=document.querySelector('canvas');c.style.width='${resolution[0]}px';c.style.height='${resolution[1]}px';c.width=${resolution[0]};c.height=${resolution[1]};dispatchEvent(new Event('resize'));})()`);
  }
  for (const view of views.filter(v=>selectedViews.includes(v.name))) {
    await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetView',${JSON.stringify(view.coordinates.join(','))})`);
    // Optional moving camera: "metres,seconds" (WebGpuSmokeProbe.SetSweep), from this view's eye.
    if (process.env.FISHMMO_WEBGPU_SWEEP) {
      await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','SetSweep',${JSON.stringify(process.env.FISHMMO_WEBGPU_SWEEP)})`);
    }
    await pause(20000);
    if (await evaluate('document.visibilityState') !== 'visible') throw Error('Benchmark window is hidden');
    // Sample before enabling the CPU profiler: profiling must not contaminate frame timings.
    // Consecutive sub-samples of at most 4 s, merged: a player's log line can be capped (8191 characters in WebAssembly 2023
    // builds), which a 20 s list of fast frames overflows. The intervals are the same completed Unity frames.
    const parts = Math.max(1, Math.ceil(seconds/4));
    let sample = null;
    for (let part = 0; part < parts; part++) {
      const id = view.name + '#' + part;
      gameSamples.delete(id);
      await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','BeginSample',${JSON.stringify(id)})`);
      await pause(seconds*1000/parts);
      await evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test','EndSample',${JSON.stringify(id)})`);
      for (let wait = 0; wait < 50 && !gameSamples.has(id); wait++) await pause(100);
      const piece = gameSamples.get(id);
      if (!piece || piece.frameMs.length < 2) throw Error('No completed Unity frame sample: '+id);
      sample = sample ? {...piece, name:view.name, worldSecondsStart:sample.worldSecondsStart, errors:Math.max(sample.errors,piece.errors), frameMs:[...sample.frameMs, ...piece.frameMs]} : {...piece, name:view.name};
    }
    sample.frames = sample.frameMs.length; sample.subSamples = parts;
    const state = JSON.parse(await evaluate('JSON.stringify({visibility:document.visibilityState,focusEmulation:true,monitor:window.fishWebGpuTestStatus,traceEnabled:!!window.webgpuTrace})'));
    const sorted = [...sample.frameMs].sort((a,b)=>a-b);
    const meanMs = sample.frameMs.reduce((a,b)=>a+b,0)/sample.frames;
    const row = {view:view.name,...sample,...state,meanMs,fps:1000/meanMs,
      medianMs:sorted[Math.floor(sorted.length*.5)],p95Ms:sorted[Math.min(sorted.length-1,Math.floor(sorted.length*.95))],
      valid:(process.env.FISHMMO_WEBGPU_EXPECT_FIRST_INSTANCE===undefined || sample.firstInstance===(process.env.FISHMMO_WEBGPU_EXPECT_FIRST_INSTANCE==='1')) &&
        (process.env.FISHMMO_WEBGPU_BRG===undefined || sample.brgActive===(process.env.FISHMMO_WEBGPU_BRG==='1')) && (!resolution || (sample.width===resolution[0] && sample.height===resolution[1])) && state.visibility==='visible' && state.monitor?.device==='created' && state.monitor.gpuErrors===0 &&
        state.monitor.runtimeErrors===0 && sample.errors===0 && !state.traceEnabled && sample.clouds &&
        sample.grass>0 && sample.scatter>0 && sample.worldSeconds>sample.worldSecondsStart && sample.frameMs.every(ms=>Number.isFinite(ms)&&ms>0)};
    fs.writeFileSync(path.join(output,view.name+'.json'),JSON.stringify(row,null,2)+'\n');
    const shot = await call('Page.captureScreenshot',{format:'png'});
    fs.writeFileSync(path.join(output,view.name+'.png'),Buffer.from(shot.data,'base64'));
    if (cpu) {
      await call('Profiler.enable'); await call('Profiler.setSamplingInterval',{interval:1000});
      await call('Profiler.start'); await pause(seconds*1000);
      const result = await call('Profiler.stop'); await call('Profiler.disable');
      fs.writeFileSync(path.join(output,view.name+'.cpuprofile'),JSON.stringify(result.profile));
    }
    summary.push({...row,frameMs:undefined});
    fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2)+'\n');
    console.log(JSON.stringify(summary.at(-1)));
  }
  if (summary.some(row=>!row.valid)) process.exitCode=2;
} finally {socket.close()}
