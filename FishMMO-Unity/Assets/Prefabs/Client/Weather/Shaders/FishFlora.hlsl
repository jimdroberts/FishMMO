#ifndef FISHMMO_FLORA_INCLUDED
#define FISHMMO_FLORA_INCLUDED

// The looks of the scene's biomes on its world (BiomeLooks, published by FloraPaletteMap, FishMMO.Client/World/Terrain):
// every biome has twelve looks for its grass, flowers, small plants, fungi and stones, and each patch of ground takes one,
// by weight. Texel loads only, no samplers, so the grass compute, vertex and fragment stages all read it alike.
// _FishFloraInfo.x is 0 when nothing is published (edit mode, A/B off): FishFloraAt then returns valid = false and every
// caller draws as it did before.
//
// Plain HLSL on purpose (no Core RP macros): FishGrassBlades.compute includes it.

Texture2D<float> _FishFloraBiomes;     // R8: the biome map grid, each texel the biome's palette index (0 = the scene's own look)
Texture2D<float4> _FishFloraPalette;   // 12 wide, 7 rows a biome, linear: foliage (a running weight), root (a height),
                                       // bloom (a bloom size), accent (a width), mineral (a bend), living green's shift, the biome's average shift
float4 _FishFloraRect;                 // xy the grid's world xz origin, zw one over its xz size (0 when the scene has no map)
float4 _FishFloraInfo;                 // x 1 when on, y grid width, z grid height, w biomes in the palette
float4 _FishFloraStrength;             // how far each takes its look: x foliage, y bloom, z accent, w mineral
float4 _FishFloraWorld;                // x how tall living things grow on this world, y how broad (BiomeLooks.GrowthOf; 1 off)
float4 _FishFloraGround;               // rgb the world's bare-ground colour (linear), a how far mineral ground takes it (0 under ordinary air)

#define FISH_FLORA_VARIANTS 12
#define FISH_FLORA_ROWS 7

// The blade grass's patches (FishGrassBlades: compute and vertex). The terrain's living ground reads the same, so the
// sward under a patch of golden grass is golden too.
#define FISH_FLORA_GRASS_PATCH 6.0
#define FISH_FLORA_GRASS_SALT 0x6A09E667u

struct FishFloraLook
{
	bool valid;
	uint biome;      // palette index
	uint variant;    // 0 … 11; 10 and 11 are the rare fantastic ones
	float3 foliage;  // linear
	float3 root;
	float3 bloom;
	float3 accent;
	float3 mineral;  // about white
	float3 shift;    // living green's multiplier: this look against the biome's common look on the home world (white there)
	float3 biomeShift; // the biome's looks' shifts averaged by weight: what the ground colour map carries
	float height;    // against the type's own
	float width;
	float bend;
	float bloomSize;
};

uint FishFloraHash(uint x)
{
	x ^= x >> 16; x *= 0x7feb352du;
	x ^= x >> 15; x *= 0x846ca68bu;
	x ^= x >> 16;
	return x;
}

float FishFloraUnit(uint x)
{
	return (FishFloraHash(x) >> 8) * (1.0 / 16777216.0);
}

uint FishFloraCellHash(int2 cell, uint salt)
{
	return FishFloraHash((uint)cell.x * 0x8da6b343u ^ (uint)cell.y * 0xd8163841u ^ salt * 0xcb1ab31fu);
}

// A smooth value noise in [0, 1], for warping the patch grid into blobs.
float FishFloraNoise(float2 p, uint salt)
{
	int2 i = (int2)floor(p);
	float2 f = p - floor(p);
	float2 u = f * f * (3.0 - 2.0 * f);
	float a = (FishFloraCellHash(i, salt) >> 8) * (1.0 / 16777216.0);
	float b = (FishFloraCellHash(i + int2(1, 0), salt) >> 8) * (1.0 / 16777216.0);
	float c = (FishFloraCellHash(i + int2(0, 1), salt) >> 8) * (1.0 / 16777216.0);
	float d = (FishFloraCellHash(i + int2(1, 1), salt) >> 8) * (1.0 / 16777216.0);
	return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// The palette index of the biome under xz. Borders are dithered over about a grid cell by a jittered read, so two
// biomes' grass mingles instead of meeting on a line.
uint FishFloraBiomeAt(float2 xz)
{
	if (_FishFloraRect.z <= 0.0)
	{
		return 0u;
	}
	float2 size = _FishFloraInfo.yz;
	float2 uv = (xz - _FishFloraRect.xy) * _FishFloraRect.zw;
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 0u;
	}
	uint h = FishFloraCellHash((int2)floor(xz * 1.7), 0x51u);
	float2 jitter = float2((h & 0xffffu) * (1.0 / 65535.0), (h >> 16) * (1.0 / 65535.0)) - 0.5;
	int2 texel = clamp((int2)floor(uv * size + jitter), int2(0, 0), (int2)size - 1);
	uint index = (uint)round(_FishFloraBiomes.Load(int3(texel, 0)) * 255.0);
	return min(index, (uint)max(_FishFloraInfo.w - 1.0, 0.0));
}

