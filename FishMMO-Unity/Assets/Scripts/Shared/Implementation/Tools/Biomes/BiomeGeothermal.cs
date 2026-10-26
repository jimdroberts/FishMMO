using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a steaming biome's hot ground divides among the kinds of site (<see cref="BiomeTemplate.Geothermal"/>, 2026-10-18,
	/// phase I2q, decision 47): the share of its sites that are geysers, hot springs, fumaroles and mud pots
	/// (FishMMO.Shared.Weather.GeothermalVents). Read only for a biome whose ground steams (it emits a vapour); a volcanic
	/// biome's vents are fumaroles whatever this says.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shares, not chances.</b> The four are weights and are divided by their sum, so 1/1/1/1 is a quarter each. Before
	/// this field existed every steaming biome split its sites a quarter geysers, two fifths springs and the rest fumaroles,
	/// with no mud pots; that is <see cref="Today"/>, every template's default, and with it the sites fall exactly where and
	/// as they did (GeothermalVents.KindOf keeps the old comparison for it, to the bit).
	/// </para>
	/// <para>
	/// <b>Everywhere the same.</b> The server, every client and the cut read the template, so a hand edit here moves sites
	/// in every scene of the biome the next time it is cut or repainted (the cut carves under them; the client only draws).
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeGeothermalShares
	{
		[Tooltip("Share of the hot ground's sites that are geysers: springs that boil over on their own clock.")]
		[Min(0f)] public float Geyser;
		[Tooltip("Share that are hot springs: steaming pools, carved into the ground with their water at the cut.")]
		[Min(0f)] public float HotSpring;
		[Tooltip("Share that are fumaroles: steady jets of steam out of a crack.")]
		[Min(0f)] public float Fumarole;
		[Tooltip("Share that are mud pots: grey boiling mud that plops on the shared clock. 0 for none (every biome until 2026-10-18).")]
		[Min(0f)] public float MudPot;
		[Tooltip("Share that are jets (2026-10-25, I2q-b, decision 53): a fissure's narrow jets of vapour and ice grains. 0 for none. Sites on stored fissure and ridge lines are jets whatever the split says.")]
		[Min(0f)] public float Jet;
		[Tooltip("Share that are sulphur pools (2026-10-25, I2q-b, decision 84): ponds of molten sulphur, dug and pooled as the springs are, wearing the biome's water look (a Melt look). 0 for none.")]
		[Min(0f)] public float SulphurPool;
		[Tooltip("The biome's own site rate (2026-10-25, I2q-b, decision 84): when above 0 its hot ground has every kind of this split at this rate (a site in that share of the 80 m cells, × 1.4), whatever it emits, so a biome whose emission is a deposit (sulphur dust) or a vented ash still has its fumaroles, pools and pots. 0 (every biome before): the rate follows the emission as it did.")]
		[Range(0f, 1f)] public float Rate;
		[Tooltip("The colour its vents fume (2026-10-25, I2q-b, decision 84: a sulphur flat's yellow-white); alpha 0 for none (the steam's own colour, or a cloud's white).")]
		public Color FumeTint;

		public BiomeGeothermalShares(float geyser, float hotSpring, float fumarole, float mudPot)
		{
			Geyser = geyser;
			HotSpring = hotSpring;
			Fumarole = fumarole;
			MudPot = mudPot;
			Jet = 0f;
			SulphurPool = 0f;
			Rate = 0f;
			FumeTint = default;
		}

		/// <summary>The split with jets and sulphur pools of their own (2026-10-25, I2q-b).</summary>
		public BiomeGeothermalShares WithMore(float jet, float sulphurPool)
		{
			BiomeGeothermalShares shares = this;
			shares.Jet = Mathf.Max(0f, jet);
			shares.SulphurPool = Mathf.Max(0f, sulphurPool);
			return shares;
		}

		/// <summary>The split with a site rate of its own (decision 84) and the colour its vents fume (alpha 0 for none).</summary>
		public BiomeGeothermalShares WithRate(float rate, Color fumeTint = default)
		{
			BiomeGeothermalShares shares = this;
			shares.Rate = Mathf.Clamp01(rate);
			shares.FumeTint = fumeTint;
			return shares;
		}

		/// <summary>The split every steaming biome had before the field existed: a quarter geysers, two fifths springs, the rest fumaroles.</summary>
		public static BiomeGeothermalShares Today => new BiomeGeothermalShares(0.25f, 0.4f, 0.35f, 0f);

		/// <summary>The shares' sum; 0 or less for a split nobody filled in (read as <see cref="Today"/>).</summary>
		public float Total => Geyser + HotSpring + Fumarole + MudPot + Jet + SulphurPool;

		/// <summary>True when the split among the kinds is exactly <see cref="Today"/>'s, value for value (the rate and tint are not kinds).</summary>
		public bool IsToday
		{
			get
			{
				BiomeGeothermalShares today = Today;
				return Geyser == today.Geyser && HotSpring == today.HotSpring && Fumarole == today.Fumarole && MudPot == today.MudPot
					&& Jet == 0f && SulphurPool == 0f;
			}
		}

		/// <summary>True when it carries a site rate of its own (<see cref="Rate"/>).</summary>
		public bool OwnRate => Rate > 0f;

		/// <summary>
		/// The split as the authoring marks it: each share to 1/10 000, in a fixed order; the jets, pools, rate and tint (2026-10-25)
		/// only when set, so every mark written before reads the same.
		/// </summary>
		public string Signature()
		{
			string Q(float v) => Mathf.Round(v * 10000f).ToString(CultureInfo.InvariantCulture);
			string mark = $"g{Q(Geyser)}|s{Q(HotSpring)}|f{Q(Fumarole)}|m{Q(MudPot)}";
			if (Jet > 0f)
			{
				mark += $"|j{Q(Jet)}";
			}
			if (SulphurPool > 0f)
			{
				mark += $"|p{Q(SulphurPool)}";
			}
			if (Rate > 0f)
			{
				mark += $"|r{Q(Rate)}";
			}
			if (FumeTint.a > 0f)
			{
				mark += "|t" + ColorUtility.ToHtmlStringRGBA(FumeTint);
			}
			return mark;
		}

		public override string ToString()
		{
			string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
			return $"geysers {F(Geyser)}, springs {F(HotSpring)}, fumaroles {F(Fumarole)}, mud pots {F(MudPot)}"
				+ (Jet > 0f ? $", jets {F(Jet)}" : string.Empty) + (SulphurPool > 0f ? $", sulphur pools {F(SulphurPool)}" : string.Empty)
				+ (Rate > 0f ? $", rate {F(Rate)}" : string.Empty) + (FumeTint.a > 0f ? ", fumes #" + ColorUtility.ToHtmlStringRGB(FumeTint) : string.Empty);
		}
	}

	/// <summary>
	/// How fully something banded by the ground's HEAT is present (2026-10-18, phase I2q): a ground layer's heat band
	/// (<see cref="TerrainTextureLayer.useHeatBand"/>) and a spawn rule's (<see cref="PrefabSpawnRule.useHeatBand"/>), read
	/// against the scene's heat mask (0 cold ground … 1 the boiling throat of a spring; SceneBiomeMap.HeatAt, baked at the
	/// cut from the same geothermal sites the client steams).
	/// </summary>
	/// <remarks>
	/// The climate band's curve (<see cref="BiomeClimateBand"/>: 1 inside, a smoothstep edge CENTRED on each closed end) with
	/// the heat's own open ends: a range starting at 0 or less is open below, one ending at 1 or more open above. A NaN
	/// reading (no heat known: a scene not cut by the generator) passes. Pure; any thread.
	/// </remarks>
	public static class BiomeHeatBand
	{
		/// <summary>The soft edge's width a heat band gets unless told otherwise.</summary>
		public const float DefaultFalloff = 0.1f;

		/// <summary>1 inside <paramref name="range"/> (either order), 0 outside, a centred smoothstep edge at each closed end.</summary>
		public static float Weight(Vector2 range, float falloff, float heat)
		{
			if (float.IsNaN(heat))
			{
				return 1f;
			}
			float low = Mathf.Min(range.x, range.y), high = Mathf.Max(range.x, range.y);
			float weight = 1f;
			if (low > 0f)
			{
				weight *= Edge(heat - low, falloff);
			}
			if (high < 1f)
			{
				weight *= Edge(high - heat, falloff);
			}
			return weight;
		}

		/// <summary>True when a range closes at least one end (an end inside 0 … 1), so it bands anything at all.</summary>
		public static bool Closes(Vector2 range) => Mathf.Min(range.x, range.y) > 0f || Mathf.Max(range.x, range.y) < 1f;

		private static float Edge(float inside, float falloff)
		{
			if (!(falloff > 0f))
			{
				return inside >= 0f ? 1f : 0f;
			}
			float t = Mathf.Clamp01(inside / falloff + 0.5f);
			return t * t * (3f - 2f * t);
		}
	}

	/// <summary>The shapes a heat source takes (append-only: later sources, such as fissure lines, add kinds).</summary>
	public enum BiomeHeatSourceShape : byte
	{
		/// <summary>A site's own heat round one point: Peak at the point falling as (1 − d/Reach)² to nothing at Reach.</summary>
		Point = 0,
		/// <summary>
		/// Heat along a segment from A to B (a spring's runoff): Peak at A easing linearly to PeakB at B, across the line falling
		/// as (1 − d/Reach)² to nothing at Reach either side, and round its ends the same.
		/// </summary>
		Line = 1,
	}

	/// <summary>One source of the ground's heat for the heat mask (<see cref="BiomeHeatMask.Bake"/>).</summary>
	[Serializable]
	public struct BiomeHeatSource
	{
		public BiomeHeatSourceShape Shape;
		public Vector2 A, B;
		public float Peak, PeakB;
		/// <summary>How far the heat reaches from the point or the line, m.</summary>
		public float Reach;

		public static BiomeHeatSource Point(Vector2 at, float peak, float reach) => new BiomeHeatSource { Shape = BiomeHeatSourceShape.Point, A = at, B = at, Peak = peak, PeakB = peak, Reach = reach };

		public static BiomeHeatSource Line(Vector2 a, Vector2 b, float peakA, float peakB, float reach) => new BiomeHeatSource { Shape = BiomeHeatSourceShape.Line, A = a, B = b, Peak = peakA, PeakB = peakB, Reach = reach };

		/// <summary>The box the source warms, world xz.</summary>
		public Rect Bounds => Rect.MinMaxRect(Mathf.Min(A.x, B.x) - Reach, Mathf.Min(A.y, B.y) - Reach, Mathf.Max(A.x, B.x) + Reach, Mathf.Max(A.y, B.y) + Reach);

		/// <summary>The source's heat at a point, 0 … 1.</summary>
		public float At(Vector2 p)
		{
			if (!(Reach > 0f))
			{
				return 0f;
			}
			float d, peak;
			if (Shape == BiomeHeatSourceShape.Line)
			{
				Vector2 ab = B - A;
				float len2 = ab.sqrMagnitude;
				float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - A, ab) / len2) : 0f;
				d = (p - (A + ab * t)).magnitude;
				peak = Mathf.Lerp(Peak, PeakB, t);
			}
			else
			{
				d = (p - A).magnitude;
				peak = Peak;
			}
			if (!(d < Reach))
			{
				return 0f;
			}
			float s = 1f - d / Reach;
			return Mathf.Clamp01(peak) * s * s;
		}
	}

	/// <summary>
	/// The ground's heat over a scene (2026-10-18, phase I2q, decision 47): a grid of bytes over the rectangle its geothermal
	/// sites warm, 0 cold … 255 a spring's throat, baked at the cut from the very sites the client steams and stored in the
	/// scene's biome map (<see cref="SceneBiomeMap.HeatAt"/>), where the splat's and the scatter's heat bands read it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Bake.</b> Every source's heat (<see cref="BiomeHeatSource.At"/>) at each texel's middle, overlapping as a union,
	/// 1 − Π(1 − hᵢ), in the sources' order: deterministic to the byte, whatever machine bakes it. Texels are
	/// <see cref="CellMetres"/> wide, coarsened only when the warmed rectangle would pass <see cref="MaxTexels"/> a side.
	/// </para>
	/// <para><b>Read.</b> Bilinear between texel middles; 0 outside the rectangle and with no bake (a scene with no hot ground).</para>
	/// </remarks>
	[Serializable]
	public struct BiomeHeatMask
	{
		/// <summary>A texel's width, m, unless the rectangle is too big for it.</summary>
		public const float CellMetres = 2f;
		/// <summary>The most texels a side.</summary>
		public const int MaxTexels = 1024;

		public int Width, Height;
		/// <summary>xy the south-west corner, zw the size, m.</summary>
		public Vector4 Area;
		public byte[] Texels;

		public bool Any => Width > 0 && Height > 0 && Texels != null && Texels.Length == Width * Height;

		public static BiomeHeatMask None => new BiomeHeatMask { Texels = new byte[0] };

		/// <summary>
		/// Bakes the sources' heat over the part of <paramref name="scene"/> they warm (empty when none warms any of it).
		/// </summary>
		public static BiomeHeatMask Bake(IReadOnlyList<BiomeHeatSource> sources, Rect scene)
		{
			if (sources == null || sources.Count == 0)
			{
				return None;
			}
			bool any = false;
			Rect box = default;
			foreach (BiomeHeatSource source in sources)
			{
				if (!(source.Reach > 0f) || !(source.Peak > 0f || source.PeakB > 0f))
				{
					continue;
				}
				Rect b = source.Bounds;
				box = any ? UnityEngine.Rect.MinMaxRect(Mathf.Min(box.xMin, b.xMin), Mathf.Min(box.yMin, b.yMin), Mathf.Max(box.xMax, b.xMax), Mathf.Max(box.yMax, b.yMax)) : b;
				any = true;
			}
			if (!any)
			{
				return None;
			}
			box = UnityEngine.Rect.MinMaxRect(Mathf.Max(box.xMin, scene.xMin), Mathf.Max(box.yMin, scene.yMin), Mathf.Min(box.xMax, scene.xMax), Mathf.Min(box.yMax, scene.yMax));
			if (box.width <= 0f || box.height <= 0f)
			{
				return None;
			}
			float cell = Mathf.Max(CellMetres, Mathf.Max(box.width, box.height) / MaxTexels);
			// On a lattice fixed to the scene's metres, so a repaint bakes the same texels.
			float x0 = Mathf.Floor(box.xMin / cell) * cell, z0 = Mathf.Floor(box.yMin / cell) * cell;
			int width = Mathf.Max(1, Mathf.CeilToInt((box.xMax - x0) / cell)), height = Mathf.Max(1, Mathf.CeilToInt((box.yMax - z0) / cell));
			var keep = new float[width * height];
			for (int i = 0; i < keep.Length; i++)
			{
				keep[i] = 1f;
			}
			foreach (BiomeHeatSource source in sources)
			{
				if (!(source.Reach > 0f) || !(source.Peak > 0f || source.PeakB > 0f))
				{
					continue;
				}
				Rect b = source.Bounds;
				int i0 = Mathf.Max(0, Mathf.FloorToInt((b.xMin - x0) / cell)), i1 = Mathf.Min(width - 1, Mathf.CeilToInt((b.xMax - x0) / cell));
				int j0 = Mathf.Max(0, Mathf.FloorToInt((b.yMin - z0) / cell)), j1 = Mathf.Min(height - 1, Mathf.CeilToInt((b.yMax - z0) / cell));
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						float h = source.At(new Vector2(x0 + (i + 0.5f) * cell, z0 + (j + 0.5f) * cell));
						if (h > 0f)
						{
							keep[j * width + i] *= 1f - h;
						}
					}
				}
			}
			var texels = new byte[width * height];
			for (int i = 0; i < texels.Length; i++)
			{
				texels[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - keep[i]) * 255f);
			}
			return new BiomeHeatMask { Width = width, Height = height, Area = new Vector4(x0, z0, width * cell, height * cell), Texels = texels };
		}

		/// <summary>The heat at a world point (xz), 0 … 1: bilinear between texel middles, 0 outside or with no bake.</summary>
		public float At(float x, float z)
		{
			if (!Any || !(Area.z > 0f) || !(Area.w > 0f))
			{
				return 0f;
			}
			if (x < Area.x || z < Area.y || x > Area.x + Area.z || z > Area.y + Area.w)
			{
				return 0f;
			}
			float gx = Mathf.Clamp((x - Area.x) / Area.z * Width - 0.5f, 0f, Width - 1);
			float gz = Mathf.Clamp((z - Area.y) / Area.w * Height - 0.5f, 0f, Height - 1);
			int ix = Mathf.Min((int)gx, Mathf.Max(0, Width - 2)), iz = Mathf.Min((int)gz, Mathf.Max(0, Height - 2));
			int jx = Mathf.Min(ix + 1, Width - 1), jz = Mathf.Min(iz + 1, Height - 1);
			float fx = gx - ix, fz = gz - iz;
			float south = Mathf.Lerp(Texels[iz * Width + ix], Texels[iz * Width + jx], fx);
			float north = Mathf.Lerp(Texels[jz * Width + ix], Texels[jz * Width + jx], fx);
			return Mathf.Lerp(south, north, fz) / 255f;
		}
	}
}
