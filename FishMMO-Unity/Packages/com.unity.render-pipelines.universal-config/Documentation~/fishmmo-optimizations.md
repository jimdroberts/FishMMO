# FishMMO shader build optimization

URP 17.4.0 supplies `ShaderOptions.k_UseDynamicBranchFogKeyword`. This local
configuration package enables that supported option in both the C# constant and
its generated HLSL definition. Keep those two values synchronized when upgrading
URP or regenerating shader includes. The rest of the configuration is unchanged.

Fog off, linear, exponential and exponential-squared remain available. URP's fog
include declares uniform dynamic keywords instead of four separately compiled
program variants. Do not remove the custom GraphicsSettings fog retention flags:
other shaders can still use static fog and region/weather fog changes at runtime.

Browser builds use WebGPU only, set in PlayerSettings and enforced before both
Addressables and player builds by BuildConfigurator. CleanBuildCache remains on
for player builds. Addressables/SBP/native shader caches retain their independent
lifecycle. These changes invalidate affected old shader programs once; subsequent
unchanged builds should reuse native cache entries.

Editor ShaderPrewarm ignores dynamic keywords when recording/requesting compiled
programs and leaves their current uniform state untouched. This prevents fog modes
from multiplying editor warm requests after enabling dynamic fog.

Validation: a small isolated project uses these actual shaders, learned keyword
sets, all three quality assets and stock SSAO. Project-specific compositing renderer
features are omitted from the harness. Baseline and optimized inventories use the
same input and normal URP stripping. Measured ForwardLit variants per WebGPU API:

| Shader | Before | Dynamic fog |
|---|---:|---:|
| Weather Lit | 1440 | 360 |
| Weather Lit Indirect | 2160 | 540 |
| Vegetation | 288 | 72 |
| Vegetation Indirect | 864 | 216 |

These are representative audit counts, not a full shipping build inventory. The
current full build's static-fog WebGPU counts (3456 Weather Lit, 3888 indirect)
project to 864 and 972 with otherwise unchanged coverage. That projection must be
verified by a full build; it is not a measured end-to-end build-time guarantee.

Run a bounded shader compilation/cache audit in the full project, with Unity closed:

    Unity -batchmode -nographics -buildTarget WebGL -standaloneBuildSubtarget Player \
      -projectPath <FishMMO-Unity> \
      -executeMethod FishMMO.Shared.CustomBuildTool.Core.ClientShaderBuildAudit.RunCLI \
      -fishmmoShaderAuditOutput <report-directory> -logFile <log-path>

Add `-fishmmoShaderAuditInventoryOnly` to inventory normal stripped variant counts
without compiling shader programs. The report records `inventoryOnly` and scope
so this mode cannot be mistaken for shader compilation/cache verification.

The setting changes shader compilation coverage, not the weather/material/lighting
features in the game. Uniform fog branching adds a small runtime branch; compare
GPU timings and fog transitions in actual WebGPU gameplay on supported devices.
