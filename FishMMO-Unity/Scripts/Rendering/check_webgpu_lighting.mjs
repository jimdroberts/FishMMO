#!/usr/bin/env node
// Exercise night and point-light shadow shaders in the isolated browser, then restore daylight.
import fs from 'node:fs';
// Loopback origin of the owned test page (default 8090; another port serves a second player beside it).
const origin=(process.env.FISHMMO_WEBGPU_ORIGIN||'http://127.0.0.1:8090');
const output=process.argv[2]||'Library/WebGpuPerformance/lighting';
const port=Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT||9229);
fs.mkdirSync(output,{recursive:true});
const tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();const tab=tabs.find(t=>t.url===origin+'/');if(!tab)throw Error('Missing owned test tab');
const ws=new WebSocket(tab.webSocketDebuggerUrl);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j});const samples=[];let next=0;const pending=new Map();
ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);if(p){clearTimeout(p.timer);pending.delete(m.id);m.error?p.j(Error(JSON.stringify(m.error))):p.r(m.result)}}else if(m.method==='Runtime.consoleAPICalled'){fs.appendFileSync(output+'/console.jsonl',JSON.stringify(m)+'\n');const text=m.params.args.map(a=>a.value||'').join(' ');if(text.startsWith('[WebGPU Performance] '))samples.push(JSON.parse(text.slice('[WebGPU Performance] '.length)))}};
const call=(method,params={})=>new Promise((r,j)=>{const id=++next,timer=setTimeout(()=>j(Error('CDP timeout')),60000);pending.set(id,{r,j,timer});ws.send(JSON.stringify({id,method,params}))});
const evaluate=async expression=>{const r=await call('Runtime.evaluate',{expression,returnByValue:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value};
const send=(method,arg)=>evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test',${JSON.stringify(method)},${JSON.stringify(arg)})`);
const pause=ms=>new Promise(r=>setTimeout(r,ms));
try {
 await call('Runtime.enable');samples.length=0;await call('Page.enable');await call('Page.bringToFront');await call('Emulation.setFocusEmulationEnabled',{enabled:true});
 await send('SetView','-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427');
 await send('SetTime','0.85');await send('SetTestLight','0');await pause(20000);
 for(const enabled of ['0','1']) {
  await send('SetTestLight',enabled);await pause(10000);
  await send('BeginSample','night-light-'+enabled);await pause(10000);await send('EndSample','night-light-'+enabled);
  const shot=await call('Page.captureScreenshot',{format:'png'});fs.writeFileSync(output+'/night-light-'+enabled+'.png',Buffer.from(shot.data,'base64'));
 }
 const state=JSON.parse(await evaluate('JSON.stringify({monitor:fishWebGpuTestStatus,errors:window.webgpuTest?.errors||[],lost:window.webgpuTest?.lost||[],visibility:document.visibilityState})'));
 state.samples=samples.filter(s=>s.name.startsWith('night-light-')).map(({frameMs,...sample})=>sample);
 state.valid=state.samples.length===2&&state.samples.every(s=>s.errors===0&&s.frames>=2&&s.clouds&&s.worldSeconds>s.worldSecondsStart)&&state.visibility==='visible'&&!state.monitor.gpuErrors&&!state.monitor.runtimeErrors&&!state.errors.length&&!state.lost.length;
 fs.writeFileSync(output+'/status.json',JSON.stringify(state,null,2));console.log(state);
 if(!state.valid)process.exitCode=2;
}finally{try{await send('SetTestLight','0');await send('SetTime','0.5')}finally{ws.close()}}