// The patch coordinate at xz: patches about patchMetres across (one per whole unit), warped into blobs.
float2 FishFloraPatchCoord(float2 xz, float patchMetres, uint salt)
{
	float2 p = xz / max(patchMetres, 0.25);
	return p + (float2(FishFloraNoise(p * 0.5, salt + 1u), FishFloraNoise(p * 0.5 + 17.3, salt + 2u)) - 0.5) * 1.6;
}

// The look one patch takes in a biome: a weighted pick of the twelve, by a hash of the patch.
uint FishFloraVariantOfPatch(int2 patch, uint biome, uint salt)
{
	float pick = (FishFloraCellHash(patch, salt + 3u) >> 8) * (1.0 / 16777216.0);
	uint row = biome * FISH_FLORA_ROWS;
	uint variant = FISH_FLORA_VARIANTS - 1u;
	[loop] for (uint v = 0u; v < FISH_FLORA_VARIANTS; v++)
	{
		if (pick < _FishFloraPalette.Load(int3(v, row, 0)).a)
		{
			variant = v;
			break;
		}
	}
	return variant;
}

// The look a patch of ground takes in a biome: patches about patchMetres across, warped into blobs, each a weighted pick of
// the twelve. salt keeps grass, flowers and fungi from sharing their patches' edges.
uint FishFloraVariantAt(float2 xz, uint biome, float patchMetres, uint salt)
{
	float2 p = FishFloraPatchCoord(xz, patchMetres, salt);
	// Patches mingle at their edges rather than meeting on a line: each read is nudged by up to a third of a patch, by a
	// hash of its own 30 cm cell, so blades and plants near a border fall either side of it (a hard edge read as "chunks").
	uint edge = FishFloraCellHash((int2)floor(xz * 3.3), salt + 4u);
	p += (float2((edge & 0xffffu) * (1.0 / 65535.0), (edge >> 16) * (1.0 / 65535.0)) - 0.5) * 0.66;
	return FishFloraVariantOfPatch((int2)floor(p), biome, salt);
}

FishFloraLook FishFloraLookOf(uint biome, uint variant)
{
	FishFloraLook look;
	uint row = biome * FISH_FLORA_ROWS;
	float4 foliage = _FishFloraPalette.Load(int3(variant, row + 0u, 0));
	float4 root = _FishFloraPalette.Load(int3(variant, row + 1u, 0));
	float4 bloom = _FishFloraPalette.Load(int3(variant, row + 2u, 0));
	float4 accent = _FishFloraPalette.Load(int3(variant, row + 3u, 0));
	float4 mineral = _FishFloraPalette.Load(int3(variant, row + 4u, 0));
	float4 shift = _FishFloraPalette.Load(int3(variant, row + 5u, 0));
	float4 biomeShift = _FishFloraPalette.Load(int3(variant, row + 6u, 0));
	look.valid = true;
	look.biome = biome;
	look.variant = variant;
	look.foliage = foliage.rgb;
	look.root = root.rgb;
	look.height = root.a;
	look.bloom = bloom.rgb;
	look.bloomSize = bloom.a;
	look.accent = accent.rgb;
	look.width = accent.a;
	look.mineral = mineral.rgb;
	look.bend = mineral.a;
	look.shift = shift.rgb;
	look.biomeShift = biomeShift.rgb;
	return look;
}

