#ifndef FISHMMO_CHANNEL_FLOW_INCLUDED
#define FISHMMO_CHANNEL_FLOW_INCLUDED

// Inland water as what grows in it sees it (decision 35 (a)/(k), 2026-10-18): where a lake's or a river's surface stands and how
// its current runs, round the camera. Published by the inland water (InlandWaterRenderer.Channel.cs) from the scene's
// SceneWaterBodies, on its own window, rebuilt as the camera moves:
//
//   _FishChannelFlow      RGBA half, x and y the current (m/s, world x and z), z the surface above the window's base
//                         (m), w 1 over a lake's or a river's water (0 off it). Read with loads only (no sampler).
//   _FishChannelFlowRect  xy the window's south-west corner (world x, z), z the cells per metre (0: no window — no
//                         inland water, nothing published), w the window's base height (world y).
//
// Read by the aquatic plants (FishVegetationPasses.hlsl, _Aquatic): a weed rooted under a lake or a river streams with its
// current and stays under ITS surface; before, every aquatic plant was kept under the SEA's ceiling, which would have
// flattened a lake's weeds to sea level (nothing grew under inland water until 2026-10-18, so nothing showed it).

TEXTURE2D(_FishChannelFlow);
float4 _FishChannelFlowRect;

/// <summary>The window's cells along a side: the texture's size (InlandWaterRenderer.ChannelCells).</summary>
#define FISH_CHANNEL_CELLS 128

/// <summary>
/// The inland water over a world point, bilinear between the four cells round it: xy the current (m/s), z the surface (world
/// y), w how much of the point is over water (0 … 1). All zero off the window or with none published.
/// </summary>
float4 FishChannelFlowAt(float2 positionXZ)
{
    if (_FishChannelFlowRect.z <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float2 cell = (positionXZ - _FishChannelFlowRect.xy) * _FishChannelFlowRect.z - 0.5;
    float2 base = floor(cell);
    float2 f = cell - base;
    int2 c = int2(base);
    if (any(c < 0) || any(c >= FISH_CHANNEL_CELLS - 1))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float4 a = LOAD_TEXTURE2D_LOD(_FishChannelFlow, c, 0);
    float4 b = LOAD_TEXTURE2D_LOD(_FishChannelFlow, c + int2(1, 0), 0);
    float4 d = LOAD_TEXTURE2D_LOD(_FishChannelFlow, c + int2(0, 1), 0);
    float4 e = LOAD_TEXTURE2D_LOD(_FishChannelFlow, c + int2(1, 1), 0);
    float4 v = lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
    // The surface is only meaningful where there is water: averaged over its own cells, so a bank cell's zero does not drag it.
    float wet = a.w + b.w + d.w + e.w;
    float surface = wet > 0.0
        ? (a.z * a.w * (1.0 - f.x) * (1.0 - f.y) + b.z * b.w * f.x * (1.0 - f.y) + d.z * d.w * (1.0 - f.x) * f.y + e.z * e.w * f.x * f.y)
            / max(1e-4, a.w * (1.0 - f.x) * (1.0 - f.y) + b.w * f.x * (1.0 - f.y) + d.w * (1.0 - f.x) * f.y + e.w * f.x * f.y)
        : 0.0;
    return float4(v.xy, surface + _FishChannelFlowRect.w, v.w);
}

/// <summary>
/// Keeps a point under a surface: below <paramref name="ceiling"/> less <paramref name="soft"/> metres it is left alone; above
/// that it is eased toward the ceiling and never reaches it (FishSeaKeepUnder's curve, for any water). Returns how far it was
/// lowered, so the caller can lay that length along the current instead.
/// </summary>
float FishKeepUnder(inout float3 positionWS, float ceiling, float soft)
{
    float s = max(0.05, soft);
    float start = ceiling - s;
    if (positionWS.y <= start)
    {
        return 0.0;
    }
    float y = start + s * (1.0 - exp(-(positionWS.y - start) / s));
    float lowered = positionWS.y - y;
    positionWS.y = y;
    return lowered;
}

/// <summary>
/// How a weed rooted in a river streams (decision 35 (k)): laid DOWN the current rather than rocked by the sea's surge — its
/// lean grows with the speed and saturates near 1.5 m/s, a slow wave runs along it downstream (a ribbon's undulation, about 3 s
/// at a metre a second, quicker in faster water), and it flicks a little across. In still water (a lake) it barely drifts.
/// <paramref name="weight"/> is how far a vertex goes (0 at the root); the result is a world offset, xz only.
/// </summary>
float2 FishChannelStream(float2 current, float3 positionWS, float weight, float time, float lag)
{
    float speed = length(current);
    float2 dir = speed > 1e-3 ? current / speed : float2(0.7071, 0.7071);
    float2 across = float2(-dir.y, dir.x);
    float lean = saturate(speed / 1.5);
    // Snapped to whole turns in the clock's wrap (FishWrapAngular), so the wrap is seamless.
    float rate = FishWrapAngular(1.2 + 1.6 * lean);
    float along = dot(positionWS.xz, dir);
    float wave = sin(time * rate - along * 1.7 - lag * 6.2831853);
    float drift = sin(time * FishWrapAngular(0.35) + lag * 6.2831853);
    float2 stream = dir * (lean * (0.85 + 0.15 * wave)) + across * (0.12 * lean * wave);
    // Still water: a slow drift a few centimetres each way, no lean.
    stream += (1.0 - lean) * float2(0.05 * drift, 0.04 * sin(time * FishWrapAngular(0.27) + lag * 4.1));
    return stream * weight;
}

#endif
