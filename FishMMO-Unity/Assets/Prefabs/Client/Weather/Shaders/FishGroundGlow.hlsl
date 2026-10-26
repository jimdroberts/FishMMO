#ifndef FISHMMO_GROUND_GLOW_INCLUDED
#define FISHMMO_GROUND_GLOW_INCLUDED

// A ground layer's glow (2026-10-25, phase I2r "molten ground", decision 65 (a)): per terrain layer, from its layer table texel
// (FISH_LAYER_ROW_GLOW, baked from its template slot's BiomeGroundGlow), a pure function of the shared motion clock
// (_FishWeatherMisc.w) and the place. FishMMO.Shared.Weather.GroundGlow is the C# twin, constant for constant; GroundGlowTable
// publishes the globals (client only). Presentation only.
//
// Seams (OVERTURN): crust plates (the Voronoi regions of jittered points on a grid of the layer's plate size) each overturn once a
// period at a hashed moment; a plate's skin cools as T = floor + dT / sqrt(1 + age / tau) with tau FISH_GROUND_GLOW_SKIN_TAU, the
// seam between two plates with the slower FISH_GROUND_GLOW_SEAM_TAU at the hotter plate's temperature, and in the last
// FISH_GROUND_GLOW_OPEN of its period a plate tears open back to the melt. Whole: the world's mean surface temperature (+ offset).
// Colour: Planck at three wavelengths, relative to a FISH_GROUND_GLOW_REFERENCE melt, compressed by the eye's response as the
// lava's is (FishLavaAdaptedResponse). Needs FishFlora.hlsl (hashes), FishTerrainIndexMap.hlsl and the layer table, Lighting.hlsl.
// Off (_FishGroundGlowInfo.x = 0) unless a biome on the scene has a glowing layer: the terrain then draws exactly as before.

#define FISH_GROUND_GLOW_NONE 0
#define FISH_GROUND_GLOW_SEAMS 1
#define FISH_GROUND_GLOW_WHOLE 2
#define FISH_GROUND_GLOW_FLOOR 550.0
#define FISH_GROUND_GLOW_SKIN_TAU 3.0
#define FISH_GROUND_GLOW_SEAM_TAU 30.0
#define FISH_GROUND_GLOW_OPEN 0.03
#define FISH_GROUND_GLOW_SEAM_SHARE 0.07
#define FISH_GROUND_GLOW_JITTER 0.8
#define FISH_GROUND_GLOW_SALT 0x4C1A9E57u
#define FISH_GROUND_GLOW_REFERENCE 1420.0
#define FISH_GROUND_GLOW_EXPOSURE 2.0
#define FISH_GROUND_GLOW_NIGHT_RESPONSE 0.45
#define FISH_GROUND_GLOW_DAY_RESPONSE 1.0
#define FISH_GROUND_GLOW_DAYLIGHT 1.0
#define FISH_GROUND_GLOW_VISIBLE 400.0
// c2 / lambda at 610, 550 and 465 nm (K), and lambda^-5 relative to green for red and blue.
#define FISH_GROUND_GLOW_K float3(23586.885, 26160.0, 30941.935)
#define FISH_GROUND_GLOW_LAMBDA float2(0.5958875, 2.3149877)

#ifndef FISH_LAYER_ROW_GLOW
#define FISH_LAYER_ROW_GLOW 8
#endif

float4 _FishGroundGlowInfo;   // x 1 when a biome on the scene has a glowing ground layer, y the world's mean surface temperature (K)

/// The plate a point lies on and the nearest other (their hashes), and the distance to the line between them (m).
float FishGroundGlowPlates(float2 xz, float cellMetres, out uint owner, out uint neighbour)
{
    float cell = max(0.5, cellMetres);
    float2 p = xz / cell;
    int2 i = (int2)floor(p);
    float d1 = 3.0e38;
    float d2 = 3.0e38;
    owner = 0u;
    neighbour = 0u;
    [unroll] for (int dz = -1; dz <= 1; dz++)
    {
        [unroll] for (int dx = -1; dx <= 1; dx++)
        {
            int2 c = i + int2(dx, dz);
            uint h = FishFloraCellHash(c, FISH_GROUND_GLOW_SALT);
            float2 s = float2(c) + 0.5 * (1.0 - FISH_GROUND_GLOW_JITTER)
                + FISH_GROUND_GLOW_JITTER * float2(FishFloraUnit(h ^ 1u), FishFloraUnit(h ^ 2u));
            float d = length(p - s);
            if (d < d1)
            {
                d2 = d1;
                neighbour = owner;
                d1 = d;
                owner = h;
            }
            else if (d < d2)
            {
                d2 = d;
                neighbour = h;
            }
        }
    }
    return 0.5 * (d2 - d1) * cell;
}

/// A crust's surface temperature this long after it formed, K.
float FishGroundGlowCrust(float ageSeconds, float meltKelvin, float tauSeconds)
{
    return FISH_GROUND_GLOW_FLOOR + (meltKelvin - FISH_GROUND_GLOW_FLOOR) * rsqrt(1.0 + max(0.0, ageSeconds) / max(0.1, tauSeconds));
}

/// A plate's temperature now: cooling since it last overturned, rising back to the melt as it tears open.
float FishGroundGlowPlate(uint plate, float seconds, float period, float meltKelvin, float tauSeconds)
{
    period = max(1.0, period);
    float p = frac(seconds / period + FishFloraUnit(plate ^ 3u));
    float crust = FishGroundGlowCrust(p * period, meltKelvin, tauSeconds);
    float open = smoothstep(1.0 - FISH_GROUND_GLOW_OPEN, 1.0, p);
    return max(crust, FISH_GROUND_GLOW_FLOOR + (meltKelvin - FISH_GROUND_GLOW_FLOOR) * open);
}