// How far into its neighbour a patch's look fades on continuous ground (FishFloraGroundLookAt), in patches either side of
// the border: the third of a patch over which FishFloraVariantAt's nudge mingles the blades.
#define FISH_FLORA_GROUND_BAND 0.33

// The look the continuous ground wears at xz (the terrain, and the terrain's contact band on rocks and trunks): the
// blades' own patches and biomes, but BLENDED across their borders instead of dithered. The blades' dither is a per-30-cm
// (patches) and per-60-cm (biomes) hashed read; a blade is a discrete thing either side of a border and mingles, but the
// ground is one continuous albedo, and the same read painted it in hard squares (2026-10-15). Here the biome is weighed
// bilinearly over its grid's four nearest texels (what the biome read's half-texel jitter averages to) and each biome's
// patches smoothly over their four nearest, fading across FISH_FLORA_GROUND_BAND either side of a border, so the middle
// of a patch is exactly the look its grass wears. Only shift and accent are blended (all FishFloraGround reads); the rest
// of the look is the strongest contributor's. Callers test _FishFloraInfo.x first.
FishFloraLook FishFloraGroundLookAt(float2 xz, float patchMetres, uint salt)
{
	uint4 biomes = 0u;
	float4 biomeWeights = float4(1.0, 0.0, 0.0, 0.0);
	float2 size = _FishFloraInfo.yz;
	float2 uv = (xz - _FishFloraRect.xy) * _FishFloraRect.zw;
	[branch] if (_FishFloraRect.z > 0.0 && all(uv >= 0.0) && all(uv <= 1.0))
	{
		float2 g = uv * size - 0.5;
		int2 t0 = (int2)floor(g);
		float2 f = g - floor(g);
		int2 hi = (int2)size - 1;
		uint top = (uint)max(_FishFloraInfo.w - 1.0, 0.0);
		[unroll] for (int k = 0; k < 4; k++)
		{
			int2 o = int2(k & 1, k >> 1);
			int2 texel = clamp(t0 + o, int2(0, 0), hi);
			biomes[k] = min((uint)round(_FishFloraBiomes.Load(int3(texel, 0)) * 255.0), top);
			biomeWeights[k] = (o.x != 0 ? f.x : 1.0 - f.x) * (o.y != 0 ? f.y : 1.0 - f.y);
		}
		// One pass of patches per distinct biome: the same biome in two texels is one weight.
		[unroll] for (int a = 1; a < 4; a++)
		{
			[unroll] for (int b = 0; b < a; b++)
			{
				bool same = biomes[a] == biomes[b] && biomeWeights[b] > 0.0;
				biomeWeights[b] += same ? biomeWeights[a] : 0.0;
				biomeWeights[a] = same ? 0.0 : biomeWeights[a];
			}
		}
	}

	float2 q = FishFloraPatchCoord(xz, patchMetres, salt) - 0.5;
	int2 patch0 = (int2)floor(q);
	float2 s = smoothstep(0.5 - FISH_FLORA_GROUND_BAND, 0.5 + FISH_FLORA_GROUND_BAND, q - floor(q));

	float3 shift = 0.0;
	float3 accent = 0.0;
	float best = -1.0;
	uint bestBiome = 0u;
	uint bestVariant = 0u;
	[loop] for (int bi = 0; bi < 4; bi++)
	{
		float bw = biomeWeights[bi];
		[branch] if (bw > 0.0)
		{
			uint biome = biomes[bi];
			uint row = biome * FISH_FLORA_ROWS;
			[loop] for (int k = 0; k < 4; k++)
			{
				int2 o = int2(k & 1, k >> 1);
				float w = bw * (o.x != 0 ? s.x : 1.0 - s.x) * (o.y != 0 ? s.y : 1.0 - s.y);
				[branch] if (w > 0.0)
				{
					uint variant = FishFloraVariantOfPatch(patch0 + o, biome, salt);
					shift += _FishFloraPalette.Load(int3(variant, row + 5u, 0)).rgb * w;
					accent += _FishFloraPalette.Load(int3(variant, row + 3u, 0)).rgb * w;
					bool stronger = w > best;
					best = stronger ? w : best;
					bestBiome = stronger ? biome : bestBiome;
					bestVariant = stronger ? variant : bestVariant;
				}
			}
		}
	}

	FishFloraLook look = FishFloraLookOf(bestBiome, bestVariant);
	look.shift = shift;
	look.accent = accent;
	return look;
}

