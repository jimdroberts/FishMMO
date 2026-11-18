using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which waters a bed layer is painted under, or a bed rule grows under (decision 35, 2026-10-18): the sea, a lake (or a
	/// pond, a moat, a grotto pool) and a river. <see cref="Default"/> on a spawn rule means "the slot's own": the lakebed's
	/// rules the sea only (what they always grew under), the riverbed's a river, a bed layer's its own <see cref="BedTextureLayer.waters"/>.
	/// </summary>
	[Flags]
	public enum BiomeBedWaters
	{
		/// <summary>On a spawn rule: the waters of the slot it is on. On a bed layer: every water.</summary>
		Default = 0,
		Sea = 1,
		Lake = 2,
		River = 4,
		/// <summary>Fresh water: a lake or a river.</summary>
		Inland = Lake | River,
		Any = Sea | Lake | River,
	}

	/// <summary>
	/// One of a biome's extra BED layers (decision 35 (b), 2026-10-18): ground painted under water by its depth below THAT water's
	/// own surface, and in a river by its current too — a lake's pale wave-cut shelf, its pondweed slope (the Halde) and its dark
	/// ooze in the deeps; a river's riffles, scour pools and bedrock reefs. Up to <see cref="BiomeTemplate.MaximumBedLayers"/> a
	/// biome, each taking a share of the submerged ground where its bands allow (several overlap as a union, as detail layers do)
	/// and leaving the rest to the biome's lakebed (under a lake or the sea) and riverbed (under a river).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Depth below its own water.</b> Under the sea a point's depth is below mean sea level; under a lake below the lake's level;
	/// under a river below the river's surface where its line passes (BiomeBedWater.At). A layer authored for "3–9 m" therefore
	/// lies at the same depth on a mountain tarn as in a lowland lake, which a sea-relative band could never do.
	/// </para>
	/// <para>
	/// <b>No noise.</b> A bed zone follows the bathymetry — a shelf's edge is a contour — so the layer is weighed by its bands
	/// alone, softened across <see cref="depthFalloff"/> and <see cref="currentFalloff"/>; its blend noise is not read.
	/// </para>
	/// </remarks>
	[Serializable]
	public class BedTextureLayer : TerrainTextureLayer
	{
		[Header("Bed Bands")]
		[Tooltip("The waters this bed is painted under: the sea, a lake (ponds, moats and grotto pools are lakes) and a river. Nothing ticked = every water.")]
		public BiomeBedWaters waters = BiomeBedWaters.Inland;

		[Tooltip("Metres below the water's OWN surface (the sea's, the lake's, the river's) this bed lies between: x the shallowest, y the deepest; 0 leaves that end open.")]
		public Vector2 depthRange = Vector2.zero;

		[Tooltip("Width of the soft edge at each closed end of the depth band, metres, centred on the end: half the bed at the stated depth.")]
		[Range(0f, 10f)] public float depthFalloff = BiomeBedBands.DefaultDepthFalloff;

		[Tooltip("Also band the bed by the river's current where it lies (a reef where the water is quick, silt where it is slow). Off: any current.")]
		public bool useCurrentBand = false;

		[Tooltip("Current band only. Metres a second the bed lies between: x the slowest, y the quickest; 0 leaves that end open. Lakes and the sea have no current (0).")]
		public Vector2 currentRange = Vector2.zero;

		[Tooltip("Current band only. Width of the soft edge at each closed end, m/s, centred on the end.")]
		[Range(0f, 2f)] public float currentFalloff = BiomeBedBands.DefaultCurrentFalloff;

		/// <summary>How strongly this bed lies at a point under water, 0 … 1: its waters' share there times its bands.</summary>
		public float Strength(in BedWater water)
		{
			float share = water.Share(waters == BiomeBedWaters.Default ? BiomeBedWaters.Any : waters);
			if (share <= 0f)
			{
				return 0f;
			}
			float strength = share * BiomeBedBands.Band(depthRange, depthFalloff, water.Depth);
			if (useCurrentBand && strength > 0f)
			{
				strength *= BiomeBedBands.Band(currentRange, currentFalloff, water.Speed);
			}
			return Mathf.Clamp01(strength);
		}
	}

	/// <summary>
	/// What lies over a point of the bed (decision 35, 2026-10-18): how much of it is the sea's, a lake's and a river's (shares
	/// summing to 1 under water, all 0 with none), how deep it lies below THAT water's own surface, and how quick the river runs there.
	/// </summary>
	public readonly struct BedWater
	{
		/// <summary>No water over the point.</summary>
		public static readonly BedWater None = new BedWater(0f, 0f, 0f, float.NegativeInfinity, 0f, float.NegativeInfinity);

		public readonly float Sea, Lake, River;
		/// <summary>Metres below the water's own surface (negative above it).</summary>
		public readonly float Depth;
		/// <summary>The river's current there, m/s; 0 in still water.</summary>
		public readonly float Speed;
		/// <summary>The surface the depth is measured from, world y; negative infinity with no water.</summary>
		public readonly float Surface;

		public BedWater(float sea, float lake, float river, float depth, float speed, float surface)
		{
			Sea = sea;
			Lake = lake;
			River = river;
			Depth = depth;
			Speed = speed;
			Surface = surface;
		}

		/// <summary>True when some water lies over the point.</summary>
		public bool Any => Sea + Lake + River > 0f;

		/// <summary>True when the water is wholly the sea's (no inland body answers there): the case every sea rule was written for.</summary>
		public bool SeaOnly => Sea > 0f && Lake <= 0f && River <= 0f;

		/// <summary>The share of the point that is one of the waters in <paramref name="mask"/> (<see cref="BiomeBedWaters.Default"/> = none).</summary>
		public float Share(BiomeBedWaters mask)
		{
			float share = 0f;
			if ((mask & BiomeBedWaters.Sea) != 0) share += Sea;
			if ((mask & BiomeBedWaters.Lake) != 0) share += Lake;
			if ((mask & BiomeBedWaters.River) != 0) share += River;
			return Mathf.Clamp01(share);
		}

		/// <summary>
		/// The water over a point of ground.
		/// </summary>
		/// <param name="groundY">The bed's world height.</param>
		/// <param name="hasSea">True when the world has a sea at <paramref name="seaLevel"/>.</param>
		/// <param name="inlandSurface">
		/// A river's or lake's surface there (the paint's and the scatter's InlandWaterSurface): negative infinity for none, positive
		/// infinity in a dry wash's bed (a river's channel with no water in it: depth 0, no current).
		/// </param>
		/// <param name="river">
		/// How much of the inland water there is a river's (x, 0 … 1: 1 in its channel, easing to 0 over its last stretch into a lake or
		/// the sea), its current (y, m/s), and z ≥ 0.5 when the rest is the sea's (a river's mouth on the coast) rather than a lake's.
		/// </param>
		public static BedWater At(float groundY, bool hasSea, float seaLevel, float inlandSurface, Vector3 river)
		{
			if (float.IsPositiveInfinity(inlandSurface))
			{
				return new BedWater(0f, 0f, 1f, 0f, 0f, groundY);
			}
			if (!float.IsNegativeInfinity(inlandSurface) && !float.IsNaN(inlandSurface))
			{
				if (hasSea && BiomeBedBands.IsSeas(inlandSurface, seaLevel))
				{
					// Water the sea's high tide reaches (an estuary's tidal channel, a lagoon on the shore) is the sea's: its floor the
					// lakebed and its sea rules by their depth below mean sea level, exactly as before the inland beds existed.
					return new BedWater(1f, 0f, 0f, seaLevel - groundY, 0f, seaLevel);
				}
				float r = Mathf.Clamp01(river.x);
				float rest = 1f - r;
				bool intoSea = river.z >= 0.5f;
				return new BedWater(intoSea ? rest : 0f, intoSea ? 0f : rest, r, inlandSurface - groundY, r > 0f ? Mathf.Max(0f, river.y) : 0f, inlandSurface);
			}
			return hasSea ? new BedWater(1f, 0f, 0f, seaLevel - groundY, 0f, seaLevel) : None;
		}
	}

	/// <summary>
	/// The pure arithmetic of the bed layers (decision 35, 2026-10-18): their depth and current bands, and how a biome's
	/// submerged ground is shared among its lakebed, its riverbed and its extra bed layers.
	/// </summary>
	public static class BiomeBedBands
	{
		/// <summary>The soft edge of a bed's depth band, metres: a shelf's edge eases over a metre or so of slope.</summary>
		public const float DefaultDepthFalloff = 0.5f;

		/// <summary>The soft edge of a bed's current band, m/s.</summary>
		public const float DefaultCurrentFalloff = 0.2f;

		/// <summary>
		/// How far above mean sea level an inland water's surface may stand and still be the sea's (the highest tide,
		/// WaterEnvironment's MaximumTideMetres): an estuary's tidal channel and a lagoon on the shore are sea floor, so the sea's
		/// rules grow there by their depth below mean sea level, as they always did.
		/// </summary>
		public const float SeaReachMetres = 2.5f;

		/// <summary>True when inland water with its surface at <paramref name="surface"/> is the sea's (<see cref="SeaReachMetres"/>).</summary>
		public static bool IsSeas(float surface, float seaLevel) => surface <= seaLevel + SeaReachMetres;

		/// <summary>
		/// One band (a depth's or a current's): 1 inside <paramref name="range"/>, 0 outside, and a smoothstep
		/// <paramref name="falloff"/> wide centred on each closed end, half at the end itself. An end at 0 or below is open
		/// (x the low end, y the high; both open = no band, 1 everywhere). A value of NaN passes.
		/// </summary>
		public static float Band(Vector2 range, float falloff, float value)
		{
			if (float.IsNaN(value))
			{
				return 1f;
			}
			float low = range.x, high = range.y;
			float grow = 1f;
			if (low > 0f)
			{
				grow *= Edge(value - low, falloff);
			}
			if (high > 0f)
			{
				grow *= Edge(high - value, falloff);
			}
			return grow;
		}

		/// <summary>True when a band closes at either end (so it changes anything).</summary>
		public static bool Closes(Vector2 range) => range.x > 0f || range.y > 0f;

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
		/// Shares a biome's submerged ground among its beds, as shares of that ground (the caller multiplies them by it).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The extra bed layers first</b>, as a union of their strengths split in proportion to them (two half-strength zones
		/// cover three quarters, as detail layers do). <b>What they leave is the default bed's</b>: the riverbed takes the river's
		/// share of it, the lakebed the rest (under a lake or the sea). A biome with only one of the two gives it the whole of that
		/// remainder; with neither, the remainder is not the beds' (the caller leaves it to the biome's ground, as when a biome had
		/// no submerged art before). Before 2026-10-18 the riverbed was painted only when a biome had no lakebed, so every river
		/// lay on its biome's lakebed (decision 35 (h)).
		/// </para>
		/// <para>
		/// With no extra layer and no river share, the lakebed's share is exactly 1 (bit for bit: nothing is multiplied by a
		/// share that is not 1), so a sea or a lake is painted exactly as it was.
		/// </para>
		/// </remarks>
		/// <param name="water">The water over the point.</param>
		/// <param name="lakebed">True when the biome has a lakebed layer.</param>
		/// <param name="riverbed">True when it has a riverbed layer of its own (not the lakebed's art: the caller passes false then).</param>
		/// <param name="strengths">Each extra bed layer's strength there (<see cref="BedTextureLayer.Strength"/>); may be null.</param>
		/// <param name="count">How many of <paramref name="strengths"/> are the biome's.</param>
		/// <param name="shares">Filled with each extra layer's share (same indices); may be null when <paramref name="count"/> is 0.</param>
		/// <param name="lakebedShare">The lakebed's share.</param>
		/// <param name="riverbedShare">The riverbed's share.</param>
		/// <returns>The share the beds took between them, 0 … 1: what the biome's ground gives up to them.</returns>
		public static float Split(in BedWater water, bool lakebed, bool riverbed, float[] strengths, int count, float[] shares,
			out float lakebedShare, out float riverbedShare)
		{
			float keep = 1f, sum = 0f;
			for (int i = 0; i < count; i++)
			{
				float s = Mathf.Clamp01(strengths[i]);
				keep *= 1f - s;
				sum += s;
			}
			float union = count > 0 ? 1f - keep : 0f;
			for (int i = 0; i < count; i++)
			{
				shares[i] = sum > 0f ? union * Mathf.Clamp01(strengths[i]) / sum : 0f;
			}
			float rest = 1f - union;
			lakebedShare = riverbedShare = 0f;
			if (!lakebed && !riverbed)
			{
				return union;
			}
			float total = water.Sea + water.Lake + water.River;
			float river = total > 0f ? Mathf.Clamp01(water.River / total) : 0f;
			if (lakebed && riverbed)
			{
				if (river <= 0f)
				{
					lakebedShare = rest;
				}
				else if (river >= 1f)
				{
					riverbedShare = rest;
				}
				else
				{
					riverbedShare = rest * river;
					lakebedShare = rest - riverbedShare;
				}
			}
			else if (lakebed)
			{
				lakebedShare = rest;
			}
			else
			{
				riverbedShare = rest;
			}
			return 1f;
		}

		/// <summary>
		/// The waters a bed rule grows under (<see cref="PrefabSpawnRule.waters"/>), given its slot's: the rule's own when it names
		/// any, else the slot's (the lakebed's the sea, a riverbed's a river, a bed layer's its own waters).
		/// </summary>
		public static BiomeBedWaters RuleWaters(BiomeBedWaters rule, BiomeBedWaters slot)
		{
			if (rule != BiomeBedWaters.Default)
			{
				return rule;
			}
			return slot == BiomeBedWaters.Default ? BiomeBedWaters.Any : slot;
		}

		/// <summary>The waters a bed slot's rules grow under by default: the lakebed the sea (what they always grew under), a riverbed a river.</summary>
		public const BiomeBedWaters LakebedWaters = BiomeBedWaters.Sea;
		public const BiomeBedWaters RiverbedWaters = BiomeBedWaters.River;

		/// <summary>
		/// How fully an aquatic rule grows under INLAND water (decision 35 (a)): the share of its waters there, a fade in from just
		/// under the surface (no tide on a lake or a river: <paramref name="fadeFrom"/> … <paramref name="fadeTo"/> metres below it,
		/// both ≤ 0), its depth band below THAT surface (<paramref name="depthRange"/>, the sea rules' band read the same way) and its
		/// current band. The sea's own case keeps the scatter's sea-level arithmetic exactly; this is the rest.
		/// </summary>
		public static float InlandRule(in BedWater water, BiomeBedWaters waters, float fadeFrom, float fadeTo, Vector2 depthRange, Vector2 currentRange)
		{
			float share = water.Share(waters);
			if (share <= 0f || float.IsNegativeInfinity(water.Surface))
			{
				return 0f;
			}
			// The fade, as the sea's water band: from fadeFrom (none) to fadeTo (full), heights relative to the surface.
			float y = -water.Depth;
			float span = fadeTo - fadeFrom;
			float fade;
			if (Mathf.Abs(span) < 1e-4f)
			{
				fade = y <= fadeTo ? 1f : 0f;
			}
			else
			{
				float t = Mathf.Clamp01((y - fadeFrom) / span);
				fade = t * t * (3f - 2f * t);
			}
			if (fade <= 0f)
			{
				return 0f;
			}
			return share * fade * DepthBand(depthRange, water.Depth) * Band(currentRange, DefaultCurrentFalloff, water.Speed);
		}

		/// <summary>
		/// A bed rule's depth band at a depth (the scatter's sea-floor band, TerrainScatter.DepthBand, read below any surface): 1 well
		/// inside it, thinning to 0 over the outer fifth at each closed end; 1 with no band.
		/// </summary>
		public static float DepthBand(Vector2 band, float depth)
		{
			float from = Mathf.Max(0f, band.x), to = Mathf.Max(0f, band.y);
			if (from <= 0f && to <= 0f)
			{
				return 1f;
			}
			float width = to > from ? 0.2f * (to - from) : Mathf.Max(0.5f, 0.25f * from);
			width = Mathf.Max(0.05f, width);
			float grow = 1f;
			if (from > 0f)
			{
				float t = Mathf.Clamp01((depth - from) / width);
				grow *= t * t * (3f - 2f * t);
			}
			if (to > 0f)
			{
				float t = Mathf.Clamp01((to - depth) / width);
				grow *= t * t * (3f - 2f * t);
			}
			return grow;
		}
	}
}
