using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Where a biome's vents stand (<see cref="BiomeVents.Layout"/>, 2026-10-25, phase I2q-b; decisions 53, 90, 95). APPEND-ONLY:
	/// the value is stored in templates and in the scene's stored vent lines (<see cref="VentLine.Layout"/>).
	/// </summary>
	public enum BiomeVentLayout : byte
	{
		/// <summary>On the jittered grids (FishMMO.Shared.Weather.GeothermalVents' 80 m cells, VolcanicVents' kilometre): every biome's before.</summary>
		Grid = 0,
		/// <summary>Along the scene's stored fissure lines (decision 53: an ice moon's tiger stripes; a rift's cracks): a jet every 40–80 m.</summary>
		Fissures = 1,
		/// <summary>In vent zones on a summit terrace (decision 95: Stromboli's three craters): clusters at a quarter, half and three quarters of each stored line, and on each stored pad.</summary>
		Terrace = 2,
		/// <summary>Along the stored ridge lines, sparser (decision 90: a tidally flexed moon's double ridges puffing from their medial troughs).</summary>
		Ridges = 3,
	}

	/// <summary>
	/// How a biome's vents' strength moves (<see cref="BiomeVents.Timing"/>, 2026-10-25, phase I2q-b; decisions 53, 66, 90, 93, 95):
	/// every mode a pure function of the shared clock (and of the sky's state, itself a pure function of the world clock),
	/// presentation only (FishMMO.Shared.Weather.VentTiming). APPEND-ONLY: stored in templates.
	/// </summary>
	public enum BiomeVentTiming : byte
	{
		/// <summary>Steady: as every vent before (a geyser keeps its own eruptions whatever this says).</summary>
		Steady = 0,
		/// <summary>The plume tide (decision 53): strongest at the moon's apocentre (0.3–1.0), each jet flickering in slots of its own; the Great Plume never under 0.4.</summary>
		PlumeTide = 1,
		/// <summary>Follows the sun's height (decision 66: Triton's solar-driven geysers), each plume living some days and then dormant.</summary>
		Solar = 2,
		/// <summary>Strombolian bursts (decision 95): each vent once in 625, 1000 or 1250 s, a hashed strength, one slot in seven skipped.</summary>
		Strombolian = 3,
		/// <summary>The biome's fountain episodes (decision 93: BiomeTemplate.FountainEpisodes, else the volcanic default).</summary>
		Episode = 4,
		/// <summary>The tidal kneading (decision 90): each vent's strength follows the stress across its crack, rotating through every orbit.</summary>
		Kneading = 5,
	}

	/// <summary>
	/// Where a biome's vents stand and how their strength moves (<see cref="BiomeTemplate.Vents"/>, 2026-10-25, phase I2q-b). Read by
	/// the server, every client and the cut alike (GeothermalVents, VolcanicVents). <see cref="Today"/> (the default: Grid, Steady, no
	/// cap) is what every biome's vents did before, to the bit.
	/// </summary>
	[Serializable]
	public struct BiomeVents
	{
		[Tooltip("Where its vents stand: on the grids (every biome's before), along the scene's stored fissure lines, in zones on a terrace (each stored line's quarter points and pads) or along its ridges. A layout other than Grid needs lines the cut stored (a landform with a line in this biome): without them the grid stands.")]
		public BiomeVentLayout Layout;
		[Tooltip("How its vents' strength moves on the shared clock: Steady (as before), the moon's plume tide, the sun's height, Strombolian bursts, the biome's fountain episodes, or the tidal kneading. Presentation only.")]
		public BiomeVentTiming Timing;
		[Tooltip("The most plumes of this biome a scene draws (its vented emission's columns or fountains); 0 for no cap of its own (the scene's eight).")]
		[Min(0)] public int MaxPlumes;

		public BiomeVents(BiomeVentLayout layout, BiomeVentTiming timing, int maxPlumes = 0)
		{
			Layout = layout;
			Timing = timing;
			MaxPlumes = Math.Max(0, maxPlumes);
		}

		/// <summary>Every biome's before: the grid, steady, no cap of its own.</summary>
		public static BiomeVents Today => default;

		/// <summary>True when it is exactly <see cref="Today"/>.</summary>
		public bool IsToday => Layout == BiomeVentLayout.Grid && Timing == BiomeVentTiming.Steady && MaxPlumes <= 0;

		/// <summary>The authoring mark: "layout/timing" (+ "≤cap" only when capped).</summary>
		public string Signature() => Layout + "/" + Timing + (MaxPlumes > 0 ? "≤" + MaxPlumes.ToString(CultureInfo.InvariantCulture) : string.Empty);

		public override string ToString() => Signature();
	}

	/// <summary>
	/// One stretch of a scene's stored vent lines (2026-10-25, phase I2q-b, decision 53): a segment of a landform's line (a tiger
	/// stripe's trough, a rift's crack, a double ridge's medial trough) or a landform's pad (a summit terrace's floor: A = B, no
	/// length), of a biome whose vents do not stand on the grid. Stored with the scene's biome map by the cut
	/// (<see cref="SceneBiomeMap.VentLines"/>), so the server, every client and the cut's carve and heat read the same lines.
	/// </summary>
	[Serializable]
	public struct VentLine
	{
		/// <summary>The segment's ends, world xz (equal for a pad).</summary>
		public Vector2 A, B;
		/// <summary>Metres along its whole line from the line's start to <see cref="A"/>.</summary>
		public float AlongA;
		/// <summary>The whole line's length, metres; 0 for a pad.</summary>
		public float LineLength;
		/// <summary>The line's half width (a pad's radius), metres.</summary>
		public float Half;
		/// <summary>The whole line's own seed (every segment of a line shares it): its vents' draws come from it.</summary>
		public uint Seed;
		/// <summary>The template id of the biome whose landform it is.</summary>
		public int BiomeID;
		/// <summary>That biome's vent layout when the cut stored it.</summary>
		public BiomeVentLayout Layout;

		/// <summary>Metres along its whole line to <see cref="B"/>.</summary>
		public float AlongB => AlongA + Vector2.Distance(A, B);

		/// <summary>True for a pad (a point with a radius), not a segment.</summary>
		public bool IsPad => LineLength <= 0f;

		/// <summary>Its bearing, radians in [0, π): a crack's direction (no sense), what the tidal kneading reads.</summary>
		public float Bearing
		{
			get
			{
				Vector2 d = B - A;
				if (d.sqrMagnitude < 1e-8f)
				{
					return 0f;
				}
				float a = Mathf.Atan2(d.y, d.x);
				return a < 0f ? a + Mathf.PI : a >= Mathf.PI ? a - Mathf.PI : a;
			}
		}
	}

	/// <summary>
	/// A scene's stored vent lines (2026-10-25, phase I2q-b, decision 53: "IceStripes.Of(map)"): the tiger stripes of an ice moon and
	/// every other stored line a vented biome's vents follow, made at the cut from its landforms (<see cref="FromLandforms"/>), stored
	/// with the biome map and read by the vents (FishMMO.Shared.Weather.GeothermalVents, VolcanicVents) and the heat mask
	/// (<see cref="Heat"/>). Pure, apart from <see cref="Of"/> reading a map.
	/// </summary>
	public static class IceStripes
	{
		/// <summary>The heat on a fissure's floor (decision 53: 1), and how far it reaches per metre of its half width, within these bounds, m.</summary>
		public const float FloorHeat = 1f, ReachPerHalf = 12f, LeastReach = 20f, MostReach = 300f;
		/// <summary>A pad's heat at its middle, and how far past its radius it reaches, m.</summary>
		public const float PadHeat = 0.9f, PadReach = 20f;

		private static readonly IReadOnlyList<VentLine> none = new VentLine[0];

		/// <summary>The map's stored vent lines (none on a map cut before 2026-10-25, or with no biome laid out on lines).</summary>
		public static IReadOnlyList<VentLine> Of(SceneBiomeMap map)
		{
			return map != null && map.VentLines != null ? map.VentLines : none;
		}

		/// <summary>True when the lines hold any of biome <paramref name="biomeID"/>'s: its vents then follow them instead of the grid.</summary>
		public static bool HasBiome(IReadOnlyList<VentLine> lines, int biomeID)
		{
			if (lines == null || biomeID == 0)
			{
				return false;
			}
			for (int i = 0; i < lines.Count; i++)
			{
				if (lines[i].BiomeID == biomeID)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The vent lines of a scene's landforms (pure): every segment of every site whose biome's layout (<paramref name="layoutOf"/>, by
		/// template id) is not the grid, in the sites' order and each site's segment order, each run of segments from along 0 one line
		/// (its seed the site's mixed with the run's index); and, for a Terrace biome, each of its pads as a pad line.
		/// </summary>
		public static List<VentLine> FromLandforms(IReadOnlyList<BiomeLandformSite> sites, IReadOnlyList<BiomeLandformPad> pads, Func<int, BiomeVentLayout> layoutOf)
		{
			var lines = new List<VentLine>();
			if (layoutOf == null)
			{
				return lines;
			}
			for (int s = 0; sites != null && s < sites.Count; s++)
			{
				BiomeLandformSite site = sites[s];
				if (site.Segments == null || site.Segments.Count == 0)
				{
					continue;
				}
				BiomeVentLayout layout = layoutOf(site.BiomeID);
				if (layout == BiomeVentLayout.Grid)
				{
					continue;
				}
				int run = -1;
				for (int k = 0; k < site.Segments.Count; k++)
				{
					BiomeLandformSegment seg = site.Segments[k];
					if (k == 0 || seg.AlongA <= 1e-4f)
					{
						run++;
					}
					lines.Add(new VentLine
					{
						A = seg.A,
						B = seg.B,
						AlongA = seg.AlongA,
						LineLength = Mathf.Max(1e-3f, seg.LineLength),
						Half = Mathf.Max(0.25f, seg.Half),
						Seed = Mix(site.Seed, (uint)run),
						BiomeID = site.BiomeID,
						Layout = layout,
					});
				}
			}
			for (int p = 0; pads != null && p < pads.Count; p++)
			{
				BiomeLandformPad pad = pads[p];
				if (pad.Water || layoutOf(pad.BiomeID) != BiomeVentLayout.Terrace)
				{
					continue;
				}
				lines.Add(new VentLine
				{
					A = pad.Centre,
					B = pad.Centre,
					LineLength = 0f,
					Half = Mathf.Max(1f, pad.Radius),
					Seed = Mix((uint)Mathf.RoundToInt(pad.Centre.x * 4f) * 0x9E3779B1u ^ (uint)Mathf.RoundToInt(pad.Centre.y * 4f), 0x7AD5u),
					BiomeID = pad.BiomeID,
					Layout = BiomeVentLayout.Terrace,
				});
			}
			return lines;
		}

		/// <summary>
		/// The heat of the lines for the heat mask (decision 53, I2q's moreHeat): a segment warms its floor to <see cref="FloorHeat"/>
		/// falling to nothing <see cref="ReachPerHalf"/> half widths out (within 20–300 m); a pad <see cref="PadHeat"/> out past its radius.
		/// </summary>
		public static List<BiomeHeatSource> Heat(IReadOnlyList<VentLine> lines)
		{
			var heat = new List<BiomeHeatSource>();
			for (int i = 0; lines != null && i < lines.Count; i++)
			{
				VentLine line = lines[i];
				if (line.IsPad)
				{
					heat.Add(BiomeHeatSource.Point(line.A, PadHeat, line.Half + PadReach));
					continue;
				}
				float reach = Mathf.Clamp(ReachPerHalf * line.Half, LeastReach, MostReach);
				heat.Add(BiomeHeatSource.Line(line.A, line.B, FloorHeat, FloorHeat, reach));
			}
			return heat;
		}

		/// <summary>A 32-bit mix (lowbias32).</summary>
		public static uint Mix(uint v, uint seed)
		{
			unchecked
			{
				uint h = v * 0x9E3779B1u ^ seed;
				h ^= h >> 16;
				h *= 0x7FEB352Du;
				h ^= h >> 15;
				h *= 0x846CA68Bu;
				h ^= h >> 16;
				return h;
			}
		}
	}
}
