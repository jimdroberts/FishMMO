Shader "Hidden/FishMMO/Water/Foam Field"
{
    // One step of the simulated foam (WaterFoamField.cs; read by surfaces through FishWaterFoamField.hlsl).
    //
    // Each texel of a window round the camera holds how much foam floats there (R) and how fresh it is (G). A step
    // carries the foam with the water (traced back along the current to where it was a step ago, read from the
    // window as it was then: semi-Lagrangian), thickens it where the water converges and thins it where the water
    // spreads (what is carried is foam per square metre), lets it decay, and adds what the water is making now:
    //
    //   rivers and lakes  the current and the foam sources the inland water drew into _FoamCurrent this frame
    //                     (FishInlandWater.shader, pass FoamSource): breaking crests, stones, falls, wakes;
    //   the open sea      a wind drift with Langmuir cells across it (the windrows that draw a sea's foam into
    //                     streaks along the wind), and the white caps, from the FFT's folding as the sea draws them;
    //   the surf          the bores running in from the breakers, carrying their foam shoreward;
    //   the sand          the swash's lip, stranding foam at the top of each run-up, where it lies until it bursts.
    //
    // Read back by index and written by index, so rows line up on every API; the windows are world-aligned, so the
    // previous window is read at world positions (it may sit a few texels off the new one after the camera moved).
    SubShader
    {
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWaterShoreCommon.hlsl"
            #include "FishWaterFoam.hlsl"
            // The lace's renewal (FishFoamLacePhase), shared with the surfaces that read it.
            #include "FishWaterFoamField.hlsl"

            // ── The step's own inputs (WaterFoamField) ──
            TEXTURE2D(_FoamPrevious);
            SAMPLER(sampler_FoamPrevious);
            TEXTURE2D(_LacePrevious);    // the lace's carried offsets as they stood last step (near window only; pass Lace)
            // Its own sampler: the Lace pass reads no _FoamPrevious, and a sampler borrowed from a texture the pass never reads
            // is one Unity cannot place (Unrecognized sampler 'sampler_foamprevious').
            SAMPLER(sampler_LacePrevious);
            Texture2D<float4> _FoamCurrent;  // the inland water's: xy current (m/s, world xz), z 1 + how broken where it is water (0 dry), w foam made per second
            float4 _FoamRect;            // this window: xy world corner, z side (m), w metres per texel
            float4 _FoamPreviousRect;    // the window as it stood last step: xy corner, z side, w 1 when it holds anything
            float4 _FoamSize;            // xy texels, zw one over them
            float4 _FoamStep;            // x seconds stepped, y what inland foam keeps, z what the sea's keeps, w what the sand's keeps
            float4 _FoamStepB;           // x what freshness keeps, y 1 with a sea, z what the surf's keeps, w the clock (s, wrapped at 10 000)
            float4 _FoamStepC;           // x what inland foam keeps on broken water (y of _FoamStep: on still water), y how much the sea and the shore make (1; 0 while a probe warms the field with the sea frozen)
            float4 _FoamSea;             // x the white-cap threshold, y the crest curve's top, z the shallow-water lowering, w the strong-wind share

            // ── The sea's, published globally by WaterSurface / WaterShoreField ──
            float _FishWaterLevel;
            float _FishWaterMeanLevel;
            float _FishWaterShoreTexel;
            float _FishWaterWhitecap;
            float4 _FishWaterPatch;
            TEXTURE2D(_FishWaterDerivatives0);
            TEXTURE2D(_FishWaterDerivatives1);
            TEXTURE2D(_FishWaterDerivatives2);
            SAMPLER(sampler_FishWaterDerivatives0);

            /// Foam piled past this is no whiter (FishFoamFieldCoverage): a cap keeps a pile from growing for ever where currents meet.
            #define FOAM_MOST 4.0

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // One triangle over the whole target.
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                float2 corner = float2((vertexID << 1) & 2, vertexID & 2);
                Varyings output;
                output.positionCS = float4(corner * 2.0 - 1.0, 0.0, 1.0);
                return output;
            }

            /// <summary>
            /// The previous window at world xz, Catmull-Rom from nine bilinear reads and clamped to the four texels
            /// round it, so it neither blurs (a bilinear back-trace smears the foam a little every step, metres in a
            /// minute) nor rings past what was there. Outside the previous window: no foam.
            /// </summary>
            float2 Previous(float2 xz)
            {
                if (_FoamPreviousRect.w < 0.5)
                {
                    return 0.0;
                }
                float2 uv = (xz - _FoamPreviousRect.xy) / _FoamPreviousRect.z;
                if (any(uv < 0.0) || any(uv > 1.0))
                {
                    return 0.0;
                }
                float2 size = _FoamSize.xy;
                float2 at = uv * size;
                float2 base = floor(at - 0.5) + 0.5;
                float2 f = at - base;
                float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
                float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
                float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
                float2 w3 = f * f * (-0.5 + 0.5 * f);
                float2 w12 = w1 + w2;
                float2 t0 = (base - 1.0) * _FoamSize.zw;
                float2 t12 = (base + w2 / w12) * _FoamSize.zw;
                float2 t3 = (base + 2.0) * _FoamSize.zw;
                float2 sum = 0.0;
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t0.x, t0.y), 0).rg * (w0.x * w0.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t12.x, t0.y), 0).rg * (w12.x * w0.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t3.x, t0.y), 0).rg * (w3.x * w0.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t0.x, t12.y), 0).rg * (w0.x * w12.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t12.x, t12.y), 0).rg * (w12.x * w12.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t3.x, t12.y), 0).rg * (w3.x * w12.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t0.x, t3.y), 0).rg * (w0.x * w3.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t12.x, t3.y), 0).rg * (w12.x * w3.y);
                sum += SAMPLE_TEXTURE2D_LOD(_FoamPrevious, sampler_FoamPrevious, float2(t3.x, t3.y), 0).rg * (w3.x * w3.y);
                // The four texels round the point bound it: Catmull-Rom overshoots at a sharp edge.
                int2 corner = clamp(int2(at - 0.5), 0, int2(size) - 2);
                float2 a = _FoamPrevious.Load(int3(corner, 0)).rg;
                float2 b = _FoamPrevious.Load(int3(corner + int2(1, 0), 0)).rg;
                float2 c = _FoamPrevious.Load(int3(corner + int2(0, 1), 0)).rg;
                float2 d = _FoamPrevious.Load(int3(corner + int2(1, 1), 0)).rg;
                float2 lo = min(min(a, b), min(c, d));
                float2 hi = max(max(a, b), max(c, d));
                return clamp(sum, lo, hi);
            }

            /// <summary>
            /// The sea's surface current: a drift down the wind (about 3% of its speed, the Ekman surface drift and the
            /// Stokes drift together) with Langmuir circulation across it — rolls along the wind, a few tens of metres
            /// apart, that sweep the surface toward lines between them, which is why foam on a windy sea lies in long
            /// streaks down the wind. The cells wander along the wind and reorganise over minutes. Returns the current
            /// (m/s, world xz) and through <paramref name="divergence"/> how fast it spreads there (1/s; negative where
            /// it converges, where the streaks gather).
            /// </summary>
            float2 SeaCurrent(float2 xz, out float divergence)
            {
                float2 down = _FishWaterWind.xy;
                down = dot(down, down) > 1e-6 ? normalize(down) : float2(1.0, 0.0);
                float2 across = float2(-down.y, down.x);
                float wind = max(0.0, _FishWaterWind.z);
                float drift = min(0.6, 0.03 * wind);
                // Spacing grows with the wind (deeper mixing); the cells wander as they run down the wind.
                float spacing = clamp(6.0 + 2.2 * wind, 8.0, 60.0);
                float a = dot(xz, down), c = dot(xz, across);
                const float Reorganise = 10000.0 / round(10000.0 / 420.0);
                float slow = 6.2831853 * frac(_FoamStepB.w / Reorganise);
                float wander = 1.1 * sin(a / (2.7 * spacing) + slow) + 0.6 * sin(a / (1.3 * spacing) - 0.7 * slow + 1.9);
                float phase = 6.2831853 * c / spacing + wander;
                // Toward the convergence lines at phase 0: a pull across the wind, strongest midway between them.
                float pull = (0.25 * drift + 0.01) * smoothstep(1.0, 4.0, wind);
                float side = -pull * sin(phase);
                divergence = -pull * cos(phase) * 6.2831853 / spacing;
                return down * drift + across * side;
            }

            /// <summary>
            /// How hard the open sea is breaking at a flat point now, 0 … 1, as the sea draws its white caps
            /// (FishWaterShading.hlsl): the FFT's folding below the threshold, the threshold lowered over the band where
            /// the sea is being faded out over the shallows, and only where the open sea's waves reach.
            /// </summary>
            float WhiteCaps(float2 xz, float depth)
            {
                float folding = 0.0;
                float3 resolved = saturate(_FishWaterPatch.xyz / (max(0.1, _FoamRect.w) * 4.0) - 1.0);
                folding += max(0.0, 1.0 - SAMPLE_TEXTURE2D_LOD(_FishWaterDerivatives0, sampler_FishWaterDerivatives0, xz / max(1.0, _FishWaterPatch.x), 0).z) * resolved.x;
                folding += max(0.0, 1.0 - SAMPLE_TEXTURE2D_LOD(_FishWaterDerivatives1, sampler_FishWaterDerivatives0, xz / max(1.0, _FishWaterPatch.y), 0).z) * resolved.y;
                folding += max(0.0, 1.0 - SAMPLE_TEXTURE2D_LOD(_FishWaterDerivatives2, sampler_FishWaterDerivatives0, xz / max(1.0, _FishWaterPatch.z), 0).z) * resolved.z;
                folding *= FishWaterOpenSea(xz);
                float jacobian = 1.0 - saturate(folding);
                float threshold = lerp(_FoamSea.x, 0.80, saturate(_FoamSea.w));
                float calm = _FishWaterBreakDepth.y <= _FishWaterBreakDepth.x ? 1.0 : smoothstep(_FishWaterBreakDepth.x, _FishWaterBreakDepth.y, depth);
                float breakDepth = max(0.01, _FishWaterBreakDepth.x);
                float fading = (1.0 - calm) * smoothstep(0.5 * breakDepth, breakDepth, depth);
                float open = saturate(calm + fading);
                threshold = lerp(threshold, 0.97, _FoamSea.z * fading);
                float crest = saturate((threshold - jacobian) / max(0.01, threshold)) * open;
                return smoothstep(0.02, max(0.06, _FoamSea.y), crest);
            }

            /// <summary>
            /// What the water does at a texel: whether there is any (false: dry, no foam), its current (m/s, world xz) and
            /// how fast it spreads (1/s), the foam it makes per second, any foam it lays as a level (the swash's stranded
            /// line), and what the foam there keeps of itself over the step.
            /// </summary>
            bool WaterAt(uint2 texel, float2 xz, out float2 current, out float divergence, out float made, out float laid, out float keep)
            {
                current = 0.0;
                divergence = 0.0;
                made = 0.0;
                laid = 0.0;
                keep = 0.0;
                bool water = false;

                // ── Rivers and lakes: the current and sources the inland water drew this frame ──
                float4 inland = _FoamCurrent.Load(int3(texel, 0));
                if (inland.z > 0.5)
                {
                    water = true;
                    current = inland.xy;
                    made = inland.w;
                    // Foam on still water lasts; on churned water its bubbles burst within seconds.
                    keep = lerp(_FoamStep.y, _FoamStepC.x, saturate(inland.z - 1.0));
                    /* How fast the current spreads, from the neighbours that are water too: across a bank the
                     * difference is with dry ground, which would read as a current running into the bank. */
                    int2 size = int2(_FoamSize.xy) - 1;
                    float4 e = _FoamCurrent.Load(int3(min(int2(texel) + int2(1, 0), size), 0));
                    float4 w = _FoamCurrent.Load(int3(max(int2(texel) - int2(1, 0), 0), 0));
                    float4 n = _FoamCurrent.Load(int3(min(int2(texel) + int2(0, 1), size), 0));
                    float4 s = _FoamCurrent.Load(int3(max(int2(texel) - int2(0, 1), 0), 0));
                    float dx = (e.z > 0.5 && w.z > 0.5) ? (e.x - w.x) * 0.5 : 0.0;
                    float dz = (n.z > 0.5 && s.z > 0.5) ? (n.y - s.y) * 0.5 : 0.0;
                    divergence = (dx + dz) / max(0.05, _FoamRect.w);
                }
                else if (_FoamStepB.y > 0.5)
                {
                    float2 shore = FishWaterShoreSample(xz);
                    float tide = _FishWaterLevel - _FishWaterMeanLevel;
                    float depth = shore.x + tide;
                    bool field = shore.y < 990.0;
                    if (depth > 0.02)
                    {
                        water = true;
                        float2 drift = SeaCurrent(xz, divergence);
                        float breakDepth = _FishWaterBreakDepth.x;
                        if (field && FishWaterSurfKeepsTime() && depth < breakDepth)
                        {
                            /* The surf: the bores running in carry their foam shoreward, at about a third of the
                             * bore's own speed (the water in a bore moves slower than its front), the drift of the
                             * wind still on it; the foam bursts fast on the churned water. */
                            float2 towardShore;
                            float confidence = FishWaterShoreFacingField(xz, _FishWaterShoreTexel, towardShore);
                            float3 bore = FishWaterBoreFoam(xz, depth, shore.y, towardShore, confidence);
                            float inSurf = 1.0 - smoothstep(0.6 * breakDepth, breakDepth, depth);
                            current = drift * (1.0 - 0.5 * inSurf) + towardShore * (0.35 * FishWaterBoreSpeed() * confidence * saturate(bore.x + 0.25) * inSurf);
                            divergence *= 1.0 - inSurf;
                            made = (6.0 * bore.y + 1.5 * bore.x) * _FoamStepC.y;
                            keep = lerp(_FoamStep.z, _FoamStepB.z, inSurf);
                        }
                        else
                        {
                            current = drift;
                            // A crest breaks for a second or so: it leaves a patch of a third or less, which then thins.
                            /* And only in a wind that breaks crests: a real sea has next to no white caps under about 4 m/s
                             * whatever its swell (about 1 % of it white at 10 m/s, 4 % at 15, Monahan), where the sea's
                             * folding alone would cap a swell running in from a far storm. */
                            float windBreaks = smoothstep(3.0, 8.0, _FishWaterWind.z);
                            made = 0.3 * WhiteCaps(xz, field ? depth : 1000.0) * windBreaks * _FoamStepC.y;
                            keep = _FoamStep.z;
                        }
                    }
                    else if (field && FishWaterSurfKeepsTime())
                    {
                        /* The sand: where the swash runs up, the foam its lip carries is left at the top of the run-up
                         * and lies there, still, until it bursts. Laid as a level, not made at a rate, so a lip passing
                         * leaves the line the shore pass drew. Too high or too low for any swash: dry. */
                        float rise = -depth;
                        float waveHeight = max(0.05, _FishWaterSwashSea.x);
                        if (rise < 2.0 * waveHeight * 1.35 * 1.3 + 0.05)
                        {
                            water = true;
                            float slopeStep = max(4.0, _FishWaterShoreTexel * 4.0);
                            float depthEast = FishWaterShoreSample(xz + float2(slopeStep, 0.0)).x;
                            float depthNorth = FishWaterShoreSample(xz + float2(0.0, slopeStep)).x;
                            float slope = length(float2(depthEast - shore.x, depthNorth - shore.x)) / slopeStep;
                            laid = FishWaterSwashFoamDeposit(rise, FishWaterRunUpHere(slope, xz, _FishWaterShoreTexel), xz) * _FoamStepC.y;
                            keep = _FoamStep.w;
                        }
                    }
                }
                return water;
            }

            float2 Frag(Varyings input) : SV_Target
            {
                uint2 texel = uint2(input.positionCS.xy);
                float2 xz = _FoamRect.xy + (float2(texel) + 0.5) * _FoamRect.w;
                float dt = _FoamStep.x;
                float2 current;
                float divergence, made, laid, keep;
                if (!WaterAt(texel, xz, current, divergence, made, laid, keep))
                {
                    return 0.0;
                }

                // Carried: where this water was a step ago, and what lay there.
                float2 was = Previous(xz - current * dt);
                // Foam per square metre: gathered where the water converges, spread thin where it parts.
                float gather = clamp(exp(-divergence * dt), 0.8, 1.25);
                float amount = was.x * gather * keep;
                float fresh = was.y * _FoamStepB.x;
                amount = min(FOAM_MOST, max(amount + made * dt, laid));
                fresh = max(fresh, saturate(max(made * 0.35, laid)));
                return float2(amount, fresh);
            }

            /// <summary>
            /// The lace's carried offsets (FishWaterFoamField.hlsl, FishFoamFieldLace): rg how far one layer's water has
            /// moved since that layer was renewed, ba the other's. Carried with the water as the foam is, plus this step's
            /// travel; a layer whose renewal comes round this step starts again from nothing (it is unseen then).
            /// </summary>
            float4 LaceFrag(Varyings input) : SV_Target
            {
                uint2 texel = uint2(input.positionCS.xy);
                float2 xz = _FoamRect.xy + (float2(texel) + 0.5) * _FoamRect.w;
                float dt = _FoamStep.x;
                float2 current;
                float divergence, made, laid, keep;
                if (!WaterAt(texel, xz, current, divergence, made, laid, keep))
                {
                    return 0.0;
                }
                float2 from = xz - current * dt;
                float4 moved = 0.0;
                if (_FoamPreviousRect.w > 0.5)
                {
                    float2 uv = (from - _FoamPreviousRect.xy) / _FoamPreviousRect.z;
                    if (all(uv >= 0.0) && all(uv <= 1.0))
                    {
                        moved = SAMPLE_TEXTURE2D_LOD(_LacePrevious, sampler_LacePrevious, uv, 0);
                    }
                }
                moved += current.xyxy * dt;
                float renewing = dt / FISH_FOAM_LACE_PERIOD;
                if (FishFoamLacePhase(xz, _FoamStepB.w, 0.0) < renewing)
                {
                    moved.xy = 0.0;
                }
                if (FishFoamLacePhase(xz, _FoamStepB.w, 1.0) < renewing)
                {
                    moved.zw = 0.0;
                }
                return moved;
            }
        ENDHLSL

        Pass
        {
            Name "Step"
            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            ENDHLSL
        }

        Pass
        {
            Name "Lace"
            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment LaceFrag
            #pragma target 3.5
            ENDHLSL
        }
    }
    Fallback Off
}
