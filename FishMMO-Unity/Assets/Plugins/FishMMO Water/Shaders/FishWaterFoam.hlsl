#ifndef FISH_WATER_FOAM_INCLUDED
#define FISH_WATER_FOAM_INCLUDED

// ─────────────────────────────────────────────────────────────────────────────
// Foam: how much there is, what that much foam looks like, and how it is lit — one model for the sea's white
// caps and surf (FishWaterShading.hlsl), the swash on the sand (FishWaterShore.shader) and the rivers'
// foam (FishInlandWater.hlsl), so all of it is the same stuff.
//
// WHAT WAS WRONG. Every foam was a threshold through the R blotch mask of WaterFoam.png, a contrasted smooth
// noise. A cut through a smooth field is a soft blob at any level, so the caps, the surf, the swash and the
// rivers all drew as cotton wool pasted onto the water: no bubbles, no holes, no threads, opaque to the edge,
// lit brighter than the water round it, and at night faintly glowing (a constant 0.1 of light).
//
// THE MODEL. A foam source says only how much foam there is — its COVERAGE, the share of the surface that is
// white (0 … 1). The map's ALPHA is a lace equalised so that cutting it at 1 − c covers exactly c of the area
// (WaterTextureBaker.FoamLace), and built the way foam decays: dense froth with round holes, a web of thin
// walls, then strands and clumps. So a thinning foam dissolves through those stages by itself, and a source
// never picks a look. Where a patch is deep inside its cut it is thick and opaque; at its lace it is thin and
// lets the water through. Under and round it the water is aerated and milky. Past where a pixel can show the
// lace, the cut gives way to the coverage itself, an even lightening by the foam's share.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The foam map's size in texels (WaterTextureBaker.FoamSize): how much lace one tile holds.</summary>
#define FISH_FOAM_LACE_TEXELS 1024.0

/// <summary>What a cut through the lace gives at a pixel.</summary>
struct FishFoam
{
	/// <summary>How much of the pixel is foam, 0 … 1 (antialiased).</summary>
	half cover;
	/// <summary>How thick that foam is, 0 thin lace … 1 the body of a patch.</summary>
	half body;
	/// <summary>The foam's share of the surface round the pixel (the coverage asked for): what aerates the water.</summary>
	half share;
};

/// <summary>
/// Two flow-map copies of the lace (the same tile carried for one cycle each, half a cycle apart), blended so
/// the mix keeps the lace's contrast. A plain lerp of two independent laces halves their spread mid-blend, and
/// the cut then lost every hole and thread twice a cycle: the foam pulsed between lace and a grey smear.
/// </summary>
half FishFoamBlend(half a, half b, half w)
{
	half m = lerp(a, b, w);
	half spread = sqrt(max(0.5h, w * w + (1.0h - w) * (1.0h - w)));
	return saturate(0.5h + (m - 0.5h) / spread);
}

/// <summary>
/// Cuts the lace for a coverage. <paramref name="lace"/> is the map's alpha (or a blend of copies of it);
/// <paramref name="metresPerPixel"/> the ground one pixel covers and <paramref name="tileMetres"/> one tile of
/// the map, which say whether the lace can be seen there at all.
/// </summary>
FishFoam FishFoamCut(half lace, half coverage, float metresPerPixel, float tileMetres)
{
	FishFoam f;
	coverage = saturate(coverage);
	half over = lace - (1.0h - coverage);
	// Antialiased by the lace's own change across the pixel, never sharper than a soft wet edge.
	half edge = max(0.012h, (half)fwidth(lace));
	half crisp = saturate(over / edge + 0.5h);
	// The body: how far inside its cut the lace is. Thin foam is near the cut, the middle of a dense patch far above it.
	half body = saturate(over / max(0.05h, 0.6h * coverage));
	/* Whether a pixel can show the lace. Past about a texel a pixel the mips have averaged the lace toward its mean,
	 * and a cut through that is all or nothing — whole seas going white at a coverage of a half. There the foam is
	 * its coverage, evenly. */
	float texelsPerPixel = metresPerPixel * FISH_FOAM_LACE_TEXELS / max(0.01, tileMetres);
	half resolved = saturate(1.5h - 0.5h * (half)log2(max(1.0, texelsPerPixel)));
	f.cover = lerp(coverage, crisp, resolved);
	f.body = lerp(coverage, body, resolved);
	f.share = coverage;
	return f;
}

/// <summary>
/// Foam's light: a thick white scatterer. It wraps the sun's light round (it is lit from inside as much as
/// from the front), glows through when the sun is behind it (<paramref name="back"/>, the water's own
/// back-scatter term), and takes the sky as diffuse <paramref name="ambient"/>. No light of its own: at night
/// it is as dark as the night.
/// </summary>
half3 FishFoamLight(half3 albedo, half3 lightColor, half NdotL, half back, half3 ambient)
{
	/* At the old foam's peak in full sun and no brighter: the project draws without a tonemapper, so anything whiter
	 * clips to a flat blown-out patch with no lace left in it. */
	half wrap = saturate((NdotL + 0.4h) / 1.4h);
	return albedo * (lightColor * (wrap * 0.7h + back * 0.3h) + ambient);
}

/// <summary>
/// The water round and under the foam: full of bubbles, so paler and milkier the more foam there is,
/// lit the way the foam is (<paramref name="foamLit"/>). Smooth — no lace — because the bubbles are below
/// the surface and seen through it.
/// </summary>
half3 FishFoamAerate(half3 water, half share, half3 foamLit)
{
	half3 milk = foamLit * half3(0.55h, 0.72h, 0.74h);
	return lerp(water, milk, saturate(share) * 0.3h);
}

/// <summary>
/// The foam laid over the water: opaque where it is thick, thin lace letting the water show through (and
/// greyer, being bubble walls with water behind). Returns the colour; <paramref name="opacity"/> is what it
/// adds to the surface's alpha.
/// </summary>
half3 FishFoamOver(half3 water, FishFoam f, half3 foamLit, out half opacity)
{
	opacity = f.cover * lerp(0.55h, 0.97h, f.body);
	half3 foam = lerp(lerp(water, foamLit, 0.75h), foamLit, f.body);
	return lerp(FishFoamAerate(water, f.share, foamLit), foam, opacity);
}

#endif
