using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.Weather
{
	/// <summary>A vent: a point on the ground that throws something up.</summary>
	public struct VentSite
	{
		/// <summary>World XZ.</summary>
		public Vector2 Position;
		/// <summary>What it throws up; null for a lava lake's own spatter.</summary>
		public WeatherSubstance Substance;
		/// <summary>How hard, 0..1: the biome's emission rate.</summary>
		public float Emission;
		/// <summary>A stable number for its look.</summary>
		public uint Seed;
		/// <summary>How its strength moves on the shared clock (its biome's <see cref="BiomeVents.Timing"/>, 2026-10-25, I2q-b; presentation only).</summary>
		public BiomeVentTiming Timing;
		/// <summary>The seed its timing draws from: its own, but the scene's first Strombolian vent takes the scene's (VentBursts.SceneSeed), which <c>_FishVentBurst</c> follows.</summary>
		public uint TimingSeed;
		/// <summary>Its crack's bearing, radians in [0, π) (its line's, or the kneading glow's field off any line).</summary>
		public float Bearing;
		/// <summary>The fountain episodes an Episode timing follows (its biome's).</summary>
		public BiomeFountainEpisodes Episodes;
	}

	/// <summary>
	/// Where a scene's vents are, and the plumes standing over them now.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A biome that emits a <see cref="WeatherSubstance.Vented"/> substance does not emit it from
	/// every square metre: it has vents. They are placed on a jittered grid of
	/// <see cref="CellMetres"/> cells over the scene's biome map — one candidate point per cell, kept
	/// where the biome there emits a vented substance, with a chance that grows with how hard it
	/// emits. Seeded from the map's name, so the server's weather and every client's drawing agree
	/// on where they are without a word on the wire.
	/// </para>
	/// <para>
	/// An eruption cell is a vent going off: a plume of its own at the cell's centre, far stronger
	/// than the steady ones (<see cref="StormPhysics.EmissionBoost"/>).
	/// </para>
	/// </remarks>
	public static class VolcanicVents
	{
		/// <summary>One candidate vent per cell this wide, m.</summary>
		public const float CellMetres = 1000f;
		/// <summary>The most vents a scene keeps, the first in grid order.</summary>
		public const int MaxVents = 8;
		/// <summary>A vented biome's chance of a vent in a cell, per unit of emission rate.</summary>
		public const float VentsPerEmission = 4f;
		/// <summary>Plumes further than this from the point asked about are skipped, m.</summary>
		public const float ReachMetres = 60000f;

		private static readonly Dictionary<SceneBiomeMap, List<VentSite>> cache = new Dictionary<SceneBiomeMap, List<VentSite>>();
		private static readonly List<VentSite> none = new List<VentSite>();

		/// <summary>Whether a biome's emission comes out of vents.</summary>
		public static bool IsVented(BiomeTemplate biome)
		{
			return biome != null && biome.Emits != null && biome.Emits.Vented && biome.EmissionRate > 0f;
		}

		/// <summary>
		/// The vents in an area: one jittered candidate per cell, kept where <paramref name="biomeAt"/>
		/// says a vented biome is, with a chance of <see cref="VentsPerEmission"/> × its rate. Pure:
		/// the same area, seed and biomes give the same vents.
		/// </summary>
		public static void Find(Rect area, uint seed, Func<Vector2, BiomeTemplate> biomeAt, List<VentSite> into, float cellMetres = CellMetres)
		{
			Find(area, seed, biomeAt, null, into, cellMetres);
		}

		/// <summary>The spacing of a plume along a stored fissure line, and along a ridge line, m (2026-10-25, I2q-b).</summary>
		public const float FissureSpacing = 800f, RidgeSpacing = 1200f;

		/// <summary>
		/// <see cref="Find(Rect, uint, Func{Vector2, BiomeTemplate}, List{VentSite}, float)"/> with the scene's stored vent lines
		/// (2026-10-25, phase I2q-b, decisions 53, 90, 95): a biome laid out on lines (<see cref="BiomeVents.Layout"/>) whose lines the
		/// scene stores has no grid vent; its vents stand on its lines instead (a Fissures line one every <see cref="FissureSpacing"/> m,
		/// a Ridges line every <see cref="RidgeSpacing"/> m, a Terrace line at its middle and on each pad), after the grid's, every vent
		/// counted against <see cref="MaxVents"/> and its biome's own cap (<see cref="BiomeVents.MaxPlumes"/>). Null lines: the grid
		/// alone, as before. Pure.
		/// </summary>
		public static void Find(Rect area, uint seed, Func<Vector2, BiomeTemplate> biomeAt, IReadOnlyList<VentLine> lines, List<VentSite> into, float cellMetres = CellMetres)
		{
			into.Clear();
			if (biomeAt == null || area.width <= 0f || area.height <= 0f)
			{
				return;
			}
			Dictionary<int, int> perBiome = null;
			bool Capped(BiomeTemplate biome)
			{
				if (biome.Vents.MaxPlumes <= 0)
				{
					return false;
				}
				perBiome ??= new Dictionary<int, int>();
				perBiome.TryGetValue(biome.ID, out int n);
				if (n >= biome.Vents.MaxPlumes)
				{
					return true;
				}
				perBiome[biome.ID] = n + 1;
				return false;
			}
			float cell = Mathf.Max(10f, cellMetres);
			int nx = Mathf.Max(1, Mathf.CeilToInt(area.width / cell));
			int nz = Mathf.Max(1, Mathf.CeilToInt(area.height / cell));
			for (int j = 0; j < nz && into.Count < MaxVents; j++)
			{
				for (int i = 0; i < nx && into.Count < MaxVents; i++)
				{
					uint h = Hash(seed, i, j);
					// Inside the cell's middle four fifths, so two cells' vents never stand together.
					float x = area.xMin + (i + 0.1f + 0.8f * Unit(h)) * cell;
					float z = area.yMin + (j + 0.1f + 0.8f * Unit(h * 747796405u + 1u)) * cell;
					if (x > area.xMax || z > area.yMax)
					{
						continue;
					}
					var at = new Vector2(x, z);
					BiomeTemplate biome = biomeAt(at);
					if (!IsVented(biome))
					{
						continue;
					}
					// A biome laid out on the scene's stored lines has its vents there (2026-10-25, I2q-b).
					if (lines != null && biome.Vents.Layout != BiomeVentLayout.Grid && IceStripes.HasBiome(lines, biome.ID))
					{
						continue;
					}
					if (Unit(h * 2891336453u + 7u) >= Mathf.Clamp01(VentsPerEmission * biome.EmissionRate) || Capped(biome))
					{
						continue;
					}
					into.Add(Vent(at, biome, h, VentTiming.FieldBearing(at.x, at.y)));
				}
			}
			for (int i = 0; lines != null && i < lines.Count && into.Count < MaxVents; i++)
			{
				VentLine line = lines[i];
				uint lineSeed = seed ^ unchecked(line.Seed * 0x165667B1u);
				if (line.IsPad || line.Layout == BiomeVentLayout.Terrace)
				{
					// A terrace's plume at its line's middle, or a pad's centre.
					float mid = 0.5f * line.LineLength;
					if (!line.IsPad && line.Layout == BiomeVentLayout.Terrace && (mid < line.AlongA || mid >= line.AlongB))
					{
						continue;
					}
					if (line.Layout != BiomeVentLayout.Terrace)
					{
						continue;
					}
					float t = line.IsPad ? 0f : (mid - line.AlongA) / Mathf.Max(1e-4f, line.AlongB - line.AlongA);
					AddOnLine(Vector2.Lerp(line.A, line.B, t), line.Bearing, Hash(lineSeed, 0, 0x7E40), area, biomeAt, lines, into, Capped);
					continue;
				}
				float spacing = line.Layout == BiomeVentLayout.Ridges ? RidgeSpacing : FissureSpacing;
				long k0 = (long)Math.Floor(line.AlongA / spacing) - 1, k1 = (long)Math.Floor(line.AlongB / spacing);
				for (long k = k0; k <= k1 && into.Count < MaxVents; k++)
				{
					uint h = Hash(lineSeed, (int)(k & 0x7FFFFFFF), 0x1F55);
					float s = (k + 0.5f + 0.4f * (Unit(h) - 0.5f)) * spacing;
					if (s < line.AlongA || s >= line.AlongB)
					{
						continue;
					}
					float t = (s - line.AlongA) / Mathf.Max(1e-4f, line.AlongB - line.AlongA);
					AddOnLine(Vector2.Lerp(line.A, line.B, t), line.Bearing, h, area, biomeAt, lines, into, Capped);
				}
			}
		}

		private static void AddOnLine(Vector2 at, float bearing, uint h, Rect area, Func<Vector2, BiomeTemplate> biomeAt, IReadOnlyList<VentLine> lines, List<VentSite> into,
			Func<BiomeTemplate, bool> capped)
		{
			if (at.x < area.xMin || at.x > area.xMax || at.y < area.yMin || at.y > area.yMax)
			{
				return;
			}
			BiomeTemplate biome = biomeAt(at);
			if (!IsVented(biome) || biome.Vents.Layout == BiomeVentLayout.Grid || !IceStripes.HasBiome(lines, biome.ID) || capped(biome))
			{
				return;
			}
			into.Add(Vent(at, biome, h, bearing));
		}

		private static VentSite Vent(Vector2 at, BiomeTemplate biome, uint seed, float bearing)
		{
			return new VentSite
			{
				Position = at,
				Substance = biome.Emits,
				Emission = Mathf.Clamp01(biome.EmissionRate),
				Seed = seed,
				Timing = biome.Vents.Timing,
				TimingSeed = seed,
				Bearing = bearing,
				Episodes = biome.FountainEpisodes,
			};
		}

		/// <summary>
		/// Gives the first Strombolian vent of a scene's list the scene's own burst seed (VentBursts.SceneSeed), so the vent and the
		/// glows that follow <c>_FishVentBurst</c> burst as one (2026-10-25, I2q-b; decision 95). Pure.
		/// </summary>
		public static void SeedSceneBurst(List<VentSite> vents, string sceneName)
		{
			for (int i = 0; vents != null && i < vents.Count; i++)
			{
				if (vents[i].Timing == BiomeVentTiming.Strombolian)
				{
					VentSite v = vents[i];
					v.TimingSeed = VentBursts.SceneSeed(sceneName);
					vents[i] = v;
					return;
				}
			}
		}

		/// <summary>A scene's vents, from its biome map: found once per map and kept. Empty with no map.</summary>
		public static List<VentSite> Of(WorldSceneSettings settings)
		{
			SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
			if (map == null || map.WorldSize.x <= 0f || map.WorldSize.y <= 0f)
			{
				return none;
			}
			if (cache.TryGetValue(map, out List<VentSite> known))
			{
				return known;
			}
			var vents = new List<VentSite>();
			uint seed = unchecked((uint)map.name.GetDeterministicHashCode());
			bool resolved = false;
			Find(new Rect(map.WorldOrigin, map.WorldSize), seed, p =>
			{
				BiomeTemplate biome = map.Sample(new Vector3(p.x, 0f, p.y));
				resolved |= biome != null;
				return biome;
			}, IceStripes.Of(map), vents);
			SeedSceneBurst(vents, settings.gameObject.scene.name);
			// Kept only once the map's biomes could be read: asked before they had loaded, every
			// candidate reads as no biome, and keeping that would leave the scene without vents for good.
			if (resolved)
			{
				cache[map] = vents;
			}
			return vents;
		}

		/// <summary>World seconds between readings of a plume's own wind.</summary>
		public const double WindSeconds = 5.0;

		[ThreadStatic] private static Dictionary<Vector2, Vector2> windByVent;
		[ThreadStatic] private static double windAt;
		[ThreadStatic] private static WeatherTimeline windTimeline;
		[ThreadStatic] private static WorldSceneSettings windSettings;

		/// <summary>The open air's wind at a vent, read at the whole <see cref="WindSeconds"/> of world time at or before the moment.</summary>
		private static Vector2 WindAt(WeatherTimeline timeline, WorldSceneSettings settings, Vector2 vent, double worldSeconds)
		{
			double at = Math.Floor(worldSeconds / WindSeconds) * WindSeconds;
			windByVent ??= new Dictionary<Vector2, Vector2>();
			if (at != windAt || !ReferenceEquals(timeline, windTimeline) || settings != windSettings || windByVent.Count > 256)
			{
				windByVent.Clear();
				windAt = at;
				windTimeline = timeline;
				windSettings = settings;
			}
			if (!windByVent.TryGetValue(vent, out Vector2 wind))
			{
				wind = WeatherField.OpenWindAtSeconds(timeline, settings, vent, at);
				windByVent[vent] = wind;
			}
			return wind;
		}

		/// <summary>Forgets every scene's vents: for tools that repaint a biome map while the game runs.</summary>
		public static void ClearCache() => cache.Clear();

		/// <summary>
		/// The plumes standing in a scene now: its vents' steady ones and every eruption cell's, in the
		/// air of the place asked about. None without air.
		/// </summary>
		/// <param name="surfaceWind">The open air's wind near the ground, m/s (no storm in it).</param>
		/// <param name="near">The point the weather is for: plumes beyond <see cref="ReachMetres"/> are skipped.</param>
		public static void Plumes(WeatherTimeline timeline, WorldSceneSettings settings, uint tick, in PlanetAir planet, Vector2 surfaceWind,
			Vector2 near, List<VolcanicPlume.Plume> into)
		{
			Plumes(timeline, settings, timeline != null ? timeline.WorldSecondsAt(tick) : 0.0, planet, surfaceWind, false, near, into);
		}

		/// <summary>
		/// The same, each plume leaning on the wind where it stands (<see cref="WeatherField.OpenWindAtSeconds"/>)
		/// rather than on the wind of the place asked about: a plume is one thing, wherever it is looked at
		/// from. The weather (its ash) and the sky (the plume it draws) both ask this way, so the plume is
		/// the plume the ash falls from, and the same for every player and the server. It used to lean on
		/// the asker's wind: each point's ash came out of a plume leaning its own way, and each player saw
		/// their own plume.
		/// </summary>
		/// <remarks>
		/// The wind is read at whole <see cref="WindSeconds"/> of world time, and kept per vent until the next:
		/// the weather is sampled often, a wind costs an open-air reading, and the air turns over hours.
		/// </remarks>
		public static void PlumesInTheirOwnWind(WeatherTimeline timeline, WorldSceneSettings settings, uint tick, in PlanetAir planet,
			Vector2 near, List<VolcanicPlume.Plume> into)
		{
			Plumes(timeline, settings, timeline != null ? timeline.WorldSecondsAt(tick) : 0.0, planet, Vector2.zero, true, near, into);
		}

		/// <summary>The same, at a moment of world time.</summary>
		public static void PlumesInTheirOwnWindAtSeconds(WeatherTimeline timeline, WorldSceneSettings settings, double worldSeconds, in PlanetAir planet,
			Vector2 near, List<VolcanicPlume.Plume> into)
		{
			Plumes(timeline, settings, worldSeconds, planet, Vector2.zero, true, near, into);
		}

		private static void Plumes(WeatherTimeline timeline, WorldSceneSettings settings, double worldSeconds, in PlanetAir planet, Vector2 surfaceWind,
			bool ownWind, Vector2 near, List<VolcanicPlume.Plume> into)
		{
			into.Clear();
			if (!VolcanicPlume.CanRise(planet))
			{
				return;
			}
			List<VentSite> vents = Of(settings);
			for (int i = 0; i < vents.Count; i++)
			{
				VentSite v = vents[i];
				if ((v.Position - near).sqrMagnitude > ReachMetres * ReachMetres)
				{
					continue;
				}
				Vector2 wind = ownWind ? WindAt(timeline, settings, v.Position, worldSeconds) : surfaceWind;
				VolcanicPlume.Plume plume = VolcanicPlume.Of(v.Position, v.Emission, v.Substance, planet, wind, false, v.Seed);
				if (plume.Valid)
				{
					into.Add(plume);
				}
			}
			if (timeline == null)
			{
				return;
			}
			SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				if (cell.Kind != StormKind.Eruption)
				{
					continue;
				}
				float strength = cell.EnvelopeAtSeconds(worldSeconds) * Mathf.Clamp01(cell.PeakIntensity);
				if (strength <= 0f)
				{
					continue;
				}
				Vector2 centre = cell.CentreAtSeconds(worldSeconds);
				if ((centre - near).sqrMagnitude > ReachMetres * ReachMetres)
				{
					continue;
				}
				// What the ground there emits, at the eruption's strength. An eruption asked for over
				// ground that emits nothing still throws up ash: its substance is null, which falls as
				// plain ash (WeatherPhysics.FallingFrom).
				BiomeTemplate biome = map != null ? map.Sample(new Vector3(centre.x, 0f, centre.y)) : null;
				// Steam is not what an eruption throws up: one forced over a geyser basin throws plain ash.
				WeatherSubstance substance = biome != null && biome.Emits != null && !biome.Emits.Vapour ? biome.Emits : null;
				float rate = substance != null ? Mathf.Max(0.05f, biome.EmissionRate) : 0.05f;
				float emission = Mathf.Clamp01(rate * StormPhysics.EmissionBoost(StormKind.Eruption, strength));
				Vector2 wind = ownWind ? WindAt(timeline, settings, centre, worldSeconds) : surfaceWind;
				VolcanicPlume.Plume plume = VolcanicPlume.Of(centre, emission, substance, planet, wind, true, cell.Seed);
				if (plume.Valid)
				{
					into.Add(plume);
				}
			}
		}

		/// <summary>
		/// How hard the plumes' ash falls at a point, 0..1, and the substance of the heaviest: the
		/// plumes overlap as independent falls do, 1 − Π(1 − a).
		/// </summary>
		/// <param name="cells">False for the background: only the vents' steady plumes, no eruption cell's.</param>
		public static float FalloutAt(List<VolcanicPlume.Plume> plumes, Vector2 position, bool cells, out WeatherSubstance substance)
		{
			substance = null;
			float clear = 1f, heaviest = 0f;
			for (int i = 0; i < plumes.Count; i++)
			{
				if (!cells && plumes[i].FromCell)
				{
					continue;
				}
				float a = Mathf.Clamp01(VolcanicPlume.FalloutAt(plumes[i], position));
				clear *= 1f - a;
				if (a > heaviest)
				{
					heaviest = a;
					substance = plumes[i].Substance;
				}
			}
			return 1f - clear;
		}

		/// <summary>
		/// The lava lakes in an area, as vents: in each <see cref="CellMetres"/> cell, the deepest of a
		/// grid of points whose ground lies below the lava's level — a lake's middle, where it is
		/// deepest — at most <see cref="MaxVents"/>. Pure: a height function in, points out.
		/// </summary>
		/// <param name="groundAt">The ground's height at a point, or NaN where there is none.</param>
		public static void LavaLakes(Rect area, float level, Func<Vector2, float> groundAt, List<Vector2> into, int samplesPerCell = 12, float cellMetres = CellMetres)
		{
			into.Clear();
			if (groundAt == null || area.width <= 0f || area.height <= 0f)
			{
				return;
			}
			float cell = Mathf.Max(10f, cellMetres);
			int n = Mathf.Max(1, samplesPerCell);
			int nx = Mathf.Max(1, Mathf.CeilToInt(area.width / cell));
			int nz = Mathf.Max(1, Mathf.CeilToInt(area.height / cell));
			for (int j = 0; j < nz && into.Count < MaxVents; j++)
			{
				for (int i = 0; i < nx && into.Count < MaxVents; i++)
				{
					float deepest = 0.5f;
					Vector2 best = default;
					bool found = false;
					for (int b = 0; b < n; b++)
					{
						for (int a = 0; a < n; a++)
						{
							float x = area.xMin + (i + (a + 0.5f) / n) * cell;
							float z = area.yMin + (j + (b + 0.5f) / n) * cell;
							if (x > area.xMax || z > area.yMax)
							{
								continue;
							}
							float depth = level - groundAt(new Vector2(x, z));
							if (depth > deepest)
							{
								deepest = depth;
								best = new Vector2(x, z);
								found = true;
							}
						}
					}
					if (found)
					{
						into.Add(best);
					}
				}
			}
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
