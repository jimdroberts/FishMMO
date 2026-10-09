// FishMMO/Vegetation Prime: the depth of a GPU-driven alpha-tested vegetation draw, laid ahead of its lit draw
// (TerrainGpuRenderer). Drawn into the camera's own target in the opaque pass one render queue earlier than the lit
// material, with the lit pass's exact coverage (FishVegetationPasses.hlsl VegPrimeFragment: the same alpha-to-coverage
// ramp, the same dissolves, the same deformed positions); the lit material is then drawn with ZWrite off
// (_VegZWrite 0) and the ordinary LEqual test, so it shades only the leaf in front at each pixel.
//
// Why: a leaf card's lit pass discards, and a discarding shader that writes depth gets no early depth rejection, so
// every layer of a crown ran the whole lit shader. URP's own depth priming would do this, but it is off under MSAA, and
// the leaves need MSAA for their alpha-to-coverage edges. The depth prepass URP runs for SSAO writes a separate,
// single-sampled texture, which the multisampled lit pass cannot test against.
//
// The Properties block is FishMMO/Vegetation Indirect's, so a clone of a lit material keeps every value it reads.
Shader "FishMMO/Vegetation Prime"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (A cutout)", 2D) = "white" {}
        [MainColor] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clip", Float) = 1.0
        [Toggle(_NORMALMAP)] _UseNormalMap("Normal map", Float) = 0.0
        [Normal] _BumpMap("Normal map", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.2
        _Translucency("Translucency (light through leaves)", Range(0.0, 2.0)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0.0
        [Toggle] _BackfaceFlip("Flip normal on back faces (flat parts; off for bent-normal cards)", Float) = 0.0
        // Off only where a depth-prime draw has already laid this material's depth (TerrainGpuRenderer, Vegetation Prime):
        // the lit pass then writes none, so a leaf hidden behind another fails the depth test before it is shaded.
        [HideInInspector] _VegZWrite("Lit pass writes depth", Float) = 1.0

        [Header(Wind)]
        _WindSway("Sway (m at sway weight 1, full wind)", Range(0.0, 2.0)) = 0.4
        _WindFlutter("Flutter (m)", Range(0.0, 0.2)) = 0.03
        _WindFrequency("Frequency", Range(0.1, 4.0)) = 1.2
        [Toggle] _Aquatic("Under the sea (sways with the surge instead of the wind, kept under the surface)", Float) = 0.0

        [Header(Season)]
        _HealthyColor("Healthy tint", Color) = (0.9, 0.95, 0.9, 1)
        _DryColor("Dry tint", Color) = (0.75, 0.7, 0.55, 1)
        _TintSpread("Tint spread between neighbours", Range(0.0, 1.0)) = 0.35
        [Toggle] _Deciduous("Deciduous (turns in autumn, bare in winter)", Float) = 0.0
        [HDR] _AutumnColor("Autumn multiplier", Color) = (1.6, 0.8, 0.3, 1)

        [Header(Weather)]
        _SnowBury("Sinks into deep snow", Range(0.0, 1.0)) = 0.0
        _FishWeatherAmount("Weather on this surface", Range(0.0, 1.0)) = 1.0

        [Header(Distance and ground)]
        [Enum(None, 0, Detail, 1, Tree, 2)] _DistanceFade("Distance fade (detail: before its patch is culled; tree: before the tree distance)", Float) = 0.0
        [Toggle] _FacingCamera("Turn to face the camera (tree billboards)", Float) = 0.0
        _GroundSink("Ground sink: x min, y max metres (each instance draws between them)", Vector) = (0, 0, 0, 0)
        _TintPatchMetres("Healthy/dry patch size (m, 0 = plant by plant)", Float) = 12

        [Header(Variation (each plant from its own hash))]
        _VaryLean("Lean (most degrees, about the root)", Range(0.0, 20.0)) = 0
        _VaryTwist("Twist (most degrees, at 10 m up)", Range(0.0, 90.0)) = 0
        _VaryCrown("Crown width (share either way, growing with height)", Range(0.0, 0.6)) = 0
        _VaryHeight("Height (share either way)", Range(0.0, 0.4)) = 0
        _VaryGirth("Trunk girth (share either way, more on the taller)", Range(0.0, 0.5)) = 0.22
        _VaryPartSwing("Each limb's swing about its foot (degrees either way)", Range(0.0, 60.0)) = 0
        _VaryPartDroop("Each limb's droop or rise (degrees either way)", Range(0.0, 40.0)) = 0
        _VaryPartLength("Each limb's length (share either way)", Range(0.0, 0.5)) = 0
        _VaryFullness("Fullness: x the fewest of the spare limbs kept, y the fewest leaves kept (0..1)", Vector) = (0, 0, 0, 0)
        _VaryColour("Colour: x brightness, y hue and saturation, z each leaf card, w bark (shares either way)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest-1"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "VegetationPrime"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0
            // The lit pass's coverage, sample for sample (its ForwardLit pass has AlphaToMask On too).
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VegDepthVertex
            #pragma fragment VegPrimeFragment

            // Always compiled, not a shader feature: nothing in a build references this shader's materials (they are made at
            // runtime), so a feature's variant would be stripped and the prime would lay solid cards over the leaves.
            #pragma multi_compile_local _ _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:FishIndirectSetup

            #define FISH_VEG_PASS_PRIME
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishIndirectInstancing.hlsl"
            #include "FishVegetationPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
