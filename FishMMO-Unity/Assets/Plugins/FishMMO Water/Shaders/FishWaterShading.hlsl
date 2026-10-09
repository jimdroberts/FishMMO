#ifndef FISHMMO_WATER_SHADING_INCLUDED
#define FISHMMO_WATER_SHADING_INCLUDED

// How the sea is lit, coloured and foamed — shared by the ocean (FishWaterForwardPass.hlsl) and the
// breakers that rise out of it (FishWaterBreaker.shader), so the two cannot differ where they meet.

#include "FishWaterWaves.hlsl"

#if defined(_WATER_DEPTH) || defined(_WATER_REFRACTION)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif
#if defined(_WATER_REFRACTION)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#endif
// The light the waves focus onto the sea bed: lit here too, for the sea bed seen by refraction.
#include "FishWaterCausticsCommon.hlsl"
// The weather's fog, which the fog passes laid over the frame BEFORE the sea was drawn.
#include "FishWaterFog.hlsl"
#include "FishWaterFoam.hlsl"

/// <summary>Eye-space depth of a raw depth sample, correct under an orthographic camera too.</summary>
float WaterEyeDepth(float rawDepth)
{
	#if UNITY_REVERSED_Z
		float ortho = lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
	#else
		float ortho = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
	#endif
	return lerp(LinearEyeDepth(rawDepth, _ZBufferParams), ortho, unity_OrthoParams.w);
}

/// <summary>
/// The ripples, from two scrolling copies of one tiling normal map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Textures, not sinusoids.</b> A small sum of sine waves is coherent: the components stay in
/// step with each other across the whole sea, and the result reads as corduroy — regular parallel
/// ribbing the eye locks onto however gently it is scaled. Real capillary waves are broadband and
/// isotropic, which is what a noise-built normal map is and what four sinusoids can never be.
/// </para>
/// <para>
/// <b>Addressed in world metres, not mesh UV.</b> The ocean disc has no meaningful UV, and a
/// generated scene's water plane spans kilometres, so a UV-addressed map would stretch to one
/// texel per hundreds of metres. In world XZ the tiling is a real distance and any mesh works.
/// </para>
/// <para>
/// <b>Two scales moving at different speeds and angles</b>, so neither the tile nor the scroll
/// direction is visible: the eye can find a repeat in one layer, not in the product of two.
/// </para>
/// </remarks>
float3 WaterRippleNormal(float2 xz, float strength)
{
	// The wind held for a window of the shared clock, never the eased one times the clock (FishWaterHeldScroll).
	float2 wind = _FishWaterRippleWindow.xy;
	float2 across = float2(-wind.y, wind.x);

	float2 uvA = xz / max(0.05, _NormalScaleA) + FishWaterHeldScroll(wind * (_WaveSpeed * 0.030));
	float2 uvB = xz / max(0.05, _NormalScaleB) - FishWaterHeldScroll((wind * 0.6 + across * 0.8) * (_WaveSpeed * 0.019));

	half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA), strength);
	half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB), strength * 0.7);

	// Whiteout blend: keeps the detail of both instead of averaging it away.
	return normalize(float3(a.xy + b.xy, a.z * b.z));
}

