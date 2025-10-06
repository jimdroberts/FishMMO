Shader "FishMMO/RealisticWater"
{
    Properties
    {
        [Header(Water Colors)]
        _WaterColor ("Deep Water Color", Color) = (0.043, 0.310, 0.424, 1)
        _ShallowColor ("Shallow Water Color", Color) = (0.243, 0.604, 0.780, 1)
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        
        [Header(Wave Properties)]
        _WaveSpeed ("Wave Speed", Range(0, 5)) = 1.0
        _WaveHeight ("Wave Height", Range(0, 1)) = 0.1
        _WaveFrequency ("Wave Frequency", Range(0.1, 10)) = 1.0
        _WaveDirection ("Wave Direction", Vector) = (1, 1, 0, 0)
        
        [Header(Tide Properties)]
        _TideHeight ("Tide Height", Range(0, 1)) = 0.2
        _TideSpeed ("Tide Speed", Range(0, 1)) = 0.1
        
        [Header(Surface Properties)]
        _Transparency ("Transparency", Range(0, 1)) = 0.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.9
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _FresnelPower ("Fresnel Power", Range(0, 5)) = 2.0
        
        [Header(Foam Properties)]
        _FoamDistance ("Foam Distance", Range(0, 5)) = 1.5
        _FoamIntensity ("Foam Intensity", Range(0, 2)) = 1.0
        _FoamCutoff ("Foam Cutoff", Range(0, 1)) = 0.3
        _FoamSmoothness ("Foam Smoothness", Range(0.1, 2)) = 0.5
        _ShorelineSmoothing ("Shoreline Smoothing", Range(0.1, 1)) = 0.3
        
        [Header(Infinite Ocean)]
        _EnableInfiniteOcean ("Enable Infinite Ocean", Float) = 1.0
        _FarOceanColor ("Far Ocean Color", Color) = (0.02, 0.2, 0.35, 1)
        _FarOceanFadeDistance ("Far Ocean Fade Distance", Range(0.1, 0.9)) = 0.7
        _HorizonBlend ("Horizon Blend", Range(0, 1)) = 0.8
        _DistanceWaveReduction ("Distance Wave Reduction", Range(0, 1)) = 0.8
        
        [Header(Normal Maps)]
        _NormalMap ("Primary Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1.0
        _NormalSpeed ("Normal Animation Speed", Vector) = (0.1, 0.1, 0, 0)
        _NormalScale ("Normal Tiling", Range(0.1, 5)) = 1.0
        
        _SecondaryNormalMap ("Secondary Normal Map", 2D) = "bump" {}
        _SecondaryNormalStrength ("Secondary Normal Strength", Range(0, 2)) = 0.5
        _SecondaryNormalSpeed ("Secondary Normal Speed", Vector) = (-0.05, 0.15, 0, 0)
        
        [Header(Noise Textures)]
        _FoamNoise ("Foam Noise Texture", 2D) = "white" {}
        _NoiseScale ("Noise Scale", Range(0.1, 5)) = 1.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "RenderPipeline"="UniversalPipeline"
        }
        
        LOD 300
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            
            // URP keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float3 worldTangent : TEXCOORD4;
                float3 worldBitangent : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
            };

            // Properties - All water shader parameters
            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor;
                half4 _ShallowColor;
                half4 _FoamColor;
                
                half _WaveSpeed;
                half _WaveHeight;
                half _WaveFrequency;
                half4 _WaveDirection;
                
                half _TideHeight;
                half _TideSpeed;
                
                half _Transparency;
                half _Smoothness;
                half _Metallic;
                half _FresnelPower;
                
                half _FoamDistance;
                half _FoamIntensity;
                half _FoamCutoff;
                half _FoamSmoothness;
                half _ShorelineSmoothing;
                
                half _EnableInfiniteOcean;
                half4 _FarOceanColor;
                half _FarOceanFadeDistance;
                half _HorizonBlend;
                half _DistanceWaveReduction;
                
                half _NormalStrength;
                half4 _NormalSpeed;
                half _NormalScale;
                
                half _SecondaryNormalStrength;
                half4 _SecondaryNormalSpeed;
                
                half _NoiseScale;
                
                float4 _NormalMap_ST;
                float4 _SecondaryNormalMap_ST;
                float4 _FoamNoise_ST;
            CBUFFER_END
            
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_SecondaryNormalMap);
            SAMPLER(sampler_SecondaryNormalMap);
            TEXTURE2D(_FoamNoise);
            SAMPLER(sampler_FoamNoise);

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0; // Initialize all fields to zero
                
                float3 worldPos = TransformObjectToWorld(input.vertex.xyz);
                
                // Calculate distance from camera for infinite ocean effect
                float3 cameraPos = _WorldSpaceCameraPos;
                float distanceFromCamera = distance(worldPos, cameraPos);
                float maxDistance = _ProjectionParams.z; // Far clip plane
                float normalizedDistance = saturate(distanceFromCamera / maxDistance);
                
                // Infinite ocean vertex expansion
                if (_EnableInfiniteOcean > 0.5)
                {
                    // Calculate direction from camera to vertex
                    float3 directionFromCamera = normalize(worldPos - cameraPos);
                    
                    // Project vertex to far clip plane along camera direction
                    // This creates true infinite ocean effect
                    float farDistance = maxDistance * 0.98; // Slightly less than far clip to avoid clipping
                    worldPos = cameraPos + directionFromCamera * farDistance;
                    
                    // Keep original Y position for water level consistency
                    float originalY = TransformObjectToWorld(input.vertex.xyz).y;
                    worldPos.y = originalY;
                }
                
                // Wave calculation with distance-based reduction and shoreline smoothing
                float time = _Time.y * _WaveSpeed;
                float2 waveDir = normalize(_WaveDirection.xy);
                float wavePhase = dot(worldPos.xz, waveDir) * _WaveFrequency + time;
                
                // Reduce wave height at distance for infinite ocean
                float waveReduction = lerp(1.0, _DistanceWaveReduction, normalizedDistance);
                
                // Add multiple wave frequencies for more natural shoreline behavior
                float primaryWave = sin(wavePhase) * _WaveHeight;
                float secondaryWave = sin(wavePhase * 1.7 + time * 0.8) * _WaveHeight * 0.3;
                float tertiaryWave = sin(wavePhase * 2.3 - time * 0.6) * _WaveHeight * 0.15;
                
                // Tide calculation with smoother variation
                float tidePhase = worldPos.x * 0.08 + worldPos.z * 0.03 + _Time.y * _TideSpeed;
                float tide = sin(tidePhase) * _TideHeight * 0.5;
                tide += sin(tidePhase * 1.3 + _Time.y * _TideSpeed * 0.7) * _TideHeight * 0.2;
                
                // Combine waves and tides with distance reduction
                float displacement = (primaryWave + secondaryWave + tertiaryWave) * waveReduction + tide * waveReduction;
                
                // Apply displacement to local space vertex before final transform
                float3 localPos = input.vertex.xyz;
                localPos.y += displacement;
                
                // Transform to world space with modifications
                if (_EnableInfiniteOcean > 0.5)
                {
                    output.worldPos = worldPos;
                    output.worldPos.y += displacement;
                    output.vertex = TransformWorldToHClip(output.worldPos);
                }
                else
                {
                    output.vertex = TransformObjectToHClip(localPos);
                    output.worldPos = TransformObjectToWorld(localPos);
                }
                
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.vertex);
                output.worldNormal = TransformObjectToWorldNormal(input.normal);
                output.worldTangent = TransformObjectToWorldDir(input.tangent.xyz);
                output.worldBitangent = cross(output.worldNormal, output.worldTangent) * input.tangent.w;
                
                // Initialize shadow coordinate
                #if _MAIN_LIGHT_SHADOWS
                    output.shadowCoord = TransformWorldToShadowCoord(output.worldPos);
                #else
                    output.shadowCoord = float4(0, 0, 0, 0);
                #endif
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Calculate distance from camera for infinite ocean effects
                float3 cameraPos = _WorldSpaceCameraPos;
                float distanceFromCamera = distance(input.worldPos, cameraPos);
                float maxDistance = _ProjectionParams.z; // Far clip plane
                float normalizedDistance = saturate(distanceFromCamera / maxDistance);
                
                // Depth calculation for foam and color blending (disabled for far ocean)
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                real sceneDepth = SampleSceneDepth(screenUV);
                real linearSceneDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);
                real linearSurfaceDepth = input.screenPos.w;
                
                // Apply temporal smoothing to depth difference to reduce twitchiness
                real rawDepthDiff = (linearSceneDepth - linearSurfaceDepth) / _FoamDistance;
                real depthDiff = saturate(rawDepthDiff);
                
                // Add slight blur to depth edges for smoother shoreline
                float2 blurOffset = float2(0.001, 0.001);
                real depth1 = LinearEyeDepth(SampleSceneDepth(screenUV + blurOffset), _ZBufferParams);
                real depth2 = LinearEyeDepth(SampleSceneDepth(screenUV - blurOffset), _ZBufferParams);
                real avgDepth = (linearSceneDepth + depth1 + depth2) / 3.0;
                depthDiff = saturate((avgDepth - linearSurfaceDepth) / _FoamDistance);
                
                // Disable depth effects for infinite ocean
                if (_EnableInfiniteOcean > 0.5 && normalizedDistance > _FarOceanFadeDistance)
                {
                    depthDiff = 1.0; // Assume deep water for far ocean
                }
                
                // Animated UV coordinates for normals with distance-based scaling
                float uvScale = lerp(_NormalScale, _NormalScale * 0.1, normalizedDistance);
                float2 time = _Time.y * _NormalSpeed.xy;
                float2 uv1 = input.uv * uvScale + time;
                float2 uv2 = input.uv * uvScale * 0.8 + _Time.y * _SecondaryNormalSpeed.xy;
                
                // Sample normal maps with distance-based reduction
                float normalReduction = lerp(1.0, 0.3, normalizedDistance);
                half3 normal1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1), _NormalStrength * normalReduction);
                half3 normal2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_SecondaryNormalMap, sampler_SecondaryNormalMap, uv2), _SecondaryNormalStrength * normalReduction);
                
                // Combine normals
                half3 combinedNormal = normalize(normal1 + normal2);
                
                // Transform normal to world space
                half3x3 tangentToWorld = half3x3(input.worldTangent, input.worldBitangent, input.worldNormal);
                half3 worldNormal = normalize(mul(combinedNormal, tangentToWorld));
                
                // Water color based on depth with infinite ocean blending
                half4 nearWaterColor = lerp(_ShallowColor, _WaterColor, saturate(depthDiff * 2.0));
                half4 waterColor = nearWaterColor;
                
                // Infinite ocean color blending
                if (_EnableInfiniteOcean > 0.5)
                {
                    float oceanBlend = smoothstep(_FarOceanFadeDistance - 0.1, _FarOceanFadeDistance + 0.1, normalizedDistance);
                    waterColor = lerp(nearWaterColor, _FarOceanColor, oceanBlend * _HorizonBlend);
                }
                
                // Foam calculation with improved smoothing (disabled for far ocean)
                half foamMask = 0.0;
                if (normalizedDistance < _FarOceanFadeDistance)
                {
                    // Multi-scale foam noise for more organic patterns
                    half2 foamUV1 = input.uv * _NoiseScale + _Time.y * 0.05; // Slower movement
                    half2 foamUV2 = input.uv * _NoiseScale * 2.0 + _Time.y * 0.08;
                    half foamNoise1 = SAMPLE_TEXTURE2D(_FoamNoise, sampler_FoamNoise, foamUV1).r;
                    half foamNoise2 = SAMPLE_TEXTURE2D(_FoamNoise, sampler_FoamNoise, foamUV2).r;
                    half foamNoise = lerp(foamNoise1, foamNoise2, 0.5);
                    
                    // Smoother depth-based foam with better falloff
                    half depthFoam = 1.0 - depthDiff;
                    depthFoam = smoothstep(_FoamCutoff - _FoamSmoothness, _FoamCutoff + _FoamSmoothness, depthFoam);
                    
                    // Apply shoreline smoothing to reduce twitchiness
                    depthFoam = lerp(depthFoam, smoothstep(0.0, _ShorelineSmoothing, depthFoam), 0.7);
                    
                    foamMask = depthFoam * _FoamIntensity * foamNoise;
                    foamMask *= (1.0 - normalizedDistance); // Fade foam with distance
                    foamMask = saturate(foamMask);
                }
                
                // Blend foam with water color
                half4 finalColor = lerp(waterColor, _FoamColor, foamMask);
                
                // Fresnel effect for transparency
                half3 viewDir = normalize(cameraPos - input.worldPos);
                half fresnel = pow(1.0 - saturate(dot(worldNormal, viewDir)), _FresnelPower);
                
                // Apply transparency with fresnel and distance fading
                float distanceAlpha = lerp(_Transparency, 1.0, normalizedDistance * 0.3); // Slightly more opaque at distance
                finalColor.a = distanceAlpha * (1.0 - fresnel * 0.5) + foamMask;
                finalColor.a = saturate(finalColor.a);
                
                // Simple lighting with distance-based reduction
                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color;
                half NdotL = saturate(dot(worldNormal, mainLight.direction));
                
                // Reduce lighting complexity at distance
                float lightIntensity = lerp(0.8, 0.6, normalizedDistance);
                finalColor.rgb *= lightColor * NdotL * lightIntensity + (0.2 + normalizedDistance * 0.1);
                
                // Add specular highlights (reduced at distance)
                float specularReduction = lerp(1.0, 0.2, normalizedDistance);
                half3 halfVector = normalize(mainLight.direction + viewDir);
                half NdotH = saturate(dot(worldNormal, halfVector));
                half specular = pow(NdotH, _Smoothness * 128.0) * _Smoothness * specularReduction;
                finalColor.rgb += specular * lightColor * 0.5;
                
                // Add subtle horizon glow for infinite ocean
                if (_EnableInfiniteOcean > 0.5 && normalizedDistance > _FarOceanFadeDistance)
                {
                    float horizonGlow = pow(saturate(dot(viewDir, half3(0, 1, 0))), 3.0) * 0.1;
                    finalColor.rgb += horizonGlow * lightColor;
                }
                
                return finalColor;
            }
            ENDHLSL
        }
        
        // Shadow casting pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            
            // Shadow keywords
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 texcoord     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 worldPos     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Reuse the same properties from main pass
            CBUFFER_START(UnityPerMaterial)
                half _WaveSpeed;
                half _WaveHeight;
                half _WaveFrequency;
                half4 _WaveDirection;
                half _TideHeight;
                half _TideSpeed;
                half _EnableInfiniteOcean;
                half _DistanceWaveReduction;
                // Other properties not needed for shadows
                half4 _WaterColor;
                half4 _ShallowColor;
                half4 _FoamColor;
                half _Transparency;
                half _Smoothness;
                half _Metallic;
                half _FresnelPower;
                half _FoamDistance;
                half _FoamIntensity;
                half _FoamCutoff;
                half _FoamSmoothness;
                half _ShorelineSmoothing;
                half4 _FarOceanColor;
                half _FarOceanFadeDistance;
                half _HorizonBlend;
                half _NormalStrength;
                half4 _NormalSpeed;
                half _NormalScale;
                half _SecondaryNormalStrength;
                half4 _SecondaryNormalSpeed;
                half _NoiseScale;
                float4 _NormalMap_ST;
                float4 _SecondaryNormalMap_ST;
                float4 _FoamNoise_ST;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;
            
            // Simple shadow bias implementation
            float3 ApplyShadowBias(float3 positionWS, float3 normalWS, float3 lightDirection)
            {
                float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
                float scale = invNdotL * 0.01; // Basic shadow bias
                positionWS = lightDirection * scale + positionWS;
                return positionWS;
            }

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0; // Initialize all fields to zero
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                
                // Apply the same wave displacement as main pass for consistent shadows
                float3 cameraPos = _WorldSpaceCameraPos;
                float distanceFromCamera = distance(worldPos, cameraPos);
                float maxDistance = _ProjectionParams.z;
                float normalizedDistance = saturate(distanceFromCamera / maxDistance);
                
                // Infinite ocean vertex expansion (same as main pass)
                if (_EnableInfiniteOcean > 0.5)
                {
                    float3 directionFromCamera = normalize(worldPos - cameraPos);
                    float farDistance = maxDistance * 0.98;
                    worldPos = cameraPos + directionFromCamera * farDistance;
                    
                    // Keep original Y position for water level consistency
                    float originalY = TransformObjectToWorld(input.positionOS.xyz).y;
                    worldPos.y = originalY;
                }
                
                // Wave calculations (same as main pass with multi-wave system)
                float time = _Time.y * _WaveSpeed;
                float2 waveDir = normalize(_WaveDirection.xy);
                float wavePhase = dot(worldPos.xz, waveDir) * _WaveFrequency + time;
                float waveReduction = lerp(1.0, _DistanceWaveReduction, normalizedDistance);
                
                // Multi-wave system for consistent shadows
                float primaryWave = sin(wavePhase) * _WaveHeight;
                float secondaryWave = sin(wavePhase * 1.7 + time * 0.8) * _WaveHeight * 0.3;
                float tertiaryWave = sin(wavePhase * 2.3 - time * 0.6) * _WaveHeight * 0.15;
                
                // Enhanced tide calculation
                float tidePhase = worldPos.x * 0.08 + worldPos.z * 0.03 + _Time.y * _TideSpeed;
                float tide = sin(tidePhase) * _TideHeight * 0.5;
                tide += sin(tidePhase * 1.3 + _Time.y * _TideSpeed * 0.7) * _TideHeight * 0.2;
                
                float displacement = (primaryWave + secondaryWave + tertiaryWave) * waveReduction + tide * waveReduction;
                
                // Apply displacement
                float3 localPos = input.positionOS.xyz;
                localPos.y += displacement;
                
                if (_EnableInfiniteOcean > 0.5)
                {
                    output.worldPos = worldPos;
                    output.worldPos.y += displacement;
                    output.positionCS = TransformWorldToHClip(output.worldPos);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(localPos);
                    output.worldPos = TransformObjectToWorld(localPos);
                }

                output.uv = input.texcoord;
                
                // Apply shadow bias
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 clipPos = output.positionCS;
                
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - output.worldPos);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.worldPos, normalWS, lightDirectionWS));
                
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                // For transparent water, we can return the alpha
                // But for shadow casting, we typically want to be fully opaque
                return 0;
            }
            ENDHLSL
        }
    }
    
    Fallback "Universal Render Pipeline/Lit"
}