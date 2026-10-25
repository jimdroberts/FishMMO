const { chromium } = require(process.env.FISHMMO_PLAYWRIGHT_MODULE || 'playwright');
const { spawn } = require('child_process');
const fs = require('fs');
const net = require('net');
const path = require('path');
const root = path.resolve(__dirname, '../../..');
const artifacts = process.env.FISHMMO_CSP_ARTIFACTS || fs.mkdtempSync(path.join(require('os').tmpdir(), 'fishmmo-csp-browser-'));
fs.mkdirSync(artifacts, {recursive:true});
console.log('Artifacts: '+artifacts);
(async () => {
  const port = await new Promise(resolve => { const s = net.createServer(); s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p));}); });
  const binary=process.env.FISHMMO_WEBGL_SERVER || path.join(root,'Builds/WebGLServer/Debug/net8.0/WebGLServer');
  const clientRoot=process.env.FISHMMO_WEBGL_ROOT || path.join(root,'Builds/Client/WebGL Demo/FishMMO WebGL/FishMMO.exe');
  if (!fs.existsSync(binary) || !fs.existsSync(path.join(clientRoot,'index.html')))
    throw new Error('Set FISHMMO_WEBGL_SERVER and FISHMMO_WEBGL_ROOT to the server executable and client directory containing index.html.');
  const log=fs.openSync(path.join(artifacts,'server.log'),'w');
  const server=spawn(binary,[],{cwd:require('path').dirname(binary),env:{...process.env,FISHMMO_ENVIRONMENT:'Development',WebServer__HttpPort:String(port),ASPNETCORE_WEBROOT:clientRoot},stdio:['ignore',log,log]});
  let browser;
  const result={violations:[],errors:[],failedRequests:[],workers:[],unity:'not-started',resources:[]};
  const safeUrl = raw => { try { const u=new URL(raw);return u.protocol==='blob:'?'blob:':u.origin+u.pathname; } catch {return String(raw).slice(0,120);} };
  try {
    const url=`http://localhost:${port}/`;
    for(let i=0;i<100;i++){try{if((await fetch(url)).ok)break;}catch{};await new Promise(r=>setTimeout(r,100));}
    browser=await chromium.launch({executablePath:process.env.FISHMMO_CHROMIUM_EXECUTABLE || undefined,headless:true,args:['--no-sandbox','--enable-unsafe-webgpu','--enable-unsafe-swiftshader','--use-angle=swiftshader'],timeout:30000});
    const context=await browser.newContext();
    const page=await context.newPage();
    await page.exposeFunction('recordViolation',v=>result.violations.push({...v,blocked:safeUrl(v.blocked),source:safeUrl(v.source)}));
    await page.exposeFunction('recordUnityState',state=>result.unity=state);
    await page.addInitScript(()=>{
      window.addEventListener('securitypolicyviolation',e=>window.recordViolation({directive:e.effectiveDirective,blocked:e.blockedURI,source:e.sourceFile,line:e.lineNumber}));
      // Capture the loader's load event before the template's onload handler calls Unity.
      document.addEventListener('load', event => {
        if (event.target?.tagName !== 'SCRIPT' || !event.target.src.endsWith('.loader.js')) return;
        const original = window.createUnityInstance;
        if (typeof original !== 'function') return;
        window.createUnityInstance = (...args) => {
          window.recordUnityState('starting');
          return original(...args).then(instance => {
            window.recordUnityState('ready');
            return instance;
          }, error => {
            window.recordUnityState('failed: ' + String(error).slice(0,200));
            throw error;
          });
        };
      }, true);
    });
    page.on('worker',worker=>result.workers.push(safeUrl(worker.url())));
    context.on('serviceworker',worker=>result.workers.push('serviceworker:'+safeUrl(worker.url())));
    page.on('pageerror',error=>result.errors.push(error.message.slice(0,250)));
    page.on('requestfailed',request=>result.failedRequests.push({url:safeUrl(request.url()),error:request.failure()?.errorText}));
    page.on('response',response=>{if(!result.resources.some(r=>r.url===safeUrl(response.url())))result.resources.push({url:safeUrl(response.url()),status:response.status()});});
    page.on('dialog',dialog=>{result.errors.push('dialog: '+dialog.message().slice(0,250));dialog.dismiss();});
    await page.goto(url,{waitUntil:'domcontentloaded',timeout:30000});
    result.features=await page.evaluate(async()=>({secureContext:isSecureContext,crossOriginIsolated,sharedArrayBuffer:typeof SharedArrayBuffer,webTransport:typeof WebTransport,webGPU:!!navigator.gpu,adapter:!!(await navigator.gpu?.requestAdapter())}));
    console.log('Browser started; features: '+JSON.stringify(result.features));
    for(let i=0;i<120;i++){
      await new Promise(r=>setTimeout(r,1000));
      if(i%20===19)console.log('Unity status after '+(i+1)+'s: '+result.unity+'; CSP violations: '+result.violations.length+'; resource responses: '+result.resources.length);
      if(result.unity==='ready'||result.unity.startsWith('failed:'))break;
    }
    await page.waitForTimeout(5000); // Allow initial scene/Addressables work after Unity's ready promise.
    result.connectionProbe = await page.evaluate(async () => {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 500);
      let transport;
      try {
        if (typeof WebTransport === 'function') {
          transport = new WebTransport('https://127.0.0.1:59999/fishmmo-csp-probe');
          transport.ready.catch(() => {});
          transport.closed.catch(() => {});
        }
        await fetch('http://localhost:8088/healthz', {signal:controller.signal}).catch(() => {});
        return {webTransportConstructed:!!transport};
      } finally { clearTimeout(timer); transport?.close(); }
    });
    await page.waitForTimeout(500);
    const beforeNegative = result.violations.length;
    await page.evaluate(() => {
      window.__untrustedInlineRan = false;
      const script = document.createElement('script');
      script.textContent = 'window.__untrustedInlineRan = true;';
      document.body.appendChild(script);
    });
    await page.waitForTimeout(200);
    result.negativeControl = {blocked:!(await page.evaluate(() => window.__untrustedInlineRan)),violations:result.violations.splice(beforeNegative)};
    await page.screenshot({path:path.join(artifacts,'client.png')});
    result.progress=await page.locator('#unity-progress-bar-full').getAttribute('style').catch(()=>null);
    result.banner=await page.locator('#unity-warning').innerText().catch(()=>null);
    result.serviceWorker=await page.evaluate(async()=>({controlled:!!navigator.serviceWorker.controller,registrations:(await navigator.serviceWorker.getRegistrations()).map(r=>({scope:r.scope,active:r.active?.state,installing:r.installing?.state}))}));
    fs.writeFileSync(path.join(artifacts,'result.json'),JSON.stringify(result,null,2));
    console.log(JSON.stringify({unity:result.unity,violations:result.violations,errors:result.errors,failedRequests:result.failedRequests,workers:result.workers,features:result.features,serviceWorker:result.serviceWorker,progress:result.progress,banner:result.banner,connectionProbe:result.connectionProbe,negativeControl:result.negativeControl},null,2));
    if(result.violations.length || result.unity !== 'ready' || !result.features.crossOriginIsolated || !result.features.adapter || result.features.sharedArrayBuffer !== 'function' || !result.negativeControl.blocked || result.negativeControl.violations.length !== 1)process.exitCode=1;
  }finally{
    if(browser)await browser.close();
    server.kill('SIGTERM');
    await new Promise(resolve=>{if(server.exitCode!==null)resolve();else{server.once('exit',resolve);setTimeout(()=>{server.kill('SIGKILL');resolve();},5000).unref();}});
    fs.closeSync(log);
  }
})().catch(e=>{console.error(String(e));process.exitCode=1;});
