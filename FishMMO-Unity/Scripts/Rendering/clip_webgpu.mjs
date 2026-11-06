#!/usr/bin/env node
// A short clip of the WebGPU player for sharing: the camera panned slowly at a named view while the world runs, recorded with
// the browser's screencast, then encoded by ffmpeg (an animated WebP by default) under a size cap.
// Usage: node clip_webgpu.mjs <out-file.webp> [view] [seconds]
//   view: forest|forest-down|settlement|settlement-close (as ab_webgpu_images.mjs); seconds of motion (default 10).
//   FISHMMO_WEBGPU_ORIGIN (http://127.0.0.1:8090), FISHMMO_WEBGPU_DEBUG_PORT (9229), FISHMMO_CLIP_PAN_DEGREES (40),
//   FISHMMO_CLIP_WIDTH (1280), FISHMMO_CLIP_FPS (20), FISHMMO_CLIP_MAX_MB (12), FISHMMO_WEBGPU_SETTINGS ("name=value;…"),
//   FISHMMO_CLIP_TIME (a fraction of the day, as SetTime takes: 0.5 noon, 0 midnight; unset: the clock as it is),
//   FISHMMO_CLIP_CROP ("x,y,w,h" of the 2560×1440 page, e.g. right of the World Sim panel; unset: the whole page).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const [outFile, view = 'forest', secondsArg = '10'] = process.argv.slice(2);
if (!outFile) throw Error('Usage: clip_webgpu.mjs <out-file.webp> [view] [seconds]');
const seconds = Number(secondsArg);
const origin = process.env.FISHMMO_WEBGPU_ORIGIN || 'http://127.0.0.1:8090';
const port = Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT || 9229);
const panDegrees = Number(process.env.FISHMMO_CLIP_PAN_DEGREES || 40);
const width = Number(process.env.FISHMMO_CLIP_WIDTH || 1280);
const fps = Number(process.env.FISHMMO_CLIP_FPS || 20);
const maxBytes = Number(process.env.FISHMMO_CLIP_MAX_MB || 12) * 1024 * 1024;
const views = {
  forest:[-1082.901245,513.39331,429.819427,-1082.901245,513.39331,445.819427],
  settlement:[1970.555176,191.37265,-1600.95056,1850.555176,126.37264,-1480.95056],
  'forest-down':[-1082.901245,513.39331,429.819427,-1082.901245,503.39331,445.819427],
  'settlement-close':[1885.555176,144.37264,-1515.95056,1850.555176,126.37264,-1480.95056],
};
if (!views[view]) throw Error('Unknown view ' + view);