/// A seamed layer's temperature at a point, K.
float FishGroundGlowSeamKelvin(float2 xz, float seconds, float meltKelvin, float cellMetres, float period)
{
    uint owner, neighbour;
    float edge = FishGroundGlowPlates(xz, cellMetres, owner, neighbour);
    float seam = max(FishGroundGlowPlate(owner, seconds, period, meltKelvin, FISH_GROUND_GLOW_SEAM_TAU),
                     FishGroundGlowPlate(neighbour, seconds, period, meltKelvin, FISH_GROUND_GLOW_SEAM_TAU));
    float skin = FishGroundGlowPlate(owner, seconds, period, meltKelvin, FISH_GROUND_GLOW_SKIN_TAU);
    float w = 1.0 - smoothstep(0.0, FISH_GROUND_GLOW_SEAM_SHARE * max(0.5, cellMetres), edge);
    return skin + (seam - skin) * w;
}

/// Planck's law at the three wavelengths, relative to green at the reference melt; 0 below the visible floor.
float3 FishGroundGlowRadiance(float kelvin)
{
    if (kelvin < FISH_GROUND_GLOW_VISIBLE)
    {
        return float3(0.0, 0.0, 0.0);
    }
    float reference = exp(FISH_GROUND_GLOW_K.y / FISH_GROUND_GLOW_REFERENCE) - 1.0;
    float3 k = FISH_GROUND_GLOW_K / kelvin;
    return float3(FISH_GROUND_GLOW_LAMBDA.x, 1.0, FISH_GROUND_GLOW_LAMBDA.y) * reference / (exp(k) - 1.0);
}

float FishGroundGlowLuminance(float3 c)
{
    return 0.2126 * c.x + 0.7152 * c.y + 0.0722 * c.z;
}

/// The emitted colour at a temperature under the eye's response, the melt's brightest channel at FISH_GROUND_GLOW_EXPOSURE.
float3 FishGroundGlowColour(float kelvin, float response)
{
    float3 rgb = FishGroundGlowRadiance(kelvin);
    float y = FishGroundGlowLuminance(rgb);
    if (y <= 1e-30)
    {
        return float3(0.0, 0.0, 0.0);
    }
    float3 melt = FishGroundGlowRadiance(FISH_GROUND_GLOW_REFERENCE);
    float meltY = FishGroundGlowLuminance(melt);
    float peak = max(melt.x, max(melt.y, melt.z)) / meltY;
    return rgb * (pow(y / meltY, response) * FISH_GROUND_GLOW_EXPOSURE / peak / y);
}

/// The eye's response under the light on the ground: the dark's, rising to daylight's (the lava's FishLavaAdaptedResponse).
float FishGroundGlowResponse()
{
    Light light = GetMainLight();
    float3 weights = float3(0.2126, 0.7152, 0.0722);
    float illuminance = dot(light.color, weights) * saturate(light.direction.y) + dot(_GlossyEnvironmentColor.rgb, weights);
    return lerp(FISH_GROUND_GLOW_NIGHT_RESPONSE, FISH_GROUND_GLOW_DAY_RESPONSE, saturate(illuminance / FISH_GROUND_GLOW_DAYLIGHT));
}

/// One layer's glow at a point from its packed table texel (BiomeGroundGlow.Pack): x mode + 8 * period, y kelvin, z strength, w plate size.
float3 FishGroundGlowLayer(float4 packed, float2 xz, float seconds, float worldKelvin, float response)
{
    float whole = max(0.0, round(packed.x));
    float period = floor(whole / 8.0);
    int mode = (int)(whole - 8.0 * period);
    float kelvin = 0.0;
    if (mode == FISH_GROUND_GLOW_SEAMS)
    {
        kelvin = FishGroundGlowSeamKelvin(xz, seconds, packed.y, packed.w, period);
    }
    else if (mode == FISH_GROUND_GLOW_WHOLE)
    {
        kelvin = worldKelvin + packed.y;
    }
    return FishGroundGlowColour(kelvin, response) * saturate(packed.z);
}

/// The terrain's glow at a pixel: its four strongest layers (FishTopLayers), each its own glow by its weight. Steep faces (a
/// cliff's rock) and the ways' trodden ground take less.
float3 FishGroundGlow(float2 uv, float3 positionWS, half3 normalWS, float trodden)
{
    int4 ids;
    half covered;
    half4 weights = FishTopLayers(uv, ids, covered);
    float seconds = _FishWeatherMisc.w;
    float worldKelvin = _FishGroundGlowInfo.y;
    float response = FishGroundGlowResponse();
    float3 glow = float3(0.0, 0.0, 0.0);
    [loop] for (int k = 0; k < 4; k++)
    {
        float w = (float)weights[k];
        [branch] if (w > 0.002)
        {
            float4 packed = FishLayerRow(ids[k], FISH_LAYER_ROW_GLOW);
            [branch] if (packed.x > 0.5 && packed.z > 0.0)
            {
                glow += w * FishGroundGlowLayer(packed, positionWS.xz, seconds, worldKelvin, response);
            }
        }
    }
    return glow * smoothstep(0.35, 0.7, (float)normalWS.y) * (1.0 - saturate(trodden));
}

#endif
