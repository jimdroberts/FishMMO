// Local test-page diagnostics. GPU validation failures are separate from Unity's managed log error count.
(() => {
  const state = window.fishWebGpuTestStatus = { device: 'waiting', gpuErrors: 0, runtimeErrors: 0, firstError: '' };
  let panel;
  function show() {
    if (!panel) return;
    const failed = state.gpuErrors || state.runtimeErrors || state.device === 'lost';
    panel.style.background = failed ? '#8b1515' : '#153b4e';
    panel.textContent = `WebGPU test — device ${state.device} | GPU errors ${state.gpuErrors} | JS errors ${state.runtimeErrors}` + (state.firstError ? `\n${state.firstError}` : '\nGPU validation is reported separately from the Unity overlay.');
  }
  function fail(message, gpu) {
    if (gpu) state.gpuErrors++; else state.runtimeErrors++;
    if (!state.firstError) state.firstError = String(message).split('\n')[0].slice(0,280);
    show();
  }
  addEventListener('DOMContentLoaded', () => {
    panel = document.createElement('div');
    panel.style.cssText = 'position:fixed;right:12px;top:12px;max-width:560px;padding:10px;color:white;font:13px monospace;white-space:pre-wrap;z-index:10000;pointer-events:none';
    document.body.appendChild(panel); show();
  });
  addEventListener('error', e => fail(e.message || 'Script error',false));
  addEventListener('unhandledrejection', e => fail(e.reason?.message || e.reason,false));
  const error = console.error;
  console.error = function(...args) {
    const message = args.map(String).join(' ');
    if (message.includes('CreateRenderPipeline Validation Error')) fail(message,true);
    return error.apply(this,args);
  };
  if (globalThis.GPUAdapter) {
    const request = GPUAdapter.prototype.requestDevice;
    GPUAdapter.prototype.requestDevice = async function(...args) {
      const device = await request.apply(this,args);
      state.device = 'created'; show();
      device.addEventListener('uncapturederror', e => fail(e.error?.message || e.error,true));
      device.lost.then(info => { state.device = info.reason === 'destroyed' ? 'destroyed' : 'lost'; if(state.device==='lost')fail(info.message,true); show(); });
      return device;
    };
  } else { state.device = 'unavailable'; }
})();
