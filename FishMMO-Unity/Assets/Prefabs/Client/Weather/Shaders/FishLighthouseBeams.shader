// The lighthouses' lanterns at night (LighthouseBeams draws them): a pre-built mesh of open cones and glare quads that
// the vertex shader hands out among the nearest lanterns. Each cone is a beam thrown by the lantern's turning mirrors —
// laid along its heading, widening as it goes, brightest through its middle and near the lantern, taken by the air over
// its length and softened where it meets the ground or a wall. Each quad is the lantern's own glare turned to the camera:
// a steady glow, and a dazzling flash when a beam swings over the eye. Added, never covering. Nothing is simulated and
// nothing is stored: the headings come from the shared motion clock (LighthouseBeams.Heading).
Shader "FishMMO/World/Lighthouse Beams"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "LighthouseBeams"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define BEACONS 8
            #define BEAMS 3

            // Per draw (LighthouseBeams): the lanterns, nearest first. xyz the lantern's middle, w its heading (radians about
            // +y from +z).
            float4 _BeaconSources[BEACONS];
            // Per lantern: x how strong its beams show (night, faded with distance; 0 none), y how many beams it throws,
            // z how strong its glare shows (the same, and dimmed by the air between), w unused.
            float4 _BeaconParams[BEACONS];
            // x a beam's drawn length (m), y the metres over which the air takes it to a third, z how much the air shows it
            // (thin in a clear night, strong in fog, rain and snow), w unused.
            float4 _BeaconAir;

            // A beam's spread: its width at the mirror (m) and how much it widens a metre.
            #define BEAM_MOUTH 0.8
            #define BEAM_SPREAD 0.04
            // How far a beam dips below level, to fall on the sea a few kilometres out.
            #define BEAM_DIP 0.02
            // How tight the flash is about a beam's axis: exp(-(1 - cos) * this), a few degrees across.
            #define FLASH_TIGHTNESS 700.0

            static const float3 BeamColour = float3(1.0, 0.8, 0.52);

            struct Attributes
            {
                float3 shape : POSITION;    // a cone's unit circle (xy) and share of its length (z); a quad's corner (xy)
                float3 tag : TEXCOORD0;     // x the lantern, y the beam, z 0 for a cone, 1 for a quad
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 data : TEXCOORD2;    // x metres along the beam, y strength, z 0 a cone / 1 a quad, w the flash
                float2 corner : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                float fogCoord : TEXCOORD5;
            };

            // Beam k of a lantern turned to `heading`, of `count` evenly round it.
            float3 BeamDirection(float heading, int k, float count)
            {
                float a = heading + 6.2831853 * k / max(count, 1.0);
                return normalize(float3(sin(a), -BEAM_DIP, cos(a)));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                int b = min(BEACONS - 1, (int)round(input.tag.x));
                int k = (int)round(input.tag.y);
                bool quad = input.tag.z > 0.5;
                float4 source = _BeaconSources[b];
                float4 params = _BeaconParams[b];
                float3 worldPos;
                if (quad)
                {
                    // The glare: turned to the camera, a little in front of the lantern's panes so they do not hide it, and
                    // growing with distance so a lighthouse far out still shows. Its flash: how near any beam points at the eye.
                    float3 toCamera = _WorldSpaceCameraPos.xyz - source.xyz;
                    float distance = max(length(toCamera), 1e-3);
                    float3 view = toCamera / distance;
                    float flash = 0.0;
                    for (int j = 0; j < BEAMS; j++)
                    {
                        if (j < params.y)
                        {
                            flash = max(flash, exp(-(1.0 - dot(BeamDirection(source.w, j, params.y), view)) * FLASH_TIGHTNESS));
                        }
                    }
                    float shown = step(0.001, params.z);
                    float size = (1.6 + 0.01 * distance) * lerp(1.0, 2.5, flash) * shown;
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                    float3 centre = source.xyz + view * min(2.2, distance * 0.5);
                    worldPos = centre + (right * input.shape.x + up * input.shape.y) * size;
                    output.data = float4(0.0, params.z, 1.0, flash);
                    output.corner = input.shape.xy;
                }
                else
                {
                    // A beam: along its heading, widening from the mirror. One the lantern does not throw folds to a point.
                    float shown = step((float)k + 0.5, params.y) * step(0.001, params.x);
                    float3 direction = BeamDirection(source.w, k, params.y);
                    float3 side = normalize(cross(float3(0.0, 1.0, 0.0), direction));
                    float3 lift = cross(direction, side);
                    float along = input.shape.z * _BeaconAir.x;
                    float radius = BEAM_MOUTH + along * BEAM_SPREAD;
                    float3 normal = side * input.shape.x + lift * input.shape.y;
                    worldPos = source.xyz + (direction * along + normal * radius) * shown;
                    output.normalWS = normal;
                    output.data = float4(along, params.x, 0.0, 0.0);
                }
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.eyeDepth = -TransformWorldToView(worldPos).z;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float strength = input.data.y;
                if (strength <= 0.0)
                {
                    discard;
                }
                if (input.data.z > 0.5)
                {
                    // The glare: a hot core the fire always shows, and a wide dazzle while a beam is on the eye. The air
                    // between is in its strength already, so no fog on top of it.
                    float r2 = dot(input.corner, input.corner);
                    float flash = input.data.w;
                    float core = exp(-r2 * 14.0);
                    float halo = exp(-r2 * 3.5);
                    float3 glare = BeamColour * (core * (0.6 + 3.0 * flash) + halo * 1.2 * flash) * strength;
                    return half4((half3)glare, 0.0);
                }
                // A beam is a shaft of lit air: thickest seen through its middle, thinnest at its edges. The air takes it over
                // its length; it starts at the mirror and fades out before its drawn end.
                float3 view = _WorldSpaceCameraPos.xyz - input.positionWS;
                float viewDistance = length(view);
                float through = pow(abs(dot(normalize(input.normalWS), view / max(viewDistance, 1e-3))), 1.5);
                float along = input.data.x;
                float carried = exp(-along * 1.0986 / max(_BeaconAir.y, 1.0))
                    * smoothstep(0.5, 4.0, along) * (1.0 - smoothstep(0.75, 1.0, along / max(_BeaconAir.x, 1.0)));
                // Soft where it meets the ground, a wall or the sea, and where the camera stands in it.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneDepth - input.eyeDepth) / 6.0);
                float close = saturate((viewDistance - 1.0) / 8.0);
                float3 rgb = BeamColour * (0.6 * through * carried * soft * close * strength * _BeaconAir.z);
                rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                return half4((half3)rgb, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
