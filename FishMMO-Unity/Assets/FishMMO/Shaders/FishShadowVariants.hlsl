#ifndef FISHMMO_SHADOW_VARIANTS_INCLUDED
#define FISHMMO_SHADOW_VARIANTS_INCLUDED
// SampleShadowmap's uniform path handles hard/low/medium/high using each light's shadowParams.y.
// The supported clients use per-light quality; no separate filtering keyword programs are needed.
#define _SHADOWS_SOFT 1

// WebGPU: URP's lighting arrays as plain globals instead of their own constant buffers. URP's own switch (its GLES3 path):
// LightShadows, LightCookies, AdditionalLights and urp_ReflectionProbeBuffer stop being cbuffers and their members join the
// program's $Globals, filled from the same global properties URP sets every frame (it binds none of the four by buffer), so
// every value is unchanged. Unity's WebGPU backend gives only the first maxDynamicUniformBuffersPerPipelineLayout (10) uniform
// bindings of a pipeline a dynamic offset; every later one is re-created as a dedicated buffer on each pass application, which
// cost the lit/vegetation forward pipelines (11-13 bindings) most of their CPU time (Docs/Rendering/2026-11-02/
// webgpu-dynamic-uniforms). Every lit forward pass includes this file before URP's Core.hlsl (WebGpuRenderingCompatibilityTests).
#if defined(SHADER_API_WEBGPU) && !defined(LIGHT_SHADOWS_NO_CBUFFER)
#define LIGHT_SHADOWS_NO_CBUFFER
#endif
#endif
