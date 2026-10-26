using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What kind of hot ground a geothermal site is. APPEND-ONLY (2026-10-19, decision 53): a kind's number is in every peer's
	/// arithmetic and the cut's records, so new kinds (a fissure's Jet next, after <see cref="MudPot"/>) take the next number and
	/// nothing is renumbered; code that counts kinds sizes by the enum, not by four.
	/// </summary>
	public enum GeothermalKind : byte
	{
		/// <summary>A steam vent: a steady jet of steam out of a crack.</summary>
		Fumarole = 0,
		/// <summary>A pool of hot water steaming over its whole surface.</summary>
		HotSpring = 1,
		/// <summary>A spring whose plumbing boils over now and then: a column of water and steam, then quiet.</summary>
		Geyser = 2,
		/// <summary>
		/// A pot of grey boiling mud (2026-10-18, phase I2q): too little water to make a spring, so the steam and gas bubble up
		/// through clay, plopping and spattering on the shared clock (<see cref="GeothermalVents.Plop"/>), a thin steam over it.
		/// </summary>
		MudPot = 3,
		/// <summary>
		/// A jet (2026-10-25, phase I2q-b, decision 53): a narrow jet of vapour and ice grains out of a fissure, laid along the scene's
		/// stored lines (an ice moon's tiger stripes); on an airless world a fountain of grains, on a thin one a plume leaning downwind.
		/// </summary>
		Jet = 4,
		/// <summary>
		/// A pond of molten sulphur (2026-10-25, phase I2q-b, decision 84): dug and pooled as a hot spring is, wearing its biome's
		/// water look (an opaque Melt look whose colour drifts on the shared clock), fuming thinly; never counted as liquid water.
		/// </summary>
		SulphurPool = 5,
		/// <summary>
		/// A black smoker (2026-10-25, decisions 83 and 97: the next free value after the jets and sulphur pools): a sulphide chimney
		/// in a sea hall's hot floor, its plume dark. Placed only by the sea halls (stored per hall, phase I2n-b), never on the surface.
		/// </summary>
		Smoker = 6,
	}

	/// <summary>
	/// What a biome's ground says about its hot ground, as <see cref="GeothermalVents.Find(Vector2, Rect, uint, Func{Vector2, GeothermalGround}, List{GeothermalSite}, float, int)"/>
	/// reads it at a candidate: the chance of a site in its cell, whether every kind stands there (else fumaroles only), how
	/// the kinds share it and the steam's substance. <see cref="Of"/> reads it off a template.
	/// </summary>
	public struct GeothermalGround
	{
		public float Chance;
		public bool AllKinds;
		public BiomeGeothermalShares Shares;
		public WeatherSubstance Substance;
		/// <summary>The biome's vents' layout and timing (2026-10-25, I2q-b; <see cref="BiomeTemplate.Vents"/>).</summary>
		public BiomeVents Vents;
		/// <summary>The biome's fountain episodes, which an Episode timing follows.</summary>
		public BiomeFountainEpisodes Episodes;
		/// <summary>The biome's template id (0 for none).</summary>
		public int BiomeID;
		/// <summary>
		/// True when the scene stores vent lines of this biome (<see cref="IceStripes.HasBiome"/>): a biome laid out on lines then has no
		/// grid sites, only those on its lines (<see cref="GeothermalVents.FindAlong"/>). Set by the scene's search, never by <see cref="Of"/>.
		/// </summary>
		public bool Lined;

		/// <summary>None: plain ground.</summary>
		public static GeothermalGround None => default;

		/// <summary>A biome's hot ground (<see cref="GeothermalVents.SiteChance"/>, <see cref="BiomeTemplate.Geothermal"/>, its emission).</summary>
		public static GeothermalGround Of(BiomeTemplate biome)
		{
			float chance = GeothermalVents.SiteChance(biome, out bool allKinds);
			return new GeothermalGround
			{
				Chance = chance,
				AllKinds = allKinds,
				Shares = biome != null ? biome.Geothermal : BiomeGeothermalShares.Today,
				// The steam's own look for a steaming biome; a biome with a site rate of its own fumes in its own tint, not its emission's.
				Substance = allKinds && GeothermalVents.IsSteaming(biome) ? biome.Emits : null,
				Vents = biome != null ? biome.Vents : BiomeVents.Today,
				Episodes = biome != null ? biome.FountainEpisodes : default,
				BiomeID = biome != null ? biome.ID : 0,
			};
		}

		/// <summary>True when this ground's sites stand on its lines rather than on the grid.</summary>
		public bool OnLines => Lined && Vents.Layout != BiomeVentLayout.Grid;
	}

	/// <summary>
	/// One plop of a mud pot (2026-10-18, phase I2q): a bubble of steam bursting through the mud and throwing a spatter up,
	/// a pure function of the site and the shared clock (<see cref="GeothermalVents.Plop"/>).
	/// </summary>
	public struct MudPlop
	{
		/// <summary>Which slot of <see cref="GeothermalVents.PlopSeconds"/> this is, from the world's epoch.</summary>
		public long Slot;
		/// <summary>Seconds since this plop burst; negative while it has yet to (or in a slot with no plop).</summary>
		public float Since;
		/// <summary>How long the burst throws spatter, s.</summary>
		public float Duration;
		/// <summary>How long a drop of it flies, up and down again, s.</summary>
		public float Flight;
		/// <summary>Where in the pot it bursts, metres off the pot's middle (xz).</summary>
		public Vector2 Offset;
		/// <summary>How high the spatter is thrown, m.</summary>
		public float Height;

		/// <summary>True from the burst until its last drop is down.</summary>
		public bool Bursting => Since >= 0f && Since < Duration + Flight;
	}

	/// <summary>One geothermal site: where, what kind and how big.</summary>
	public struct GeothermalSite
	{
		/// <summary>World XZ.</summary>
		public Vector2 Position;
		public GeothermalKind Kind;
		/// <summary>How big, 0 the smallest of its kind … 1 the largest.</summary>
		public float Size;
		/// <summary>A stable number for its look and its timing.</summary>
		public uint Seed;
		/// <summary>The biome's steam, when it emits one (its look); null for a volcano's fumaroles, which steam plain white.</summary>
		public WeatherSubstance Substance;
		/// <summary>The colour it fumes (2026-10-25, I2q-b: its biome's <see cref="BiomeGeothermalShares.FumeTint"/>); alpha 0 for its steam's own.</summary>
		public Color Fume;
		/// <summary>How its strength moves on the shared clock (its biome's <see cref="BiomeVents.Timing"/>; presentation only, <see cref="VentTiming"/>).</summary>
		public BiomeVentTiming Timing;
		/// <summary>The fountain episodes an Episode timing follows (its biome's; empty: the volcanic default).</summary>
		public BiomeFountainEpisodes Episodes;
		/// <summary>Its crack's bearing, radians in [0, π): its line's for a site on a line, the kneading glow's field elsewhere.</summary>
		public float Bearing;
		/// <summary>True for a site laid on a stored line (<see cref="GeothermalVents.FindAlong"/>), false for a grid site.</summary>
		public bool OnLine;
	}

	/// <summary>A geyser at one moment: which eruption, how far into it, and what it is doing.</summary>
	public struct GeyserMoment
	{
		/// <summary>Which eruption this is, counted from the world's epoch: one per interval.</summary>
		public long Eruption;
		/// <summary>Seconds since this eruption began; negative while it has yet to begin.</summary>
		public float Since;
		/// <summary>How long this eruption plays, s.</summary>
		public float Duration;
		/// <summary>How tall the water column stands now, as a share of its full height (0 between eruptions).</summary>
		public float Column;
		/// <summary>How much steam the eruption is putting up, 0..1: all of it while it plays, dying away after.</summary>
		public float Steam;

		public bool Erupting => Since >= 0f && Since < Duration;
	}

	/// <summary>
	/// The hot ground of a scene: fumaroles, hot springs and geysers, where they are, how big, and when
	/// each geyser goes off.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> On a jittered grid of <see cref="CellMetres"/> cells anchored on the scene's biome map:
	/// one candidate per cell, kept where the biome there steams. A biome that emits a vapour
	/// (<see cref="WeatherSubstance.Vapour"/>: the geyser basin's steam) is set with all three kinds, at a
	/// chance per cell of <see cref="SitesPerEmission"/> × its emission rate. A biome whose emission comes
	/// out of vents (<see cref="VolcanicVents.IsVented"/>: the volcanic and cryovolcanic ground) has
	/// fumaroles only, more thinly: water vapour is most of what a magma degasses (60–95 % of volcanic gas
	/// by mole: Symonds et al. 1994, Reviews in Mineralogy 30), so every volcanic field steams from its
	/// cracks between eruptions. Seeded from the map's name, so every client and the server agree on them
	/// without a word on the wire. A finer grid than <see cref="VolcanicVents"/>' kilometre: a geyser
	/// basin's springs stand tens of metres apart (Yellowstone's Upper Geyser Basin holds some 150 geysers
	/// in a few square kilometres).
	/// </para>
	/// <para>
	/// <b>When.</b> A geyser's plumbing refills and reheats at a steady rate, so it erupts at a steady
	/// interval with a scatter: Old Faithful's runs 60–110 minutes, its eruptions 1.5–5 minutes. Here each
	/// interval of the world clock is one slot holding one eruption, started at a seeded point inside it
	/// and lasting a seeded share of the geyser's own duration, so the intervals between eruptions scatter
	/// about the mean while the schedule stays a pure O(1) function of the world clock: every player sees
	/// the same eruption at the same moment and a rejoin finds it mid-play. The eruption and the steam
	/// that drifts off after it always end inside their slot, so only the current slot is ever read.
	/// </para>
	/// <para>
	/// <b>How tall.</b> A geyser's column is set by the pressure its superheated water flashes at, which
	/// fixes its launch speed; the column stands v²/2g, so on a world of weaker pull the same plumbing
	/// throws it higher. Most geysers are small (the sizes are skewed), a few stand tens of metres.
	/// </para>
	/// </remarks>
	public static class GeothermalVents
	{
		/// <summary>One candidate site per cell this wide, m.</summary>
		public const float CellMetres = 80f;
		/// <summary>A steaming biome's chance of a site in a cell, per unit of emission rate.</summary>
		public const float SitesPerEmission = 1.4f;
		/// <summary>A vented (volcanic) biome's chance of a fumarole in a cell, per unit of emission rate.</summary>
		public const float FumarolesPerVentedEmission = 1f;
		/// <summary>
		/// In a steaming biome whose split is <see cref="BiomeGeothermalShares.Today"/> (every biome's until 2026-10-18), the share
		/// of sites that are geysers, and that are hot springs; the rest are fumaroles. A template's own split
		/// (<see cref="BiomeTemplate.Geothermal"/>) says otherwise (<see cref="KindOf"/>).
		/// </summary>
		public const float GeyserShare = 0.25f, HotSpringShare = 0.4f;
		/// <summary>
		/// The most sites one search returns, the first in grid order: more than the client's search round its camera can hold
		/// ((2 × 1560 m ÷ 80 m + 1)² = 1600 cells), so that search is never cut short and finds every site the cut carved
		/// under (2026-10-18, I2q; it was 512, which a whole geyser basin could fill from its southern rows). The cut searches
		/// the whole scene with no cap (<see cref="SceneSites"/>).
		/// </summary>
		public const int MaxSites = 2048;

		/// <summary>The shortest and longest mean intervals between a geyser's eruptions, s.</summary>
		public const float ShortestInterval = 180f, LongestInterval = 4200f;
		/// <summary>The shortest and longest eruption, s.</summary>
		public const float ShortestEruption = 15f, LongestEruption = 120f;
		/// <summary>How long the steam hangs after the smallest and the largest eruption, s.</summary>
		public const float ShortestSteam = 30f, LongestSteam = 180f;
		/// <summary>The smallest and largest column on our own world, m.</summary>
		public const float LowestColumn = 4f, HighestColumn = 50f;
		/// <summary>How far an eruption's start may wander inside its slot, as a share of the slot's slack.</summary>
		public const float StartScatter = 0.35f;
		/// <summary>How far an eruption's length may stray from the geyser's own, either way (a share).</summary>
		public const float DurationScatter = 0.2f;

		/// <summary>Whether a biome's ground steams with every kind of site: it emits a vapour.</summary>
		public static bool IsSteaming(BiomeTemplate biome)
		{
			return biome != null && biome.Emits != null && biome.Emits.Vapour && biome.EmissionRate > 0f;
		}

		/// <summary>
		/// The chance of a site in a cell of a biome, and whether every kind of its split is among them (else fumaroles only): a
		/// steaming biome's by its emission, as before; else, a biome with a site rate of its own (2026-10-25, decision 84:
		/// <see cref="BiomeGeothermalShares.Rate"/>) every kind of its split at that rate, whatever it emits; else a vented biome's
		/// fumaroles by its emission, as before.
		/// </summary>
		public static float SiteChance(BiomeTemplate biome, out bool allKinds)
		{
			allKinds = IsSteaming(biome);
			if (allKinds)
			{
				return Mathf.Clamp01(SitesPerEmission * biome.EmissionRate);
			}
			if (biome != null && biome.Geothermal.OwnRate)
			{
				allKinds = true;
				return Mathf.Clamp01(SitesPerEmission * biome.Geothermal.Rate);
			}
			return VolcanicVents.IsVented(biome) ? Mathf.Clamp01(FumarolesPerVentedEmission * biome.EmissionRate) : 0f;
		}

		/// <summary>
		/// The sites in an area: one jittered candidate per cell of a grid anchored at
		/// <paramref name="origin"/>, kept where <paramref name="biomeAt"/> says the ground steams. Pure:
		/// the same origin, seed and biomes give the same sites, whatever area is asked about.
		/// </summary>
		public static void Find(Vector2 origin, Rect area, uint seed, Func<Vector2, BiomeTemplate> biomeAt, List<GeothermalSite> into, float cellMetres = CellMetres)
		{
			if (biomeAt == null)
			{
				into.Clear();
				return;
			}
			Find(origin, area, seed, p => GeothermalGround.Of(biomeAt(p)), into, cellMetres, MaxSites);
		}

		/// <summary>
		/// <see cref="Find(Vector2, Rect, uint, Func{Vector2, BiomeTemplate}, List{GeothermalSite}, float)"/> with the ground read
		/// as <see cref="GeothermalGround"/> (what a template gives, <see cref="GeothermalGround.Of"/>) and a cap of
		/// <paramref name="maxSites"/>: the one search the client, the server and the cut all run.
		/// </summary>
		public static void Find(Vector2 origin, Rect area, uint seed, Func<Vector2, GeothermalGround> groundAt, List<GeothermalSite> into, float cellMetres, int maxSites)
		{
			into.Clear();
			if (groundAt == null || area.width <= 0f || area.height <= 0f)
			{
				return;
			}
			int cap = Math.Max(0, maxSites);
			float cell = Mathf.Max(5f, cellMetres);
			int i0 = Mathf.FloorToInt((area.xMin - origin.x) / cell), i1 = Mathf.FloorToInt((area.xMax - origin.x) / cell);
			int j0 = Mathf.FloorToInt((area.yMin - origin.y) / cell), j1 = Mathf.FloorToInt((area.yMax - origin.y) / cell);
			for (int j = j0; j <= j1 && into.Count < cap; j++)
			{
				for (int i = i0; i <= i1 && into.Count < cap; i++)
				{
					uint h = Hash(seed, i, j);
					// Inside the cell's middle four fifths, so two cells' sites never stand together.
					float x = origin.x + (i + 0.1f + 0.8f * Unit(h)) * cell;
					float z = origin.y + (j + 0.1f + 0.8f * Unit(h * 747796405u + 1u)) * cell;
					if (x < area.xMin || x > area.xMax || z < area.yMin || z > area.yMax)
					{
						continue;
					}
					var at = new Vector2(x, z);
					GeothermalGround ground = groundAt(at);
					float chance = ground.Chance;
					// A biome laid out on the scene's stored lines has its sites there instead (2026-10-25, I2q-b; FindAlong).
					if (chance <= 0f || ground.OnLines || Unit(h * 2891336453u + 7u) >= chance)
					{
						continue;
					}
					GeothermalKind kind = GeothermalKind.Fumarole;
					if (ground.AllKinds)
					{
						kind = KindOf(Unit(h * 1103515245u + 12345u), ground.Shares);
					}
					into.Add(new GeothermalSite
					{
						Position = at,
						Kind = kind,
						Size = Unit(h * 3266489917u + 3u),
						Seed = h,
						Substance = ground.AllKinds ? ground.Substance : null,
						Fume = ground.Shares.FumeTint,
						Timing = ground.Vents.Timing,
						Episodes = ground.Episodes,
						Bearing = VentTiming.FieldBearing(x, z),
					});
				}
			}
		}

		/// <summary>
		/// A steaming site's kind from its draw <paramref name="k"/> (0 … 1) and the biome's split: geysers first, then springs,
		/// fumaroles, mud pots, each its share of the sum. The split every biome had before 2026-10-18
		/// (<see cref="BiomeGeothermalShares.Today"/>, and any split nobody filled in) takes the old comparison verbatim, so
		/// those sites are what they were to the bit.
		/// </summary>
		public static GeothermalKind KindOf(float k, in BiomeGeothermalShares shares)
		{
			float total = shares.Total;
			if (shares.IsToday || !(total > 0f))
			{
				return k < GeyserShare ? GeothermalKind.Geyser : k < GeyserShare + HotSpringShare ? GeothermalKind.HotSpring : GeothermalKind.Fumarole;
			}
			float geyser = shares.Geyser / total;
			float spring = (shares.Geyser + shares.HotSpring) / total;
			float vent = (shares.Geyser + shares.HotSpring + shares.Fumarole) / total;
			if (k < geyser)
			{
				return GeothermalKind.Geyser;
			}
			if (k < spring)
			{
				return GeothermalKind.HotSpring;
			}
			if (shares.Jet <= 0f && shares.SulphurPool <= 0f)
			{
				// Every split before 2026-10-25 (no jets, no sulphur pools): the comparison as it was, to the bit.
				return shares.MudPot > 0f && k >= vent ? GeothermalKind.MudPot : GeothermalKind.Fumarole;
			}
			// Then mud pots, jets and sulphur pools, each its share (2026-10-25, I2q-b); a draw rounding past the last goes to the last named.
			float mud = (shares.Geyser + shares.HotSpring + shares.Fumarole + shares.MudPot) / total;
			float jet = (shares.Geyser + shares.HotSpring + shares.Fumarole + shares.MudPot + shares.Jet) / total;
			if (k < vent)
			{
				return GeothermalKind.Fumarole;
			}
			if (k < mud && shares.MudPot > 0f)
			{
				return GeothermalKind.MudPot;
			}
			if (k < jet && shares.Jet > 0f)
			{
				return GeothermalKind.Jet;
			}
			if (shares.SulphurPool > 0f)
			{
				return GeothermalKind.SulphurPool;
			}
			return shares.Jet > 0f ? GeothermalKind.Jet : shares.MudPot > 0f ? GeothermalKind.MudPot : GeothermalKind.Fumarole;
		}

		// ── Sites on the scene's stored lines (2026-10-25, phase I2q-b, decisions 53, 90, 95) ──

		/// <summary>The spacing of a jet along a fissure, and of a vent along a ridge, m (decision 53: one every 40–80 m of stripe).</summary>
		public const float FissureSpacing = 60f, RidgeSpacing = 200f;
		/// <summary>How far a jet may stray along its slot of a line, as a share of the spacing (either way), and across it, as a share of the half width.</summary>
		public const float AlongScatter = 0.3f, AcrossScatter = 0.3f;
		/// <summary>The share of a ridge's slots that hold a vent (a fissure's all do).</summary>
		public const float RidgeKeep = 0.6f;
		/// <summary>A terrace's vent zones, at these shares of each line (Stromboli's north-east, central and south-west craters).</summary>
		public static readonly float[] TerraceZones = { 0.25f, 0.5f, 0.75f };
		/// <summary>The most vents a terrace zone holds, and how far apart along it they stand, m.</summary>
		public const int TerraceVents = 3;
		public const float TerraceSpread = 12f;

		/// <summary>
		/// The sites on a scene's stored vent lines (pure; appended to <paramref name="into"/> up to <paramref name="maxSites"/>): on a
		/// Fissures line a jet every <see cref="FissureSpacing"/> m (hash-jittered along and across it), on a Ridges line a jet in
		/// six of ten <see cref="RidgeSpacing"/> m slots, on a Terrace line 1–3 vents in each of its three zones and on a terrace pad
		/// 1–3 across its middle; each kept only where <paramref name="groundAt"/> is hot ground laid out on lines, inside
		/// <paramref name="area"/>. A slot belongs to the one segment of its line it falls on, so a line's sites never double where its
		/// segments meet. A terrace's vents are of its ground's split (fumaroles for a vented ground).
		/// </summary>
		public static void FindAlong(IReadOnlyList<VentLine> lines, Rect area, uint seed, Func<Vector2, GeothermalGround> groundAt, List<GeothermalSite> into, int maxSites)
		{
			if (lines == null || groundAt == null || area.width <= 0f || area.height <= 0f)
			{
				return;
			}
			for (int i = 0; i < lines.Count && into.Count < maxSites; i++)
			{
				VentLine line = lines[i];
				uint lineSeed = seed ^ unchecked(line.Seed * 0x2C1B3C6Du);
				if (line.Layout == BiomeVentLayout.Fissures || line.Layout == BiomeVentLayout.Ridges)
				{
					if (line.IsPad)
					{
						continue;
					}
					bool ridge = line.Layout == BiomeVentLayout.Ridges;
					float spacing = ridge ? RidgeSpacing : FissureSpacing;
					float alongA = line.AlongA, alongB = line.AlongB;
					long k0 = (long)Math.Floor(alongA / spacing) - 1, k1 = (long)Math.Floor(alongB / spacing);
					for (long k = k0; k <= k1 && into.Count < maxSites; k++)
					{
						uint h = Hash(lineSeed, (int)(k & 0x7FFFFFFF), ridge ? 0x5A17 : 0x7151);
						float s = (k + 0.5f + 2f * AlongScatter * (Unit(h) - 0.5f)) * spacing;
						if (s < alongA || s >= alongB || (ridge && Unit(h * 2891336453u + 7u) >= RidgeKeep))
						{
							continue;
						}
						float t = (s - alongA) / Mathf.Max(1e-4f, alongB - alongA);
						AddOnLine(line, t, 2f * AcrossScatter * (Unit(h * 747796405u + 1u) - 0.5f) * line.Half, h, area, groundAt, into, true);
					}
				}
				else if (line.Layout == BiomeVentLayout.Terrace)
				{
					if (line.IsPad)
					{
						uint h = Hash(lineSeed, 0, 0x7E44);
						int n = 1 + (int)(h % TerraceVents);
						float heading = Unit(h * 3266489917u + 3u) * Mathf.PI;
						var way = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
						float step = Mathf.Min(TerraceSpread, line.Half * 0.5f);
						for (int v = 0; v < n && into.Count < maxSites; v++)
						{
							AddAt(line.A + way * ((v - 0.5f * (n - 1)) * step), heading, Hash(lineSeed, v + 1, 0x7E45), area, groundAt, into, false);
						}
						continue;
					}
					for (int z = 0; z < TerraceZones.Length; z++)
					{
						float s = TerraceZones[z] * line.LineLength;
						if (s < line.AlongA || s >= line.AlongB)
						{
							continue;
						}
						uint h = Hash(lineSeed, z, 0x7E46);
						int n = 1 + (int)(h % TerraceVents);
						for (int v = 0; v < n && into.Count < maxSites; v++)
						{
							uint hv = Hash(lineSeed, z * 8 + v + 1, 0x7E47);
							float t = Mathf.Clamp01((s + (v - 0.5f * (n - 1)) * TerraceSpread - line.AlongA) / Mathf.Max(1e-4f, line.AlongB - line.AlongA));
							AddOnLine(line, t, 2f * AcrossScatter * (Unit(hv * 747796405u + 1u) - 0.5f) * line.Half, hv, area, groundAt, into, false);
						}
					}
				}
			}
		}

		private static void AddOnLine(in VentLine line, float t, float across, uint h, Rect area, Func<Vector2, GeothermalGround> groundAt, List<GeothermalSite> into, bool jet)
		{
			Vector2 d = line.B - line.A;
			Vector2 normal = d.sqrMagnitude > 1e-8f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;
			AddAt(line.A + d * t + normal * across, line.Bearing, h, area, groundAt, into, jet);
		}

		private static void AddAt(Vector2 at, float bearing, uint h, Rect area, Func<Vector2, GeothermalGround> groundAt, List<GeothermalSite> into, bool jet)
		{
			if (at.x < area.xMin || at.x > area.xMax || at.y < area.yMin || at.y > area.yMax)
			{
				return;
			}
			GeothermalGround ground = groundAt(at);
			if (ground.Chance <= 0f || !ground.OnLines)
			{
				return;
			}
			GeothermalKind kind = jet ? GeothermalKind.Jet : ground.AllKinds ? KindOf(Unit(h * 1103515245u + 12345u), ground.Shares) : GeothermalKind.Fumarole;
			into.Add(new GeothermalSite
			{
				Position = at,
				Kind = kind,
				Size = Unit(h * 3266489917u + 3u),
				Seed = h,
				Substance = ground.AllKinds ? ground.Substance : null,
				Fume = ground.Shares.FumeTint,
				Timing = ground.Vents.Timing,
				Episodes = ground.Episodes,
				Bearing = bearing,
				OnLine = true,
			});
		}

		/// <summary>The ground a scene's search reads at a point: the template's (<see cref="GeothermalGround.Of"/>), marked lined when the map stores its lines.</summary>
		public static GeothermalGround GroundOf(SceneBiomeMap map, IReadOnlyList<VentLine> lines, Vector2 p)
		{
			GeothermalGround ground = GeothermalGround.Of(map.Sample(new Vector3(p.x, 0f, p.y)));
			ground.Lined = ground.BiomeID != 0 && lines != null && lines.Count > 0 && IceStripes.HasBiome(lines, ground.BiomeID);
			return ground;
		}

		// ── The Great Plume (2026-10-25, phase I2q-b, decision 53) ──

		/// <summary>
		/// The scene's Great Plume among its sites: the jet on a line of the largest size, the smaller seed on a tie, among those
		/// <paramref name="eligible"/> keeps (null: all); −1 for none. At most one a scene; the cut stores its seed with the map.
		/// </summary>
		public static int GreatPlumeIndex(IReadOnlyList<GeothermalSite> sites, Func<int, bool> eligible = null)
		{
			int best = -1;
			for (int i = 0; sites != null && i < sites.Count; i++)
			{
				GeothermalSite s = sites[i];
				if (s.Kind != GeothermalKind.Jet || !s.OnLine || (eligible != null && !eligible(i)))
				{
					continue;
				}
				if (best < 0 || s.Size > sites[best].Size || (s.Size == sites[best].Size && s.Seed < sites[best].Seed))
				{
					best = i;
				}
			}
			return best;
		}

		/// <summary>True when the site is the scene's Great Plume, as the cut stored it with the map.</summary>
		public static bool IsGreatPlume(SceneBiomeMap map, in GeothermalSite site)
		{
			return map != null && map.HasGreatPlume && site.Kind == GeothermalKind.Jet && site.Seed == map.GreatPlumeSeed;
		}

		/// <summary>The seed a scene's sites are placed with, from its biome map's name (salted apart from the volcanic vents').</summary>
		public static uint SeedOf(SceneBiomeMap map)
		{
			return map != null ? SeedOf(map.name) : 0u;
		}

		/// <summary>
		/// The seed from a biome map's asset name: what <see cref="SeedOf(SceneBiomeMap)"/> gives the map of that name, for the cut,
		/// which places the sites before the map asset is written (2026-10-18, I2q).
		/// </summary>
		public static uint SeedOf(string mapName)
		{
			return mapName != null ? unchecked((uint)mapName.GetDeterministicHashCode() ^ 0x5BD1E995u) : 0u;
		}

		/// <summary>
		/// Every site of a scene (2026-10-18, I2q): the whole map's rectangle, searched as <see cref="Near"/> searches a corner of
		/// it (the same origin, seed and biomes, so every site <see cref="Near"/> finds is among these), with no cap. What the cut
		/// carves under and where the Great Spout is found (<see cref="GreatSpoutIndex"/>).
		/// </summary>
		public static void SceneSites(SceneBiomeMap map, List<GeothermalSite> into)
		{
			into.Clear();
			if (map == null || map.WorldSize.x <= 0f || map.WorldSize.y <= 0f)
			{
				return;
			}
			IReadOnlyList<VentLine> lines = IceStripes.Of(map);
			var scene = new Rect(map.WorldOrigin, map.WorldSize);
			Find(map.WorldOrigin, scene, SeedOf(map), p => GroundOf(map, lines, p), into, CellMetres, int.MaxValue);
			// The sites on its stored lines after the grid's (2026-10-25, I2q-b): none on a map that stores none.
			FindAlong(lines, scene, SeedOf(map), p => GroundOf(map, lines, p), into, int.MaxValue);
		}

		/// <summary>The sites of a scene within <paramref name="radius"/> of a point. Empty with no biome map.</summary>
		public static void Near(WorldSceneSettings settings, Vector2 centre, float radius, List<GeothermalSite> into)
		{
			into.Clear();
			SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
			if (map == null || map.WorldSize.x <= 0f || map.WorldSize.y <= 0f || radius <= 0f)
			{
				return;
			}
			Rect scene = new Rect(map.WorldOrigin, map.WorldSize);
			Rect near = Rect.MinMaxRect(Mathf.Max(scene.xMin, centre.x - radius), Mathf.Max(scene.yMin, centre.y - radius),
				Mathf.Min(scene.xMax, centre.x + radius), Mathf.Min(scene.yMax, centre.y + radius));
			if (near.width <= 0f || near.height <= 0f)
			{
				return;
			}
			IReadOnlyList<VentLine> lines = IceStripes.Of(map);
			Find(map.WorldOrigin, near, SeedOf(map), p => GroundOf(map, lines, p), into, CellMetres, MaxSites);
			FindAlong(lines, near, SeedOf(map), p => GroundOf(map, lines, p), into, MaxSites);
		}

		// ── What each kind is like ─────────────────────────────────────

		/// <summary>
		/// The vent's mouth or the pool's radius, m: a fumarole's crack a metre or so, a geyser's vent one to
		/// three, a hot spring's pool two to eight.
		/// </summary>
		public static float SourceRadius(in GeothermalSite site)
		{
			float u = Mathf.Clamp01(site.Size);
			switch (site.Kind)
			{
				case GeothermalKind.HotSpring: return 2f + 6f * u;
				case GeothermalKind.Geyser: return 1f + 2f * u;
				case GeothermalKind.MudPot: return 1f + 3f * u;
				// A jet's slot in the fissure; a smoker's chimney; a sulphur pond's basin 3–12 m across (decision 84).
				case GeothermalKind.Jet: return 0.3f + 0.7f * u;
				case GeothermalKind.Smoker: return 0.5f + 1.5f * u;
				case GeothermalKind.SulphurPool: return 1.5f + 4.5f * u;
				default: return 0.4f + 1.2f * u;
			}
		}

		/// <summary>
		/// The length its steam dilutes over at the start, m (SteamPhysics.VisibleLength): about the size of
		/// what feeds it. A pool's steam starts over its whole surface, so half its radius; an erupting
		/// geyser's flash steam a few metres.
		/// </summary>
		public static float VirtualSource(in GeothermalSite site, bool erupting = false)
		{
			float u = Mathf.Clamp01(site.Size);
			switch (site.Kind)
			{
				case GeothermalKind.HotSpring: return 0.5f * SourceRadius(site);
				case GeothermalKind.Geyser: return erupting ? 2f + 4f * u : 0.5f + 0.5f * u;
				case GeothermalKind.MudPot: return 0.4f * SourceRadius(site);
				case GeothermalKind.Jet: return 0.5f + 0.5f * u;
				case GeothermalKind.SulphurPool: return 0.4f * SourceRadius(site);
				default: return 0.6f + 1.2f * u;
			}
		}

		/// <summary>How fast its steam climbs off it, m/s: a fumarole jets, a pool's steam only drifts up, an eruption's boils up fast.</summary>
		public static float RiseSpeed(in GeothermalSite site, bool erupting = false)
		{
			float u = Mathf.Clamp01(site.Size);
			switch (site.Kind)
			{
				case GeothermalKind.HotSpring: return 0.6f + 0.6f * u;
				case GeothermalKind.Geyser: return erupting ? 6f + 8f * u : 1f + u;
				case GeothermalKind.MudPot: return 0.4f + 0.4f * u;
				case GeothermalKind.Jet: return 6f + 10f * u;
				case GeothermalKind.Smoker: return 1f + u;
				case GeothermalKind.SulphurPool: return 0.3f + 0.3f * u;
				default: return 2f + 4f * u;
			}
		}

		/// <summary>
		/// How hot what steams is, °C: a vent's steam and a geyser's water at boiling for the air's pressure;
		/// a pool's surface 8–28 K under it, cooled by its own evaporation (Yellowstone's springs run 70–92 °C).
		/// </summary>
		public static float SourceC(in GeothermalSite site, float boilingC)
		{
			switch (site.Kind)
			{
				case GeothermalKind.HotSpring: return boilingC - Mathf.Lerp(28f, 8f, Mathf.Clamp01(site.Size));
				// A mud pot's surface: the clay holds it a few kelvin under boiling, the bubbles at it.
				case GeothermalKind.MudPot: return boilingC - Mathf.Lerp(15f, 4f, Mathf.Clamp01(site.Size));
				default: return boilingC;
			}
		}

		// ── The ground a site stands in (2026-10-18, phase I2q) ─────────
		/* The cut carves each site's ground (GeothermalGroundCarve, the editor's), and the client stands its steam on that ground:
		 * both read these, so the vent the steam rises from is the throat the cut dug. */

		/// <summary>
		/// How far a hot spring's funnel lies under its water at the deepest, m: the pool's throat (Yellowstone's pools run from a
		/// metre to tens of metres; a 2 m heightmap holds a few). The cut floods the pool to just under its rim; the client takes
		/// a spring whose water stands no deeper than this over its ground for its own pool, and steams it from the surface.
		/// </summary>
		public static float PoolDepth(in GeothermalSite site)
		{
			// A sulphur pond is a shallow pan of melt (decision 84), a spring's funnel deeper.
			return site.Kind == GeothermalKind.SulphurPool ? 0.4f + 1.2f * Mathf.Clamp01(site.Size) : 0.8f + 2.2f * Mathf.Clamp01(site.Size);
		}

		/// <summary>True for a kind whose site is a pool the cut digs and floods (a hot spring, a sulphur pond, 2026-10-25).</summary>
		public static bool IsPool(GeothermalKind kind) => kind == GeothermalKind.HotSpring || kind == GeothermalKind.SulphurPool;

		/// <summary>How far under its rim a mud pot's dish is dug, m.</summary>
		public static float DishDepth(in GeothermalSite site)
		{
			return 0.3f + 0.6f * Mathf.Clamp01(site.Size);
		}

		/// <summary>A geyser's sinter cone: how tall over the ground round it and how wide its foot, m (its orifice in the top).</summary>
		public static float ConeMetres(in GeothermalSite site) => 0.4f + 1.6f * Mathf.Clamp01(site.Size);

		/// <summary>The cone's foot radius, m.</summary>
		public static float ConeRadius(in GeothermalSite site) => 2.5f + 4.5f * Mathf.Clamp01(site.Size);

		/// <summary>The beaded sinter rim a spring or mud pot stands in: how high over the basin's level, and how wide, m.</summary>
		public const float RimMetres = 0.25f, RimWidth = 1.5f;

		/// <summary>
		/// How far a site warms the ground round it, m: the reach of its heat (<see cref="Heat"/>), apron, warm ring and the green
		/// halo past it. A geyser's splash and runoff spread widest, a fumarole's crack least.
		/// </summary>
		public static float HeatReach(in GeothermalSite site)
		{
			float u = Mathf.Clamp01(site.Size);
			switch (site.Kind)
			{
				case GeothermalKind.Geyser: return 10f + 24f * u;
				case GeothermalKind.HotSpring: return SourceRadius(site) + 6f + 18f * u;
				case GeothermalKind.MudPot: return SourceRadius(site) + 4f + 6f * u;
				case GeothermalKind.Jet: return 6f + 10f * u;
				case GeothermalKind.Smoker: return 8f + 12f * u;
				case GeothermalKind.SulphurPool: return SourceRadius(site) + 6f + 12f * u;
				default: return 4f + 8f * u;
			}
		}

		/// <summary>How hot a site's own ground is at its middle, 0 … 1.</summary>
		public static float HeatPeak(in GeothermalSite site)
		{
			switch (site.Kind)
			{
				case GeothermalKind.Geyser: return 1f;
				case GeothermalKind.HotSpring: return 0.95f;
				case GeothermalKind.MudPot: return 0.85f;
				case GeothermalKind.Jet: return 0.9f;
				case GeothermalKind.Smoker:
				case GeothermalKind.SulphurPool: return 1f;
				default: return 0.8f;
			}
		}

		/// <summary>
		/// The ground's heat from one site at <paramref name="distance"/> m, 0 … 1: its peak, falling as (1 − d/R)² to nothing at
		/// its reach R, so a fifth of the way out is still bare hot ground (≥ 0.65 for a geyser), two fifths a warm ring, and the
		/// rest a halo. Several sites (and runoff) overlap as a union, 1 − Π(1 − hᵢ) (the heat mask's bake).
		/// </summary>
		public static float Heat(in GeothermalSite site, float distance)
		{
			float reach = HeatReach(site);
			if (!(distance < reach))
			{
				return 0f;
			}
			float s = 1f - Mathf.Max(0f, distance) / reach;
			return HeatPeak(site) * s * s;
		}

		// ── Mud pots' plops (2026-10-18, phase I2q) ──────────────────────

		/// <summary>A mud pot's clock ticks this often, s: at most one plop a tick in each of its lanes, down again inside it.</summary>
		public const float PlopSeconds = 1.2f;
		/// <summary>How many plops may overlap in one pot: lanes of their own ticks, offset from each other.</summary>
		public const int PlopLanes = 2;

		/// <summary>
		/// The plop of a mud pot's lane at a moment of world time: a pure function of the site and the clock. Each tick of
		/// <see cref="PlopSeconds"/> may hold one (three in five do), bursting at a seeded moment in its first quarter, somewhere in
		/// the inner seven tenths of the pot, its spatter thrown 0.15–0.6 m (more from a bigger pot) for a fifth of a second and
		/// down again within the tick, so only the current tick is ever read.
		/// </summary>
		public static MudPlop Plop(in GeothermalSite site, double worldSeconds, int lane = 0)
		{
			uint laneSeed = unchecked(site.Seed * 0x9E3779B1u + (uint)lane * 0x85EBCA77u + 17u);
			double phase = Unit(laneSeed) * PlopSeconds;
			long slot = (long)Math.Floor((worldSeconds + phase) / PlopSeconds);
			uint h = Hash(laneSeed, (int)(slot & 0x7FFFFFFF), (int)(slot >> 31));
			var plop = new MudPlop { Slot = slot, Since = -1f };
			if (Unit(h) >= 0.6f)
			{
				return plop;
			}
			float u = Mathf.Clamp01(site.Size);
			float height = (0.15f + 0.45f * Unit(h * 2654435761u + 1u)) * (0.6f + 0.4f * u);
			// Up and down again: 2·√(2h/g), on our own world's pull (a presenter on another world may scale it).
			float flight = 2f * Mathf.Sqrt(2f * height / FishMMO.Shared.Celestial.SurfacePhysics.EarthGravity);
			double start = slot * (double)PlopSeconds - phase + 0.25 * PlopSeconds * Unit(h * 747796405u + 3u);
			float angle = Unit(h * 3266489917u + 5u) * Mathf.PI * 2f;
			float radius = 0.7f * SourceRadius(site) * Mathf.Sqrt(Unit(h * 1103515245u + 7u));
			plop.Since = (float)(worldSeconds - start);
			plop.Duration = 0.2f;
			plop.Flight = flight;
			plop.Offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			plop.Height = height;
			return plop;
		}

		// ── The Great Spout (2026-10-18, phase I2q) ──────────────────────

		/// <summary>
		/// The scene's Great Spout among its sites (<see cref="SceneSites"/>): the geyser of the largest size, the smaller seed on a
		/// tie; −1 when the scene has no geyser. At most one a scene, the same for every peer: a Spout clock (I7) reads its
		/// eruptions from <see cref="Moment"/> and <see cref="SecondsToNextEruption"/>, as the client's steam does.
		/// </summary>
		public static int GreatSpoutIndex(IReadOnlyList<GeothermalSite> sites) => GreatSpoutIndex(sites, null);

		/// <summary>
		/// <see cref="GreatSpoutIndex(IReadOnlyList{GeothermalSite})"/> among the sites <paramref name="eligible"/> keeps (by index; null
		/// keeps every one): the cut asks it of the geysers it dug a cone for, so the Great Spout is never one under a lake or the
		/// sea, which the client never draws (GeothermalGroundCarve.Make), and stores the answer with the map (<see cref="TryGreatSpout"/>).
		/// </summary>
		public static int GreatSpoutIndex(IReadOnlyList<GeothermalSite> sites, Func<int, bool> eligible)
		{
			int best = -1;
			for (int i = 0; sites != null && i < sites.Count; i++)
			{
				GeothermalSite s = sites[i];
				if (s.Kind != GeothermalKind.Geyser || (eligible != null && !eligible(i)))
				{
					continue;
				}
				if (best < 0 || s.Size > sites[best].Size || (s.Size == sites[best].Size && s.Seed < sites[best].Seed))
				{
					best = i;
				}
			}
			return best;
		}

		/// <summary>
		/// The Great Spout of a scene's map, when it has one: the geyser the cut chose and stored with the map
		/// (<see cref="SceneBiomeMap.GreatSpoutBaked"/>, read with no search: what a Spout clock asks); for a map cut before the
		/// cut stored it, the largest geyser of all its sites (<see cref="GreatSpoutIndex(IReadOnlyList{GeothermalSite})"/> of
		/// <see cref="SceneSites"/>), which may be one under water.
		/// </summary>
		public static bool TryGreatSpout(SceneBiomeMap map, out GeothermalSite spout)
		{
			spout = default;
			if (map == null)
			{
				return false;
			}
			if (map.GreatSpoutBaked)
			{
				if (!map.HasGreatSpout)
				{
					return false;
				}
				spout = StoredSpout(map.GreatSpoutPosition, map.GreatSpoutSeed, map.GreatSpoutSize,
					GeothermalGround.Of(map.Sample(new Vector3(map.GreatSpoutPosition.x, 0f, map.GreatSpoutPosition.y))).Substance);
				return true;
			}
			var sites = new List<GeothermalSite>();
			SceneSites(map, sites);
			int index = GreatSpoutIndex(sites);
			spout = index >= 0 ? sites[index] : default;
			return index >= 0;
		}

		/// <summary>
		/// A geyser as the map stores its Great Spout: where, its seed and its size are all its schedule reads (<see cref="Moment"/>,
		/// <see cref="EruptionStart"/>), so the stored spout erupts exactly as the site the search finds there does.
		/// </summary>
		public static GeothermalSite StoredSpout(Vector2 position, uint seed, float size, WeatherSubstance substance = null)
		{
			return new GeothermalSite { Position = position, Kind = GeothermalKind.Geyser, Size = size, Seed = seed, Substance = substance };
		}

		/// <summary>
		/// When eruption <paramref name="slot"/> of a geyser begins, world seconds: the start <see cref="Moment"/> works out for
		/// that slot, by the same arithmetic.
		/// </summary>
		public static double EruptionStart(in GeothermalSite site, long slot)
		{
			double interval = IntervalSeconds(site);
			float mean = DurationSeconds(site);
			float steam = SteamSeconds(site);
			double phase = Unit(site.Seed * 0x9E3779B9u + 5u) * interval;
			uint slotHash = Hash(site.Seed, (int)(slot & 0x7FFFFFFF), (int)(slot >> 31));
			double slack = Math.Max(0.0, interval - mean * (1f + DurationScatter) - steam);
			return phase + slot * interval + StartScatter * slack * Unit(slotHash * 2654435761u + 9u);
		}

		/// <summary>
		/// Seconds from <paramref name="worldSeconds"/> until the geyser's next eruption begins (0 never: one playing now counts
		/// from the start of the next). Pure in the site and the clock: what a Spout clock's pointer reads.
		/// </summary>
		public static double SecondsToNextEruption(in GeothermalSite site, double worldSeconds)
		{
			GeyserMoment now = Moment(site, worldSeconds);
			if (now.Since < 0f)
			{
				return EruptionStart(site, now.Eruption) - worldSeconds;
			}
			return EruptionStart(site, now.Eruption + 1) - worldSeconds;
		}

		// ── A geyser's schedule ────────────────────────────────────────

		/// <summary>The geyser's mean interval between eruptions, s: the larger the geyser, the longer it takes to refill.</summary>
		public static float IntervalSeconds(in GeothermalSite site)
		{
			float u = Mathf.Clamp01(site.Size);
			float scatter = 0.8f + 0.4f * Unit(site.Seed * 0x2545F491u + 11u);
			return Mathf.Lerp(ShortestInterval, LongestInterval, u * Mathf.Sqrt(u)) * scatter;
		}

		/// <summary>The geyser's own eruption length, s, before each eruption's scatter.</summary>
		public static float DurationSeconds(in GeothermalSite site)
		{
			return Mathf.Lerp(ShortestEruption, LongestEruption, Mathf.Clamp01(site.Size));
		}

		/// <summary>How long the steam hangs after an eruption, s.</summary>
		public static float SteamSeconds(in GeothermalSite site)
		{
			return Mathf.Lerp(ShortestSteam, LongestSteam, Mathf.Clamp01(site.Size));
		}

		/// <summary>The column's full height on a world of this gravity, m: v²/2g for the launch speed its plumbing gives on ours.</summary>
		public static float ColumnMetres(in GeothermalSite site, float gravity)
		{
			float u = Mathf.Clamp01(site.Size);
			float earth = Mathf.Lerp(LowestColumn, HighestColumn, u * u);
			return earth * FishMMO.Shared.Celestial.SurfacePhysics.EarthGravity / Mathf.Max(0.05f, gravity);
		}

		/// <summary>
		/// How tall the column stands this far into an eruption, as a share of its full height: up in a few
		/// seconds, steady (and surging) while the plumbing empties, sinking over the last three tenths.
		/// The twin of FishVolcanicPlume.shader's.
		/// </summary>
		public static float ColumnEnvelope(float since, float duration)
		{
			if (since < 0f || since >= duration || duration <= 0f)
			{
				return 0f;
			}
			float ramp = Mathf.Min(4f, 0.1f * duration);
			float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(since / ramp));
			float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f * duration, duration, since));
			return rise * fall;
		}

		/// <summary>The geyser at a moment of world time: a pure function of the site and the clock.</summary>
		public static GeyserMoment Moment(in GeothermalSite site, double worldSeconds)
		{
			double interval = IntervalSeconds(site);
			float mean = DurationSeconds(site);
			float steam = SteamSeconds(site);
			double phase = Unit(site.Seed * 0x9E3779B9u + 5u) * interval;
			long slot = (long)Math.Floor((worldSeconds - phase) / interval);
			uint slotHash = Hash(site.Seed, (int)(slot & 0x7FFFFFFF), (int)(slot >> 31));
			float duration = mean * (1f - DurationScatter + 2f * DurationScatter * Unit(slotHash));
			// The longest eruption and its steam fit inside the slot's slack, so nothing spills into the next.
			double slack = Math.Max(0.0, interval - mean * (1f + DurationScatter) - steam);
			double start = phase + slot * interval + StartScatter * slack * Unit(slotHash * 2654435761u + 9u);
			float since = (float)(worldSeconds - start);
			var moment = new GeyserMoment { Eruption = slot, Since = since, Duration = duration };
			moment.Column = ColumnEnvelope(since, duration);
			if (since >= 0f)
			{
				moment.Steam = since < duration ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((since - duration) / steam));
			}
			return moment;
		}

		private static uint Hash(uint seed, int x, int y)
		{
			unchecked
			{
				uint h = seed ^ 0x9E3779B9u;
				h ^= (uint)x * 0x85EBCA6Bu;
				h = (h << 13) | (h >> 19);
				h ^= (uint)y * 0xC2B2AE35u;
				h *= 0x27D4EB2Fu;
				h ^= h >> 15;
				h *= 0x165667B1u;
				h ^= h >> 13;
				return h;
			}
		}

		private static float Unit(uint h)
		{
			unchecked
			{
				h ^= h >> 16;
				h *= 0x7FEB352Du;
				h ^= h >> 15;
				h *= 0x846CA68Bu;
				h ^= h >> 16;
			}
			return (h >> 8) * (1f / 16777216f);
		}
	}
}