// How living things grow at xz, as a multiplier on their own size: a smooth swell of about ±spread over some ten metres
// (two octaves of value noise), CONTINUOUS, so nothing steps where one patch meets the next. Shape never comes from a
// patch's look: per-patch heights stood whole patches of grass a different height from their neighbours (2026-10-15).
// 1 when nothing is published.
float FishFloraSwell(float2 xz, float spread, uint salt)
{
	if (_FishFloraInfo.x < 0.5)
	{
		return 1.0;
	}
	float n = FishFloraNoise(xz * (1.0 / 9.0), salt) * 0.7 + FishFloraNoise(xz * (1.0 / 3.3), salt + 9u) * 0.3;
	return 1.0 + spread * (2.0 * n - 1.0);
}

// The swell times the world's own growth (_FishFloraWorld.x: thin air low, thick air tall), for heights and sizes.
float FishFloraGrowth(float2 xz, float spread, uint salt)
{
	return FishFloraSwell(xz, spread, salt) * (_FishFloraInfo.x < 0.5 ? 1.0 : _FishFloraWorld.x);
}

// The look of the patch at xz: what grass, a plant, a flower or a stone standing there should wear.
FishFloraLook FishFloraAt(float2 xz, float patchMetres, uint salt)
{
	// One return: FXC's optimiser overflows its stack when an early return of the whole struct is inlined under a
	// caller's branches (the terrain's four-layer blend did, 2026-10-15).
	FishFloraLook look = (FishFloraLook)0;
	look.valid = false;
	look.height = 1.0; look.width = 1.0; look.bend = 1.0; look.bloomSize = 1.0;
	look.mineral = 1.0;
	look.shift = 1.0;
	look.biomeShift = 1.0;
	[branch] if (_FishFloraInfo.x >= 0.5)
	{
		uint biome = FishFloraBiomeAt(xz);
		look = FishFloraLookOf(biome, FishFloraVariantAt(xz, biome, patchMetres, salt));
	}
	return look;
}

float FishFloraLuminance(float3 c)
{
	return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// Recolours an albedo to a look's colour, keeping some of its own light and dark: an albedo as bright as the look becomes
// the look exactly, a darker or brighter texel a softened step either side (x = own / look: 1.5x / (x + 0.5), so a texel
// twice as bright reads 1.2×, half as bright 0.75×). The look's own brightness survives, so a pale patch stays pale and a
// shaded one dark. strength 0 leaves the albedo, 1 takes the look outright.
float3 FishFloraRecolour(float3 albedo, float3 target, float strength)
{
	float x = FishFloraLuminance(albedo) / max(FishFloraLuminance(target), 1e-3);
	float3 recoloured = target * (1.5 * x / (x + 0.5));
	return lerp(albedo, recoloured, saturate(strength));
}

// Living green in its look: its own colour (which already carries its ground's climate: a dry patch's straw, a wet one's
// green) times the look's shift. strength 0 leaves it. The common look of an Earth-like world shifts nothing.
float3 FishFloraShift(float3 albedo, float3 shift, float strength)
{
	return albedo * lerp(1.0, shift, saturate(strength));
}

// A terrain ground layer's albedo in its patch's look. shares = (foliage, accent, mineral) of the layer (BiomeLooks.
// GroundShareOf, per layer from the binder): living sward shifted as the grass on it is (FishFloraShift), lichen toward its lichen colour, mineral toward the world's own ground where the air is thin
// or absent. look comes from FishFloraAt(xz, FISH_FLORA_GRASS_PATCH, FISH_FLORA_GRASS_SALT), once per pixel.
float3 FishFloraGround(float3 albedo, float3 shares, FishFloraLook look)
{
	if (_FishFloraInfo.x < 0.5)
	{
		return albedo;
	}
	float3 c = albedo;
	if (look.valid)
	{
		c = FishFloraShift(c, look.shift, shares.x * _FishFloraStrength.x);
		c = FishFloraRecolour(c, look.accent, shares.y * _FishFloraStrength.z);
	}
	return FishFloraRecolour(c, _FishFloraGround.rgb, shares.z * _FishFloraGround.a);
}

#endif
