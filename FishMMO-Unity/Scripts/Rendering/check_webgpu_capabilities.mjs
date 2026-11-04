#!/usr/bin/env node
import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const source = fs.readFileSync('Assets/Scripts/Client/WebGL/WebGpuCapabilities.jslib','utf8');
const cases = [
  ['no WebGPU module',{},0],
  ['no device',{WebGPU:{}},0],
  ['adapter support alone',{WebGPU:{adapter:{features:new Set(['indirect-first-instance'])},device:{features:new Set()}}},0],
  ['device feature disabled',{WebGPU:{device:{features:new Set(['timestamp-query'])}}},0],
  ['device feature enabled',{WebGPU:{device:{features:new Set(['indirect-first-instance'])}}},1],
];
for (const [name,module,expected] of cases) {
  const context = {Module:module,LibraryManager:{library:{}},mergeInto:Object.assign};
  vm.runInNewContext(source,context);
  assert.equal(context.LibraryManager.library.FishMMOWebGpuHasIndirectFirstInstance(),expected,name);
}
console.log(`${cases.length} WebGPU device-feature bridge cases passed`);
