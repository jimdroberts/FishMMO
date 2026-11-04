#!/usr/bin/env node
// Capture only the isolated local Brave test profile, using its optional loopback DevTools port.
import fs from 'node:fs';
import path from 'node:path';
// Loopback origin of the owned test page (default 8090; another port serves a second player beside it).
const origin=(process.env.FISHMMO_WEBGPU_ORIGIN||'http://127.0.0.1:8090');
const port=Number(process.env.FISHMMO_WEBGPU_DEBUG_PORT||9229);
const output=path.resolve(process.argv[2]||'Library/WebGpuSmoke/browser-run');
const seconds=Number(process.argv[3]||180);
fs.mkdirSync(output,{recursive:true});
class CDP {
  constructor(url){this.ws=new WebSocket(url);this.next=1;this.pending=new Map();this.listeners=[];this.open=new Promise((r,j)=>{this.ws.onopen=r;this.ws.onerror=j});this.ws.onmessage=e=>{let m=JSON.parse(e.data);if(m.id){let p=this.pending.get(m.id);if(p){this.pending.delete(m.id);clearTimeout(p.timer);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}}else for(let f of this.listeners)f(m)}}
  async call(method,params={}){await this.open;let id=this.next++;return await new Promise((resolve,reject)=>{let timer=setTimeout(()=>{this.pending.delete(id);reject(Error('DevTools timeout: '+method))},120000);this.pending.set(id,{resolve,reject,timer});this.ws.send(JSON.stringify({id,method,params}))})}
}
const version=await(await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser=new CDP(version.webSocketDebuggerUrl);
fs.writeFileSync(path.join(output,'browser.json'),JSON.stringify(version,null,2));
fs.writeFileSync(path.join(output,'gpu.json'),JSON.stringify(await browser.call('SystemInfo.getInfo'),null,2));browser.ws.close();
const tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const tab=tabs.find(t=>t.type==='page'&&t.url.startsWith(origin+'/'));
if(!tab)throw Error('No isolated localhost test tab');
const cdp=new CDP(tab.webSocketDebuggerUrl);let logged=0,printedErrors=0;
cdp.listeners.push(e=>{if(e.method==='Page.javascriptDialogOpening'){fs.appendFileSync(path.join(output,'dialogs.jsonl'),JSON.stringify(e)+'\n');console.log('Startup dialog: '+e.params.message);cdp.call('Page.handleJavaScriptDialog',{accept:true}).catch(console.error)}if(['Runtime.consoleAPICalled','Runtime.exceptionThrown','Inspector.targetCrashed','Log.entryAdded'].includes(e.method)){if(logged++<12000)fs.appendFileSync(path.join(output,'console.jsonl'),JSON.stringify(e)+'\n');if(e.method==='Runtime.consoleAPICalled'){let text=e.params.args.map(a=>a.value??a.description??'').join(' ');if(text.includes('[WebGPU Smoke]')||(e.params.type==='error'&&printedErrors++<8))console.log(text.slice(0,600))}}});
await cdp.call('Runtime.enable');await cdp.call('Log.enable');await cdp.call('Page.enable');
await cdp.call('Emulation.setFocusEmulationEnabled',{enabled:true});
if(!process.env.FISHMMO_WEBGPU_ATTACH){await cdp.call('Runtime.discardConsoleEntries');fs.writeFileSync(path.join(output,'console.jsonl'),'');logged=0;printedErrors=0;}
if(process.env.FISHMMO_WEBGPU_DEBUG){
 await cdp.call('Debugger.enable');
 cdp.listeners.push(e=>{if(e.method==='Debugger.paused'){fs.writeFileSync(path.join(output,'paused-stack.json'),JSON.stringify(e.params,null,2));console.log('Paused stack: '+e.params.callFrames.slice(0,12).map(f=>f.functionName).join(' > '));cdp.call('Debugger.resume').catch(console.error)}});
 setTimeout(()=>cdp.call('Debugger.pause').catch(console.error),45000);
}

await cdp.call('Page.addScriptToEvaluateOnNewDocument',{source:`
window.webgpuTest={devices:[],errors:[],lost:[],pipelineFailures:[]};
const shaderSources=new WeakMap(),pipelineSources=new Map();let pipelineId=0;
function rememberFailure(label,message){const p=pipelineSources.get(label);if(p&&webgpuTest.pipelineFailures.length<16)webgpuTest.pipelineFailures.push({...p,message})}
function describePipeline(desc){if(!desc.label)desc.label='smoke-pipeline-'+(++pipelineId);pipelineSources.set(desc.label,{label:desc.label,targets:desc.fragment?.targets,entry:desc.fragment?.entryPoint,vertex:shaderSources.get(desc.vertex?.module),fragment:shaderSources.get(desc.fragment?.module)});if(pipelineSources.size>1024)pipelineSources.delete(pipelineSources.keys().next().value);return desc.label}
const originalError=console.error;console.error=function(...args){let text=args.map(String).join(' ');for(let match of text.matchAll(/smoke-pipeline-[0-9]+/g))rememberFailure(match[0],text);return originalError.apply(this,args)};
if(globalThis.GPUDevice){
 let shader=GPUDevice.prototype.createShaderModule;GPUDevice.prototype.createShaderModule=function(desc){let m=shader.call(this,desc);shaderSources.set(m,desc.code);return m};
 let pipeline=GPUDevice.prototype.createRenderPipeline;GPUDevice.prototype.createRenderPipeline=function(desc){describePipeline(desc);return pipeline.call(this,desc)};
 let asyncPipeline=GPUDevice.prototype.createRenderPipelineAsync;GPUDevice.prototype.createRenderPipelineAsync=function(desc){let label=describePipeline(desc);return asyncPipeline.call(this,desc).catch(e=>{rememberFailure(label,String(e.message||e));throw e})};
}
if(globalThis.GPUAdapter){let original=GPUAdapter.prototype.requestDevice;GPUAdapter.prototype.requestDevice=async function(...args){let d=await original.apply(this,args);webgpuTest.devices.push({features:[...d.features],limits:{uniforms:d.limits.maxUniformBuffersPerShaderStage,dynamicUniforms:d.limits.maxDynamicUniformBuffersPerPipelineLayout,storage:d.limits.maxStorageBuffersPerShaderStage,workgroup:d.limits.maxComputeInvocationsPerWorkgroup}});d.addEventListener('uncapturederror',e=>{let message=String(e.error?.message||e.error);if(webgpuTest.errors.length<500)webgpuTest.errors.push(message);for(let label of pipelineSources.keys())if(message.includes(label))rememberFailure(label,message);console.error('[WebGPU validation]',message)});d.lost.then(x=>{webgpuTest.lost.push({reason:x.reason,message:x.message});console.error('[WebGPU device lost]',x.reason,x.message)});return d}}
`});
// Test the real fallback device configuration by withholding this optional feature.
// Optional experiment: the device's maxDynamicUniformBuffersPerPipelineLayout. Unity's WebGPU backend gives a pipeline's
// uniform buffers past this many a dedicated buffer per update instead of a dynamic offset into its scratch pages
// (Docs/Rendering/2026-11-01/webgpu-constants). Clamped by the browser to the adapter's own limit.
if(process.env.FISHMMO_WEBGPU_DYNAMIC_UNIFORMS)await cdp.call('Page.addScriptToEvaluateOnNewDocument',{source:`
if(globalThis.GPUAdapter){const request=GPUAdapter.prototype.requestDevice;
GPUAdapter.prototype.requestDevice=function(desc={}){return request.call(this,{...desc,requiredLimits:{...(desc.requiredLimits||{}),maxDynamicUniformBuffersPerPipelineLayout:${Number(process.env.FISHMMO_WEBGPU_DYNAMIC_UNIFORMS)}}})}}`});
if(process.env.FISHMMO_WEBGPU_NO_FIRST_INSTANCE)await cdp.call('Page.addScriptToEvaluateOnNewDocument',{source:`
if(globalThis.GPUAdapter){const request=GPUAdapter.prototype.requestDevice;
GPUAdapter.prototype.requestDevice=function(desc={}){return request.call(this,{...desc,requiredFeatures:[...(desc.requiredFeatures||[])].filter(f=>f!=='indirect-first-instance')})}}
`});
if(process.env.FISHMMO_WEBGPU_TRACE)await cdp.call('Page.addScriptToEvaluateOnNewDocument',{source:`
window.webgpuTrace={hazards:[]};const views=new WeakMap(),groups=new WeakMap(),passes=new WeakMap(),pipelines=new WeakMap();
const cv=GPUTexture.prototype.createView;GPUTexture.prototype.createView=function(desc){let v=cv.call(this,desc);views.set(v,{texture:this,label:this.label,width:this.width,height:this.height,format:this.format,mip:desc?.baseMipLevel||0});return v};
const bg=GPUDevice.prototype.createBindGroup;GPUDevice.prototype.createBindGroup=function(desc){let g=bg.call(this,desc);groups.set(g,[...desc.entries].filter(e=>views.has(e.resource)).map(e=>({binding:e.binding,...views.get(e.resource)})));return g};
const cp=GPUDevice.prototype.createRenderPipeline;GPUDevice.prototype.createRenderPipeline=function(desc){let p=cp.call(this,desc);pipelines.set(p,{name:desc.label,fragment:shaderSources.get(desc.fragment?.module),vertex:shaderSources.get(desc.vertex?.module)});return p};
const ca=GPUDevice.prototype.createRenderPipelineAsync;GPUDevice.prototype.createRenderPipelineAsync=function(desc){return ca.call(this,desc).then(p=>{pipelines.set(p,{name:desc.label,fragment:shaderSources.get(desc.fragment?.module),vertex:shaderSources.get(desc.vertex?.module)});return p})};
const begin=GPUCommandEncoder.prototype.beginRenderPass;GPUCommandEncoder.prototype.beginRenderPass=function(desc){let p=begin.call(this,desc);passes.set(p,{label:desc.label,targets:[...desc.colorAttachments].filter(Boolean).flatMap(a=>[views.get(a.view),views.get(a.resolveTarget)]).filter(Boolean),groups:new Map(),debug:[]});return p};
function check(pass,method){let p=passes.get(pass);if(!p||webgpuTrace.hazards.length>=12)return;for(let [group,list]of p.groups)for(let e of list)if(p.targets.some(t=>t.texture===e.texture&&t.mip===e.mip)){let code=p.pipeline?.fragment||'',lines=code.split(String.fromCharCode(10)),line=lines.find(x=>x.includes('@group('+group+') @binding('+e.binding+')')),name=line?.split(' var ')[1]?.split(' :')[0];webgpuTrace.hazards.push({method,pass:p.label,pipeline:p.pipeline?.name,shader:lines[0],binding:e.binding,group,name,width:e.width,height:e.height,format:e.format,debug:p.debug.slice()})}}

const bind=GPURenderPassEncoder.prototype.setBindGroup;GPURenderPassEncoder.prototype.setBindGroup=function(index,g,...rest){let p=passes.get(this);if(p)p.groups.set(index,groups.get(g)||[]);let r=bind.call(this,index,g,...rest);check(this,'setBindGroup');return r};
const pipe=GPURenderPassEncoder.prototype.setPipeline;GPURenderPassEncoder.prototype.setPipeline=function(p){let s=passes.get(this);if(s)s.pipeline=pipelines.get(p);return pipe.call(this,p)};
for(let method of ['draw','drawIndexed','drawIndirect','drawIndexedIndirect']){let original=GPURenderPassEncoder.prototype[method];GPURenderPassEncoder.prototype[method]=function(...args){check(this,method);return original.apply(this,args)}}
for(let method of ['pushDebugGroup','popDebugGroup']){let original=GPURenderPassEncoder.prototype[method];GPURenderPassEncoder.prototype[method]=function(...args){let p=passes.get(this);if(p){if(method==='pushDebugGroup')p.debug.push(args[0]);else p.debug.pop()}return original.apply(this,args)}}
`});
if(!process.env.FISHMMO_WEBGPU_ATTACH)await cdp.call('Page.navigate',{url:origin+'/'});
for(let elapsed=0;elapsed<seconds;elapsed+=15){await new Promise(r=>setTimeout(r,15000));let r=await cdp.call('Runtime.evaluate',{expression:'JSON.stringify({title:document.title,canvas:[...document.querySelectorAll("canvas")].map(c=>({width:c.width,height:c.height})),webgpu:webgpuTest,monitor:window.fishWebGpuTestStatus,trace:window.webgpuTrace,body:document.body.innerText.slice(-1500)})',returnByValue:true});fs.writeFileSync(path.join(output,'status.json'),r.result.value||'{}');console.log('Elapsed '+(elapsed+15)+' seconds; captured '+logged+' events')}
const shot=await cdp.call('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,'browser.png'),Buffer.from(shot.data,'base64'));
cdp.ws.close();console.log('Browser capture: '+output);
