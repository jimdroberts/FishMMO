using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What a biome paints and grows under the sea (<see cref="BiomeTemplate.SeaFloor"/>, 2026-10-19, phase I2m: decisions 36 (a),
	/// 38, 40 and 45): the shore's rule, its own ground at every depth, or its own ground down to the lowest tide. Values are stored
	/// by number: append only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Until 2026-10-19 every biome followed the shore's rule: under the sea its lakebed took the ground from the water line down
	/// (over BiomeSplatOptions.ShoreFadeMetres) and its main and detail layers never painted there, so an abyss was one layer of
	/// silt with basalt cliffs, and a reef's live coral, rubble and drop-off walls could never be told apart. A sea-floor biome's own
	/// grounds now paint under the sea as they do on land, zoned by their depth bands (<see cref="TerrainTextureLayer.useSeaDepthBand"/>),
	/// and its rules naming the sea in their waters grow there (PrefabSpawnRule.waters).
	/// </para>
	/// <para>
	/// Every template is <see cref="Shore"/> until its round says otherwise, so a land biome's shore is painted exactly as before.
	/// </para>
	/// </remarks>
	public enum BiomeSeaFloor : byte
	{
		/// <summary>The shore's rule, every biome's before 2026-10-19: under the sea the lakebed takes the ground from the water line down.</summary>
		Shore = 0,
		/// <summary>
		/// A sea-floor biome, deep or on the shelf (Abyss, Abyssal Plain, Deep Ocean, Coastal Water, Coral Reef…): its main and
		/// detail layers paint at every depth, zoned by their depth bands; its lakebed only under inland water (a rock pool, an
		/// island's pond), never under the sea.
		/// </summary>
		Ground = 1,
		/// <summary>
		/// A tidal biome (Estuary): its main and detail layers paint down to the lowest tide (the scene's tide reach below mean sea
		/// level), the lakebed below it, so a salt marsh's zones follow the tide down onto the flats the ebb lays bare.
		/// </summary>
		ToLowWater = 2,
	}

	/// <summary>
	/// The pure arithmetic of the sea floor (2026-10-19, phase I2m): how a biome's bed shares the ground under the sea, the signed
	/// depth bands of its ground layers (in metres or in tides), the tide a scene's coast biomes ask of its sea, and how fast the
	/// sea takes the daylight out on its way down to the floor.
	/// </summary>
	public static class BiomeSeaFloors
	{
		/// <summary>An end of a depth band at or past this many units (metres or tides) either way is open.</summary>
		public const float OpenDepth = 11000f;

		/// <summary>The soft edge of a ground layer's depth band, centred on each closed end (metres, or tides for a band in tides).</summary>
		public const float DefaultDepthFalloff = 1f;

		/// <summary>
		/// The most the tide moves the sea on a scene whose coast asks nothing (WaterEnvironment.DefaultMaximumTideMetres, held
		/// equal by a test: FishMMO.Water is not a reference of this assembly).
		/// </summary>
		public const float DefaultTideMetres = 2.5f;

		/// <summary>How much the open-ocean tide is amplified on a coast that asks nothing (WaterEnvironment.CoastalAmplification's default, held equal by a test).</summary>
		public const float DefaultAmplification = 2.5f;

		/// <summary>The most a coast biome's tide strength may move the sea, metres (decision 45, 2026-10-18: "capped 4 m").</summary>
		public const float MaximumTideMetres = 4f;

		/// <summary>The widest tide strength a template may ask (the Estuary's ×4).</summary>
		public const float MaximumTideStrength = 4f;

		/// <summary>The smallest tide the arithmetic divides by, metres: a sea with no tide still has a tide unit.</summary>
		public const float MinimumTideUnit = 0.1f;

		/// <summary>
		/// The share of a biome's ground under the sea its bed takes, 0 … 1, where the ground lies <paramref name="depth"/> metres
		/// below mean sea level (negative above it).
		/// </summary>
		/// <param name="floor">The biome's sea floor.</param>
		/// <param name="shore">
		/// The shore's own share there (the painter's: a smoothstep from the water line to its fade's depth): what a
		/// <see cref="BiomeSeaFloor.Shore"/> biome takes unchanged, bit for bit.
		/// </param>
		/// <param name="fade">The metres over which the bed takes over (BiomeSplatOptions.ShoreFadeMetres).</param>
		/// <param name="lowWater">How far below mean sea level the lowest tide falls, metres (the scene's tide reach).</param>
		public static float BedShare(BiomeSeaFloor floor, float shore, float depth, float fade, float lowWater)
		{
			switch (floor)
			{
				case BiomeSeaFloor.Ground:
					return 0f;
				case BiomeSeaFloor.ToLowWater:
					if (!(fade > 0f))
					{
						return depth >= lowWater ? 1f : 0f;
					}
					return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - Mathf.Max(0f, lowWater)) / fade));
				default:
					return shore;
			}
		}

		/// <summary>
		/// A signed depth band (a ground layer's, <see cref="TerrainTextureLayer.SeaDepthWeight"/>): 1 inside
		/// <paramref name="range"/> (x the shallowest, y the deepest, below mean sea level; negative is above it), 0 outside, and a
		/// smoothstep <paramref name="falloff"/> wide centred on each closed end, half at the end itself. An end at or past
		/// ±<see cref="OpenDepth"/> is open. A NaN depth (no sea) passes.
		/// </summary>
		public static float DepthBand(Vector2 range, float falloff, float depth)
		{
			if (float.IsNaN(depth))
			{
				return 1f;
			}
			float weight = 1f;
			if (range.x > -OpenDepth)
			{
				weight *= Edge(depth - range.x, falloff);
			}
			if (range.y < OpenDepth)
			{
				weight *= Edge(range.y - depth, falloff);
			}
			return weight;
		}

		/// <summary>True when a band closes at either end (so it changes anything).</summary>
		public static bool Closes(Vector2 range) => range.x > -OpenDepth || range.y < OpenDepth;

		private static float Edge(float inside, float falloff)
		{
			if (!(falloff > 0f))
			{
				return inside >= 0f ? 1f : 0f;
			}
			float t = Mathf.Clamp01(inside / falloff + 0.5f);
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// A depth below mean sea level in tides (decision 45: 1 is the depth of the lowest tide, −1 the height of the highest),
		/// for a scene whose tide moves the sea <paramref name="tideMetres"/> either way.
		/// </summary>
		public static float InTides(float depthMetres, float tideMetres) => depthMetres / Mathf.Max(MinimumTideUnit, tideMetres);

		/// <summary>
		/// The height above mean sea level (in tides: 0 mean sea, 1 mean high water, −1 mean low water, the drafts' tide units) as the
		/// signed depth a depth band reads: a marsh zone authored "from −0.6 to 0.3 tides" is the depth band (−0.3, 0.6).
		/// </summary>
		public static Vector2 TideHeightsAsDepths(float lowHeight, float highHeight)
		{
			float shallow = highHeight >= OpenDepth ? -OpenDepth : -highHeight;
			float deep = lowHeight <= -OpenDepth ? OpenDepth : -lowHeight;
			return new Vector2(shallow, deep);
		}

		// ── The tide a coast asks (decision 45) ───────────────────────

		/// <summary>
		/// The tide strength a scene's coast asks of its sea: the mean of its coast biomes' strengths (<see cref="BiomeTemplate.TideStrength"/>)
		/// weighted by how much of the coast each holds; exactly 1 when they all ask 1 or no coast was found.
		/// </summary>
		/// <param name="strengths">Each biome's tide strength.</param>
		/// <param name="coast">Each biome's share of the coast (same indices; summed over the coast's points).</param>
		public static float CoastStrength(IReadOnlyList<float> strengths, IReadOnlyList<float> coast)
		{
			if (strengths == null || coast == null)
			{
				return 1f;
			}
			double sum = 0.0, weight = 0.0;
			bool asks = false;
			int count = Math.Min(strengths.Count, coast.Count);
			for (int i = 0; i < count; i++)
			{
				float w = coast[i];
				if (!(w > 0f))
				{
					continue;
				}
				float s = Strength(strengths[i]);
				asks |= s != 1f;
				sum += (double)w * s;
				weight += w;
			}
			if (!asks || weight <= 0.0)
			{
				return 1f;
			}
			return Strength((float)(sum / weight));
		}

		/// <summary>A template's tide strength as the arithmetic reads it: 0 … <see cref="MaximumTideStrength"/>, 1 for NaN.</summary>
		public static float Strength(float strength) => float.IsNaN(strength) ? 1f : Mathf.Clamp(strength, 0f, MaximumTideStrength);

		/// <summary>The scene sea's coastal amplification (WaterEnvironment.CoastalAmplification) for a coast's tide strength.</summary>
		public static float Amplification(float strength) => DefaultAmplification * Strength(strength);

		/// <summary>
		/// The most the scene sea's tide may move it (WaterEnvironment.MaximumTideMetres) for a coast's tide strength: the default
		/// scaled by the strength, never past <see cref="MaximumTideMetres"/>; the default itself at strength 1.
		/// </summary>
		public static float TideMetres(float strength)
		{
			float s = Strength(strength);
			return s == 1f ? DefaultTideMetres : Mathf.Min(MaximumTideMetres, DefaultTideMetres * s);
		}

		/// <summary>
		/// True when a scene sea's tide settings are ones this arithmetic wrote (for some strength) or the defaults, so the cut may
		/// write them again; false for settings somebody tuned by hand, which a repaint keeps.
		/// </summary>
		public static bool IsAuthoredTide(float amplification, float maximumMetres)
		{
			if (!(amplification >= 0f))
			{
				return false;
			}
			float strength = amplification / DefaultAmplification;
			if (strength > MaximumTideStrength + 1e-4f)
			{
				return false;
			}
			return Mathf.Abs(TideMetres(strength) - maximumMetres) <= 1e-3f;
		}

		// ── Daylight under the sea (decision 36 (c)) ──────────────────

		/// <summary>
		/// The share of a sea look's extinction (<see cref="BiomeSeaLook.Extinction"/>) the daylight loses per metre on its way down:
		/// the underwater pass dims what lies behind by 0.5 × (0.45 e + 0.45 e / cos θ) a metre, its sky and its refracted sun's beam,
		/// which for a sun 50° up (the beam ~35° off the vertical in the water) is 0.5 × 0.45 × (1 + 1.22) e ≈ 0.5 e; 0.5 so the lit
		/// floor seen from above and seen from beside a diver darkens alike.
		/// </summary>
		public const float DaylightShare = 0.5f;

		/// <summary>
		/// How fast the sea takes the daylight out on its way down to the floor, per metre (FishWeather.hlsl's _FishOcclusionWater.w,
		/// FishSeaDaylight): <see cref="DaylightShare"/> of the coverage-weighted mean of the sea looks' extinction
		/// (<see cref="BiomeSeaLook.Extinction"/>, the underwater pass's own) over the scene's sea-floor biomes; 0 (no dimming, as
		/// before 2026-10-25) when the scene holds none.
		/// </summary>
		/// <param name="looks">Each sea-floor biome's sea look and its share of the scene (texels, cells, any weight).</param>
		public static float DaylightExtinction(IEnumerable<(BiomeSeaLook look, float weight)> looks)
		{
			if (looks == null)
			{
				return 0f;
			}
			double sum = 0.0, weight = 0.0;
			foreach ((BiomeSeaLook look, float w) in looks)
			{
				if (!(w > 0f))
				{
					continue;
				}
				BiomeSeaLook used = look.Defined ? look : BiomeSeaLooks.Today;
				sum += (double)w * used.Extinction;
				weight += w;
			}
			return weight > 0.0 ? (float)(DaylightShare * sum / weight) : 0f;
		}

		/// <summary>
		/// The share of the daylight that reaches ground <paramref name="depth"/> metres under the sea's surface
		/// (FishWeather.hlsl's FishSeaDaylight, held equal by a test): exp(−extinction × depth), 1 at and above the surface or with no
		/// extinction.
		/// </summary>
		public static float Daylight(float extinction, float depth)
		{
			if (!(extinction > 0f) || !(depth > 0f))
			{
				return 1f;
			}
			return Mathf.Exp(-extinction * depth);
		}

		// ── What grows and stands under the sea (decision 36 (a), (b)) ─

		/// <summary>
		/// True when a spawn rule on a biome's MAIN or DETAIL layer grows under the sea (2026-10-25): only in a sea-floor biome
		/// (<see cref="BiomeSeaFloor.Ground"/> or <see cref="BiomeSeaFloor.ToLowWater"/>, whose own grounds paint there) and only for a
		/// rule that names the sea in its waters (PrefabSpawnRule.waters); every other rule on those layers grows on land as before.
		/// </summary>
		public static bool GrowsUnderSea(BiomeSeaFloor floor, BiomeBedWaters ruleWaters)
		{
			return floor != BiomeSeaFloor.Shore && (ruleWaters & BiomeBedWaters.Sea) != 0;
		}

		/// <summary>
		/// The depth band an underwater site kind stands in on a biome's floor, metres below mean sea level: the biome's own
		/// (<see cref="BiomeTemplate.UnderwaterSiteDepth"/>) when it gives one (y &gt; 0), else the kind's (<paramref name="kindMin"/> …
		/// <paramref name="kindMax"/>), as every sunken site was sited before 2026-10-25.
		/// </summary>
		public static Vector2 SiteDepth(Vector2 biome, float kindMin, float kindMax)
		{
			if (biome.y > 0f && biome.y >= biome.x)
			{
				return new Vector2(Mathf.Max(0f, biome.x), biome.y);
			}
			return new Vector2(kindMin, kindMax);
		}
	}
}