/// <summary>
/// Shades a piece of the sea: the open ocean, or a breaker rising out of it.
/// </summary>
/// <param name="positionWS">The point being drawn, where it actually is.</param>
/// <param name="flatXZ">
/// Where its textures are read: the still-water point the ocean's waves moved here, or the point of
/// the sea a breaker's sheet unrolls to. Wherever a breaker meets the sea the two are the same point,
/// which is what makes the seam vanish — the same ripples, the same foam, the same clarity.
/// </param>
/// <param name="screenPos">ComputeScreenPos of the drawn point.</param>
/// <param name="distanceToCamera">Metres, for the ripple fade.</param>
/// <param name="fogFactor">URP's fog factor for the drawn point.</param>
/// <param name="wave">
/// The surface's shape and state: its normal (ripples are added here, about it), height (the
/// light through a crest), folding (white caps), depth (sediment), calm (the fade over the shallows)
/// and surf — whitewater, 0 to 1, already scaled by whoever laid it.
/// </param>
/// <remarks>
/// <b>ONE function for both, and that is the seam.</b> The breakers used to be imaginable only as the
/// sea's own vertices thrown about; drawn as geometry of their own, they would show a line wherever
/// their colour, light, foam or clarity differed from the sea's by the smallest amount. So this was
/// the ocean's fragment stage, lifted out whole, and the breaker calls it with its own shape: at its
/// base, where its shape is the still water's, it returns the sea's colour to the bit.
/// </remarks>
half4 FishWaterShade(float3 positionWS, float2 flatXZ, float4 screenPos, float distanceToCamera,
	float fogFactor, FishWaterSurface wave)
{
	float2 screenUV = screenPos.xy / screenPos.w;
	float surfaceEye = screenPos.w;

	float3 view = _WorldSpaceCameraPos - positionWS;
	float viewLength = max(1e-4, length(view));
	view /= viewLength;

	// ── Normal ────────────────────────────────────────────────────────
	//
	// The ripple strength falls off with distance and the loss is put back as roughness, which is
	// what those ripples do to the light anyway. Without it the specular highlight past a few
	// hundred metres becomes a field of crawling sparkles.
	float detailFade = saturate(1.0 - distanceToCamera / max(1.0, _NormalFadeDistance));
	float3 ripple = WaterRippleNormal(flatXZ, _NormalStrength * detailFade);

	// Applied about the WAVE normal, which is the only frame here that means anything: the mesh
	// has no tangents, and one invented from world up seams wherever a wave face passes vertical.
	float3 waveNormal = wave.normalWS;
	float3 tangent = normalize(cross(float3(0.0, 1.0, 0.0), waveNormal) + float3(1e-4, 0.0, 0.0));
	float3 bitangent = cross(waveNormal, tangent);
	float3 normalWS = normalize(ripple.x * tangent + ripple.y * bitangent + ripple.z * waveNormal);

	/* Flattened as the view goes grazing, and this is not a cheat.
	 *
	 * A pixel at a glancing angle covers a long, thin strip of surface carrying many ripples, and
	 * what reaches the eye is their AVERAGE — which is the flat wave normal, not any one ripple's.
	 * Left unflattened, the perturbation scatters N·V across the whole Fresnel range at exactly
	 * the angle where Fresnel is steepest, so neighbouring pixels alternate between mirror and
	 * clear water and the distance reads as dark speckled mud. This is also what stops shallow
	 * water at a low angle from showing a long dark column where it should show sky. */
	half grazing = saturate(dot(waveNormal, view) * 3.0);
	normalWS = normalize(lerp(waveNormal, normalWS, grazing));

	bool underwater = _WorldSpaceCameraPos.y < _FishWaterLevel;
	if (underwater)
	{
		normalWS = -normalWS;
	}

	// ── Light ─────────────────────────────────────────────────────────
	float4 shadowCoord;
	#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
		shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
	#else
		shadowCoord = TransformWorldToShadowCoord(positionWS);
	#endif
	Light mainLight = GetMainLight(shadowCoord);
	half shade = mainLight.shadowAttenuation * saturate(_FishWaterCloudShadow);
	half3 lightColor = mainLight.color * shade;
	half NdotL = saturate(dot(normalWS, mainLight.direction));

	/* What lights the water ITSELF.
	 *
	 * The colour of a body of water is not a pigment — it is sunlight and skylight that went in,
	 * scattered, and came back out, so it must be multiplied by the light like any other
	 * reflectance. Left as a raw constant, deep water renders as a hole cut in the world.
	 *
	 * Keyed to how high the sun stands rather than to NdotL: that is the mirror direction, and it
	 * is already spent on the reflection and the glint. The floor is the light arriving by every
	 * path not modelled here, and without it the sea goes to literal black wherever the sun is low
	 * or shadowed — this project runs no tonemapper to lift it back out.
	 */
	half sunHeight = saturate(mainLight.direction.y);
	half3 waterLight = lightColor * sunHeight * 0.9 + _GlossyEnvironmentColor.rgb * 0.8 + 0.12;

	// ── How much water is in front of what is behind ──────────────────
	float waterColumn = 1000.0;
	float edgeFade = 1.0;
	#if defined(_WATER_DEPTH)
		float rawDepth = SampleSceneDepth(screenUV);
		float sceneEye = WaterEyeDepth(rawDepth);
		// Along the view ray, which is the path the light actually took through it.
		waterColumn = max(0.0, sceneEye - surfaceEye);
		// Soft-particle intersection fade: no hard line where the sea meets the sand.
		edgeFade = saturate(waterColumn / max(0.01, _EdgeFade));
	#endif

	/* How clear the water is here. A real sea is never one tint: plankton, sediment and the
	 * currents that carry them leave patches hundreds of metres across, and without them an ocean
	 * is one flat colour whose only variation is the waves. Drifting with the held wind, a whole tile or
	 * two a window (FishWaterHeldScroll): wind × clock slid the patches whenever the wind turned. */
	float2 clarityUV = flatXZ / max(20.0, _ClarityScale) - FishWaterHeldScroll(_FishWaterRippleWindow.xy * 0.004);
	float clarityField = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, clarityUV).g;
	half3 density = max(1e-3, _WaterDensity.rgb * lerp(1.0 + _Clarity, 1.0 - _Clarity, clarityField));

	/* Beer-Lambert, PER CHANNEL. Red is absorbed in the first couple of metres and blue survives
	 * tens, which is the entire reason deep water is blue and a hand held underwater goes grey.
	 * A single scalar density makes shallow water a paler version of deep water instead. */
	half3 transmittance = exp(-waterColumn * density);
	half3 open = lerp(_DeepColor.rgb, _ShallowColor.rgb, transmittance);

	/* Shore sediment, separate from absorption on purpose. Absorption alone only makes shallow
	 * water paler; real shallows are GREENER and browner, because a shore stirs sand and silt into
	 * water that is clear half a kilometre out. Squared so it stays in the surf zone. */
	half sediment = 1.0 - saturate(wave.depth / max(0.05, _ShoreDepth));
	half3 bodyColor = lerp(open, _ShoreColor.rgb, sediment * sediment) * waterLight;

	// ── What is behind it ─────────────────────────────────────────────
	half3 behind = bodyColor;
	half alpha = _MaxAlpha;
	#if defined(_WATER_REFRACTION)
		// Offset scaled by 1/depth so a ripple bends the sea bed by the same number of METRES near
		// and far; an offset in screen pixels makes distant water look like frosted glass.
		float2 offset = normalWS.xz * _RefractionStrength / max(1.0, surfaceEye);
		float2 refractedUV = screenUV + offset;

		#if defined(_WATER_DEPTH)
			/* The edge case every refractive water gets wrong: if what is at the offset UV is IN
			 * FRONT of the water it is not behind the water at all, and sampling it smears a
			 * character standing at the shoreline across the surface. Reject and go straight. */
			float refractedRaw = SampleSceneDepth(refractedUV);
			float refractedEye = WaterEyeDepth(refractedRaw);
			bool straight = refractedEye < surfaceEye;
			refractedUV = straight ? screenUV : refractedUV;
			float seabedRaw = straight ? rawDepth : refractedRaw;
			waterColumn = max(0.0, max(refractedEye, sceneEye) - surfaceEye);
			transmittance = exp(-waterColumn * density);
			open = lerp(_DeepColor.rgb, _ShallowColor.rgb, transmittance);
			bodyColor = lerp(open, _ShoreColor.rgb, sediment * sediment) * waterLight;
		#endif

		half3 refracted = SampleSceneColor(refractedUV);
		#if defined(_WATER_DEPTH)
			/* The copy of the frame this refracts was taken before any transparent pass ran — the
			 * caustics pass among them — so the sea bed it shows has no caustics on it, and the sea
			 * puts them there itself: the same light, from the same function, at the point the
			 * refracted ray actually reaches. Seen from below, the frame itself is lit instead. */
			if (!underwater)
			{
				refracted *= FishWaterCausticLight(FishWaterSceneWorldPosition(refractedUV, seabedRaw));
			}
		#endif
		// What survived the column, plus the water's own scattered light. This is why shallow
		// water shows the sand and deep water does not.
		behind = refracted * transmittance + bodyColor * (1.0 - transmittance);
		alpha = 1.0;   // the background is composited into rgb already
	#else
		alpha = _MaxAlpha * lerp(0.35, 1.0, 1.0 - Luminance(transmittance));
	#endif

	// ── Reflection, Fresnel and glint ─────────────────────────────────
	//
	// F0 = 0.02 is not a tuning value: it falls out of water's refractive index of 1.333, and it
	// is why water is almost a mirror at a grazing angle and almost clear looking straight down.
	half NdotV = saturate(dot(normalWS, view));
	half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

	half perceptualRoughness = saturate((1.0 - _Smoothness) + (1.0 - detailFade) * 0.06);
	/* Specular anti-aliasing (Tokuyoshi & Kaplanyan): where the normal turns faster than a pixel can follow, the
	 * pixel sees a spread of facets, not one, and its highlight must be the rougher average of them. Without it the
	 * sun's glint on waves a few hundred metres off broke into a field of white pinpoints that read as foam. */
	float3 dndx = ddx(normalWS), dndy = ddy(normalWS);
	half normalVariance = saturate(0.25 * (dot(dndx, dndx) + dot(dndy, dndy)));
	perceptualRoughness = saturate(sqrt(perceptualRoughness * perceptualRoughness + min(2.0 * normalVariance, 0.25)));
	float3 reflectVector = reflect(-view, normalWS);
	// A reflection bent below the horizon by a steep crest picks up the ground half of the probe
	// and smears brown across the wave; real water at that angle reflects the sky past the crest.
	reflectVector.y = abs(reflectVector.y) * (underwater ? -1.0 : 1.0);
	half3 reflection = GlossyEnvironmentReflection(reflectVector, positionWS,
		perceptualRoughness, 1.0h, screenUV) * _ReflectionStrength;

	// GGX, so the sun's highlight is a long streak across the chop rather than a round blob.
	half3 halfVector = SafeNormalize(mainLight.direction + view);
	half NdotH = saturate(dot(normalWS, halfVector));
	half roughness = max(1e-3, perceptualRoughness * perceptualRoughness);
	half a2 = roughness * roughness;
	half d = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
	half3 glint = lightColor * (a2 / max(1e-4, 3.14159 * d * d)) * _SpecularStrength * fresnel * NdotL;

	// Sub-surface: light that entered a crest and came out toward the eye, which is what makes a
	// wave glow green with the sun behind it. Keyed to the wave's own height, so only on crests.
	half back = pow(saturate(dot(view, -mainLight.direction)) * 0.5 + 0.5, 3.0);
	half crestLift = saturate(wave.height * 0.5 + 0.35);
	half3 scatter = _ScatterColor.rgb * lightColor * back * crestLift * _ScatterStrength;

	half3 color = lerp(behind + scatter, reflection, fresnel) + glint;

	/* Seen from below, the surface is Snell's window: the whole sky squeezed into a cone 48.6
	 * degrees wide, and a mirror of the water outside it. The critical angle is not tuned — it is
	 * asin(1/1.333), and refract() reports it for free by returning zero when the ray cannot
	 * escape. The normal it wants is the one on the incident side, which underwater is the flipped
	 * one; handing it the other quietly returns a direction pointing back DOWN and the window
	 * renders as a brown hole. */
	if (underwater)
	{
		float3 transmitted = refract(-view, normalWS, 1.333);
		half3 mirrored = _ShallowColor.rgb * waterLight * 0.45;

		/* How much of the world above comes through: nothing outside the window, where every ray
		 * is reflected back down, and a Fresnel falloff inside it taken on the WATER's side — the
		 * transmitted ray's angle, not the eye's — so the window's rim brightens into the mirror the
		 * way it does from under a real surface, rather than stopping at a hard circle. */
		half through = 0.0;
		if (dot(transmitted, transmitted) > 1e-5)
		{
			half cosTransmitted = saturate(dot(normalize(transmitted), -normalWS));
			through = 1.0 - (0.02 + 0.98 * pow(1.0 - cosTransmitted, 5.0));
		}

		/* What is up there is the actual scene, not the sky's probe. From below, the camera's
		 * opaque pass has already drawn whatever stands above the water behind this surface — the
		 * shore, the trees, the sky — so the window shows that: bent by the waves where there is a
		 * copy of the frame to bend, and simply let through where there is not. It used to be the
		 * reflection probe on an opaque surface, which is why looking up from below showed a clean
		 * sky and nothing that was actually there. */
		#if defined(_WATER_REFRACTION)
			float2 bent = screenUV + normalWS.xz * _RefractionStrength / max(1.0, surfaceEye);
			color = lerp(mirrored, SampleSceneColor(bent), through);
			alpha = 1.0;
		#else
			color = mirrored;
			alpha = saturate(1.0 - through * 0.95);
		#endif
	}

	// ── Foam ──────────────────────────────────────────────────────────
	half foamAlpha = 0.0;
	if (!underwater)
	{
		/* Drifting shoreward in the surf, with the wind everywhere else. Foam on a beach is carried
		 * by the breaking wave, so there the mask has to travel the way the surf travels or it
		 * slides sideways through it; out at sea it is the wind's. The direction to the shore is
		 * defined everywhere — it points at the nearest one from two kilometres out — so it is the
		 * depth that says where the surf is.
		 *
		 * A FLOW MAP, never direction × clock. The drift was the flow times the whole clock, and a
		 * flow that turns across the water — in every inlet, round every headland, wherever two
		 * shores' pulls meet — pulled the texture apart there by thousands of metres of offset: a
		 * starburst of radial streaks that grew worse every minute the game ran. Now two copies are
		 * each carried for one short cycle, half a cycle apart, and blended so that each restarts
		 * while the other is showing: no copy is ever further from home than one cycle's travel. The
		 * flow also fades where the way to the shore is unsure — on the ridge between two shores it
		 * flips from one to the other. */
		float2 shoreward;
		float facing = FishWaterShoreFacing(flatXZ, shoreward);
		float inSurf = facing * (1.0 - smoothstep(0.75, 1.0, wave.depth / max(8.0, _FishWaterBreakDepth.y)));
		float2 flow = lerp(_FishWaterWind.xy, shoreward, inSurf) * 0.5;
		// Six seconds, snapped to a whole fraction of the clock's 10 000 s wrap so the wrap lands on a cycle's start.
		const float FlowCycle = 10000.0 / round(10000.0 / 6.0);
		float phase = frac(_FishWaterTime / FlowCycle);
		float phaseOther = frac(phase + 0.5);
		half other = abs(2.0 * phase - 1.0);
		float2 foamUV = (flatXZ + flow * (phase * FlowCycle)) / max(0.5, _FoamScale);
		float2 foamOtherUV = (flatXZ + flow * (phaseOther * FlowCycle)) / max(0.5, _FoamScale) + float2(0.41, 0.23);
		// The lace (the map's alpha, FishWaterFoam.hlsl): the two carried copies blended so the blend keeps its holes.
		half lace = FishFoamBlend(
			SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV).a,
			SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamOtherUV).a,
			other);

		/* NO shoreline foam here.
		 *
		 * The band at the water's edge, the foam the swash carries and the wet sand behind it all
		 * belong to the shore pass, which can place them against the BEACH — a surface that stops
		 * at the waterline cannot draw the thing that happens past it. What stays here is the foam
		 * that belongs to the sea itself: breaking crests and white caps. */

		// Whitewater: the breakers' own, on their sheets, and the bore they leave on the still water
		// inshore — the band of white that rolls in ahead of the swash.
		half surf = saturate(wave.surf);

		/* White caps, where the Gerstner Jacobian falls BELOW the threshold, not merely below one
		 * — it is below one over the whole upper half of every wave, so the looser reading foams
		 * the entire sea. 0.70 is measured: the Jacobian drops under it on 0% of the sea below
		 * 8 m/s of wind, 1.6% at 12 and 7% at 16, against a real ocean's 0% below 6 and 2-5% at
		 * 10-12. Past ~15 m/s a developed sea stops getting steeper while a real one goes on
		 * whitening, and _FishWaterWhitecap carries that part. */
		half threshold = lerp(_WhitecapThreshold, 0.80, saturate(_FishWaterWhitecap));

		/* And where the sea is being faded out over the shallows. That band is where a real sea
		 * steepens and breaks, and the energy the fade takes out of the waves is the energy a real
		 * one spends as foam — so the white caps there take a lower bar. They follow the folding of
		 * the sea as it would have been, which the fade leaves alone, so they sit on the crests the
		 * eye expects and it is them, not the waves' going, that it sees. Inshore of the break line
		 * the water is still and the caps give way to the breakers' bore. */
		float breakDepth = max(0.01, _FishWaterBreakDepth.x);
		half fading = (1.0 - wave.calm) * smoothstep(0.5 * breakDepth, breakDepth, wave.depth);
		half open = saturate(wave.calm + fading);
		threshold = lerp(threshold, 0.97, _ShallowWhitecaps * fading);
		half crest = saturate((threshold - wave.jacobian) / max(0.01, threshold)) * open;
		/* What the crests left: the foam memory (WaterFFT) keeps how far past the open sea's threshold each one
		 * broke, decaying, on the same scale as the crest above. A cap is a crest that is breaking NOW, and the patch
		 * of foam it leaves behind it as it runs on, thinning from froth to lace to strands; without the memory the
		 * caps blinked on and off with every wave. */
		half kept = saturate(wave.breakingKept / max(0.01, threshold)) * open;

		/* Each term gets its own curve, and the sharpness is applied to the CREST term rather than
		 * to the product. Applied afterwards it cancelled the calibration: a genuinely breaking
		 * crest scores about 0.18, the mask takes it to 0.14, and a curve starting at 0.15 returns
		 * zero — white caps on one crest in the frame and nowhere else. */
		half curveTop = max(0.06, _FoamSharpness);
		half breaking = smoothstep(0.02, curveTop, crest);
		half lingering = smoothstep(0.0, 2.0 * curveTop, kept);
		/* The COVERAGE each asks for — the share of the surface that is white — and the lace says what that much
		 * foam looks like. A breaking crest is mostly froth; what it leaves is less and less. Past a metre or so
		 * per pixel the lace cannot be seen and a capped sea at a distance is a faint, even lightening by the
		 * caps' share of it (FishFoamCut), which is far less than a crest's own. */
		float footprint = length(fwidth(flatXZ));
		half far = saturate(footprint / 0.5);
		half caps = max(breaking * 0.85, lingering * 0.55) * lerp(1.0, 0.2, far);
		// The surf: the breakers' whitewater, as dense as the breaker says, up to the material's opacity.
		half surfing = smoothstep(0.02, curveTop, surf) * _SurfFoamOpacity;
		half coverage = max(caps, surfing);

		/* Foam is a rough white surface: it takes the sky as diffuse ambient, not as a reflection, and the
		 * sun wraps round it. And a thin one: with the sun behind it, light comes THROUGH it, as through
		 * the crest it rides on. Lit by the front alone, the foam on a back-lit breaker's face drew duller
		 * than the glowing water under it — dark lace on a bright wave. The same back-light the scatter
		 * uses. Its own light (a constant 0.1) is gone: foam at night is as dark as the night. */
		FishFoam foamCut = FishFoamCut(lace, coverage, footprint, _FoamScale);
		half3 foamLit = FishFoamLight(_FoamColor.rgb, lightColor, NdotL, back, _GlossyEnvironmentColor.rgb * 0.6);
		half foam;
		color = FishFoamOver(color, foamCut, foamLit, foam);
		foamAlpha = foam;
	}

	/* The soft edge applies to the WATER, and the foam is laid over the top of it. The other order
	 * fades the swash out exactly where it matters: a sheet of water running up wet sand is a
	 * centimetre thick and almost entirely foam, so multiplying that foam by its own thinness
	 * erases the thing being drawn. */
	alpha = saturate(alpha * edgeFade);
	alpha = max(alpha, foamAlpha * _MaxAlpha);

	/* One term at a time, drawn opaque, for finding what a finished image cannot say. The dark
	 * patches in the shallows were guessed at from finished renders all one day, and every guess
	 * was wrong — a colour is the product of a dozen terms and the eye cannot factor it. With this
	 * the product is taken apart on screen, and the term that goes dark where the water does is the
	 * answer. Uniform across the draw, so the branch costs nothing when it is off. */
	// 13, the breakers' sheet, is drawn by the breaker pass itself; the sea shades normally under it.
	if (_DebugView > 0.5 && _DebugView < 12.5)
	{
		int term = (int)(_DebugView + 0.5);
		half3 shown = half3(1.0, 0.0, 1.0);
		if (term == 1) shown = sediment.xxx;
		else if (term == 2) shown = clarityField.xxx;
		else if (term == 3) shown = transmittance;
		else if (term == 4) shown = fresnel.xxx;
		else if (term == 5) shown = alpha.xxx;
		else if (term == 6) shown = waterLight * 0.5;
		else if (term == 7) shown = shade.xxx;
		else if (term == 8) shown = saturate(wave.depth / 10.0).xxx;
		else if (term == 9) shown = bodyColor;
		else if (term == 10) shown = behind;
		else if (term == 11) shown = reflection;
		else if (term == 12) shown = foamAlpha.xxx;
		return half4(shown, 1.0);
	}

	color = MixFog(color, fogFactor);
	/* Then the weather's clouds and its fog, which the passes that draw them laid over the frame
	 * before the sea was drawn, in the order they did: without them the sea painted over every cloud
	 * between it and the camera — seen from any height, the water plane cut the clouds off — and
	 * stayed bright and clear through the thickest fog on the ground. Only from above — from below,
	 * what lies between the eye and the surface is water. */
	if (!underwater)
	{
		color = FishWaterBehindClouds(color, screenUV);
		// The air's fog on top, only where the cloud buffer did not already carry it: once the cloud
		// march draws the fog (_FishAirFogRange.z = 1), the buffer just laid over the sea holds it,
		// and applying the analytic fog as well fogged the sea twice.
		if (_FishAirFogRange.z < 0.5)
		{
			half fogKeep;
			color = FishWaterAirFog(color, positionWS, screenUV, fogKeep);
		}
	}
	return half4(color, alpha);
}

#endif
