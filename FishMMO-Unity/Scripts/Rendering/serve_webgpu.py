#!/usr/bin/env python3
"""Loopback-only Unity Web test server with WASM types and cross-origin isolation headers."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.data': 'application/octet-stream'}
    def end_headers(self):
        self.send_header('Cross-Origin-Opener-Policy', 'same-origin')
        self.send_header('Cross-Origin-Embedder-Policy', 'require-corp')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()
    def do_GET(self):
        if self.path == '/__gpu':
            body=b'<!doctype html><title>Local WebGPU adapter test</title><h1>Local WebGPU adapter test</h1><pre id="result">Inspecting adapter...</pre><script>window.gpuProbe=(async()=>{try{if(!navigator.gpu)throw Error("navigator.gpu unavailable");let a=await navigator.gpu.requestAdapter({powerPreference:"high-performance"});if(!a)throw Error("No adapter");let d=await a.requestDevice();let r={ok:true,secure:isSecureContext,isolated:crossOriginIsolated,info:a.info,features:[...a.features],limits:{maxBufferSize:a.limits.maxBufferSize,maxStorageBuffersPerShaderStage:a.limits.maxStorageBuffersPerShaderStage,maxComputeInvocationsPerWorkgroup:a.limits.maxComputeInvocationsPerWorkgroup,maxTextureArrayLayers:a.limits.maxTextureArrayLayers}};document.querySelector("pre").textContent=JSON.stringify(r,null,2);d.destroy();return r}catch(e){let r={ok:false,error:String(e),secure:isSecureContext};document.querySelector("pre").textContent=JSON.stringify(r);return r}})();</script>'
            self.send_response(200);self.send_header('Content-Type','text/html');self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
        else: super().do_GET()

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,required=True);p.add_argument('--port',type=int,default=8090);a=p.parse_args()
    root=a.root.resolve()
    if not root.is_dir():p.error('Build root does not exist')
    print(f'Serving {root} at http://127.0.0.1:{a.port}',flush=True)
    ThreadingHTTPServer(('127.0.0.1',a.port),partial(Handler,directory=str(root))).serve_forever()
if __name__=='__main__':main()