const tabs = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab = tabs.find(t => t.type === 'page' && t.url === origin + '/');
if (!tab) throw Error('No isolated localhost WebGPU test tab');
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
let next = 0; const pending = new Map();
const frames = [];
const frameDir = fs.mkdtempSync(path.join(os.tmpdir(), 'fish-clip-'));
let recording = false, recordingSince = 0;
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.method === 'Page.screencastFrame') {
    ws.send(JSON.stringify({ id: ++next, method: 'Page.screencastFrameAck', params: { sessionId: m.params.sessionId } }));
    if (recording && Date.now() - recordingSince > 500) {
      const file = path.join(frameDir, `f${String(frames.length).padStart(5, '0')}.jpg`);
      fs.writeFileSync(file, Buffer.from(m.params.data, 'base64'));
      frames.push({ file, t: m.params.metadata.timestamp });
    }
    return;
  }
  const p = pending.get(m.id);
  if (p) { clearTimeout(p.timer); pending.delete(m.id); m.error ? p.reject(Error(JSON.stringify(m.error))) : p.resolve(m.result); }
};
const call = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++next, timer = setTimeout(() => { pending.delete(id); reject(Error('DevTools timeout: ' + method)); }, 120000);
  pending.set(id, { resolve, reject, timer }); ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async expression => { const r = await call('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }); if (r.exceptionDetails) throw Error(JSON.stringify(r.exceptionDetails)); return r.result.value; };
const send = (method, arg) => evaluate(`fishWebGpuInstance.SendMessage('WebGPU Rendering Test',${JSON.stringify(method)},${JSON.stringify(arg)})`);
const pause = ms => new Promise(r => setTimeout(r, ms));

// The view's eye, and its target turned about the vertical by an angle (degrees).
const [ex, ey, ez, tx, ty, tz] = views[view];
const turned = degrees => {
  const a = degrees * Math.PI / 180, dx = tx - ex, dz = tz - ez;
  return [ex, ey, ez, ex + dx * Math.cos(a) + dz * Math.sin(a), ty, ez - dx * Math.sin(a) + dz * Math.cos(a)].join(',');
};

try {
  await call('Emulation.setFocusEmulationEnabled', { enabled: true });
  await call('Emulation.setDeviceMetricsOverride', { width: 2560, height: 1440, deviceScaleFactor: 1, mobile: false });
  await evaluate(`(()=>{const c=document.querySelector('canvas');c.style.width='2560px';c.style.height='1440px';c.width=2560;c.height=1440;dispatchEvent(new Event('resize'));})()`);
  for (const s of (process.env.FISHMMO_WEBGPU_SETTINGS || '').split(';').filter(Boolean)) await send('SetSetting', s);
  // The page's own status line (HTML over the canvas) out of the picture.
  await evaluate(`[...document.querySelectorAll('div')].filter(d=>!d.querySelector('canvas')&&d.id!=='unity-container'&&/WebGPU test/.test(d.innerText||'')).forEach(d=>d.style.display='none')`);
  await send('SetSetting', 'paused=0');
  if (process.env.FISHMMO_CLIP_TIME) await send('SetTime', process.env.FISHMMO_CLIP_TIME);
  await send('SetView', turned(-panDegrees / 2));
  await pause(15000);
  await call('Page.startScreencast', { format: 'jpeg', quality: 92, maxWidth: 2560, maxHeight: 1440, everyNthFrame: 1 });
  recording = true;
  recordingSince = Date.now();
  const start = Date.now();
  while (Date.now() - start < seconds * 1000) {
    const k = (Date.now() - start) / (seconds * 1000);
    // Eased: the pan starts and ends at rest.
    const eased = 0.5 - 0.5 * Math.cos(Math.PI * k);
    await send('SetView', turned(-panDegrees / 2 + panDegrees * eased));
    await pause(30);
  }
  recording = false;
  await call('Page.stopScreencast');
} finally {
  // The canvas back to the page's own size (it fills the window), so the tab is usable at its real size afterwards.
  await evaluate(`(()=>{const c=document.querySelector('canvas');if(c){c.style.width='';c.style.height='';}})()`).catch(()=>{}); await call('Emulation.clearDeviceMetricsOverride').catch(()=>{}); ws.close();
}

if (frames.length < 2) throw Error('No frames recorded');
// The frames at their own times (concat durations), resampled to a steady rate and scaled, then a size cap by quality.
const list = path.join(frameDir, 'frames.txt');
fs.writeFileSync(list, frames.map((f, i) => `file '${f.file}'\nduration ${Math.max(0.001, (frames[Math.min(i + 1, frames.length - 1)].t - f.t) || 1 / fps).toFixed(4)}`).join('\n') + `\nfile '${frames[frames.length - 1].file}'\n`);
const c = (process.env.FISHMMO_CLIP_CROP || '').split(',').map(Number);
const crop = c.length === 4 ? `crop=${c[2]}:${c[3]}:${c[0]}:${c[1]},` : '';
let written = 0;
for (const quality of (process.env.FISHMMO_CLIP_QUALITIES || '70,60,50,42,35').split(',').map(Number)) {
  execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', list,
    '-vf', `${crop}fps=${fps},scale=${width}:-2:flags=lanczos`, '-c:v', 'libwebp_anim', '-lossless', '0', '-q:v', String(quality),
    '-compression_level', process.env.FISHMMO_CLIP_EFFORT || '6', '-loop', '0', outFile]);
  written = fs.statSync(outFile).size;
  console.log(JSON.stringify({ quality, bytes: written, frames: frames.length, seconds }));
  if (written <= maxBytes) break;
}
fs.rmSync(frameDir, { recursive: true, force: true });
if (written > maxBytes) throw Error(`Clip is ${written} bytes, over the cap`);
