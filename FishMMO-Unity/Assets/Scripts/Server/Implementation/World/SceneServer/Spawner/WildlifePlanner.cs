using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Plans a world scene's wildlife spawners when it is cut: big overlapping areas over the open country, each with the
	/// species its biomes support, plus a group at each den point of interest and some cave mouths. The cut writes the plan
	/// into the scene as ObjectSpawners (WildlifeSpawnerCut), which the spawn-table bake carries to the server like any other.
	/// At spawn time the runtime keeps each species on its own ground and its herd together (SpawnerRuntime).
	/// </summary>
	/// <remarks>
	/// Never in a settlement (by habitat or by a settlement point of interest), at sea, or inside a keep-out.
	/// </remarks>
	public static class WildlifePlanner
	{
		/// <summary>One species to place: its prefab and the template the prefab carries.</summary>
		public readonly struct Species
		{
			public readonly NetworkObject Prefab;
			public readonly WildlifeTemplate Template;

			public Species(NetworkObject prefab, WildlifeTemplate template)
			{
				Prefab = prefab;
				Template = template;
			}
		}

		/// <summary>What the ground is at a point: false when unknown (off the map).</summary>
		public delegate bool GroundSampler(float x, float z, out BiomeHabitat habitat, out float altitude);

		/// <summary>
		/// The plan for one scene: big overlapping areas over the open country, each holding the species whose ground covers
		/// enough of it (weighted by how much, and by their density), then small single-species areas at dens and cave mouths.
		/// Dens come first so the cap never starves them. Pure and deterministic (seeded by the scene's name), so it is tested
		/// on its own; the cut writes it into the scene as ObjectSpawners.
		/// </summary>
		public static List<WildlifeArea> Plan(string sceneName, Rect area, GroundSampler ground,
			IReadOnlyList<PointOfInterestRecord> pois, Func<float, float, bool> keptOut,
			IReadOnlyList<SceneCaveLifeSpot> caves, IReadOnlyList<Species> species, WildlifeCatalogue limits)
		{
			var result = new List<WildlifeArea>();
			if (species == null || species.Count == 0 || ground == null || limits == null)
			{
				return result;
			}
			int max = Mathf.Max(0, limits.MaxSpawnersPerScene);
			int countMin = Mathf.Max(1, limits.SpawnCountMin);
			int countMax = Mathf.Max(countMin, limits.SpawnCountMax);
			var rng = new DeterministicRNG(StableHash(sceneName));
			List<Vector3> settlements = Settlements(pois, limits.SettlementClearance);

			if (pois != null)
			{
				for (int p = 0; p < pois.Count && result.Count < max; ++p)
				{
					PointOfInterestRecord poi = pois[p];
					for (int s = 0; s < species.Count && result.Count < max; ++s)
					{
						WildlifeTemplate t = species[s].Template;
						if (t.DenKinds != null && t.DenKinds.Contains(poi.Kind) && Inside(area, poi.Position))
						{
							float spread = Mathf.Clamp(poi.Radius, 8f, 30f) * 2f;
							result.Add(Single(species[s], poi.Position, spread, rng.Next(countMin, countMax + 1), $"{t.name} den ({poi.Kind})"));
						}
					}
				}
			}
			if (caves != null)
			{
				var seenSites = new HashSet<int>();
				for (int c = 0; c < caves.Count && result.Count < max; ++c)
				{
					SceneCaveLifeSpot spot = caves[c];
					if (spot.Kind != SceneCaveLifeKind.Mouth || !seenSites.Add(spot.SiteId))
					{
						continue;
					}
					for (int s = 0; s < species.Count && result.Count < max; ++s)
					{
						WildlifeTemplate t = species[s].Template;
						if (t.DensInCaveMouths && rng.NextFloat() < limits.CaveDenChance)
						{
							Vector3 outside = spot.Position + new Vector3(spot.Normal.x, 0f, spot.Normal.z).normalized * 6f;
							if (Inside(area, outside))
							{
								result.Add(Single(species[s], outside, 12f, rng.Next(countMin, countMax + 1), $"{t.name} cave den"));
							}
							break;
						}
					}
				}
			}

			float size = Mathf.Max(64f, limits.AreaMetres);
			float stride = Mathf.Max(32f, limits.AreaStrideMetres);
			// Each point lies in about (size / stride)^2 areas: an area's share of the ground is one stride square.
			float shareKm2 = stride * stride / 1_000_000f;
			int across = Mathf.Max(1, Mathf.CeilToInt(area.width / stride));
			int down = Mathf.Max(1, Mathf.CeilToInt(area.height / stride));
			const int Samples = 6;
			var matches = new int[species.Count];
			for (int iz = 0; iz < down && result.Count < max; ++iz)
			{
				for (int ix = 0; ix < across && result.Count < max; ++ix)
				{
					// The area's box, cut to the scene (the boundary, less its margin): nothing spawns past the edge and falls.
					float x0 = Mathf.Max(area.xMin, area.xMin + (ix + 0.5f) * stride - size * 0.5f);
					float x1 = Mathf.Min(area.xMax, area.xMin + (ix + 0.5f) * stride + size * 0.5f);
					float z0 = Mathf.Max(area.yMin, area.yMin + (iz + 0.5f) * stride - size * 0.5f);
					float z1 = Mathf.Min(area.yMax, area.yMin + (iz + 0.5f) * stride + size * 0.5f);
					if (x1 - x0 < 32f || z1 - z0 < 32f)
					{
						continue;
					}
					float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f, sizeX = x1 - x0, sizeZ = z1 - z0;
					Array.Clear(matches, 0, matches.Length);
					float low = float.PositiveInfinity, high = float.NegativeInfinity;
					for (int sz = 0; sz < Samples; ++sz)
					{
						for (int sx = 0; sx < Samples; ++sx)
						{
							float x = cx + ((sx + 0.5f) / Samples - 0.5f) * sizeX;
							float z = cz + ((sz + 0.5f) / Samples - 0.5f) * sizeZ;
							bool any = false;
							float altitude = 0f;
							for (int s = 0; s < species.Count; ++s)
							{
								if (Suitable(species[s].Template, x, z, ground, keptOut, settlements, out altitude))
								{
									matches[s]++;
									any = true;
								}
							}
							if (any)
							{
								low = Mathf.Min(low, altitude);
								high = Mathf.Max(high, altitude);
							}
						}
					}
					var weights = new List<WildlifeWeight>();
					float expected = 0f;
					for (int s = 0; s < species.Count; ++s)
					{
						float coverage = matches[s] / (float)(Samples * Samples);
						if (coverage < limits.MinimumCoverage || coverage <= 0f)
						{
							continue;
						}
						WildlifeTemplate t = species[s].Template;
						float animals = t.GroupsPerKm2 * coverage * shareKm2 * (t.GroupMin + t.GroupMax) * 0.5f;
						if (animals <= 0f)
						{
							continue;
						}
						weights.Add(new WildlifeWeight(species[s], animals));
						expected += animals;
					}
					// Any species whose ground covers enough of it gives the area a spawner; how many animals it keeps is
					// the catalogue's range, the species' shares are their weights.
					if (weights.Count == 0 || expected <= 0f)
					{
						continue;
					}
					int count = rng.Next(countMin, countMax + 1);
					result.Add(new WildlifeArea
					{
						Name = $"Wildlife Area {ix},{iz}",
						Center = new Vector3(cx, (low + high) * 0.5f, cz),
						// Tall enough for the area's whole relief, with room to cast down onto the lowest ground.
						Size = new Vector3(sizeX, Mathf.Max(20f, high - low + 30f), sizeZ),
						Species = weights,
						Count = count,
					});
				}
			}
			return result;
		}

		private static bool Inside(Rect area, Vector3 position)
		{
			return position.x >= area.xMin && position.x <= area.xMax && position.z >= area.yMin && position.z <= area.yMax;
		}

		private static WildlifeArea Single(Species species, Vector3 position, float spread, int count, string name)
		{
			return new WildlifeArea
			{
				Name = "Wildlife " + name,
				Center = position,
				Size = new Vector3(spread, 60f, spread),
				Species = new List<WildlifeWeight> { new WildlifeWeight(species, 1f) },
				Count = Mathf.Max(1, count),
				Den = true,
			};
		}

		/// <summary>
		/// Turns an area into a spawner definition (the baked shape the runtime reads): its species as weighted entries, a
		/// pack per species (the runtime keys wildlife packs by species), respawn times from each species.
		/// </summary>
		public static SpawnerDefinition ToDefinition(WildlifeArea area)
		{
			bool hunts = area.Species.Exists(w => w.Species.Template.Role == WildlifeRole.Predator);
			var definition = new SpawnerDefinition
			{
				Name = area.Name,
				Position = area.Center,
				MaxSpawnCount = area.Count,
				InitialSpawnCount = area.Count,
				SpawnType = area.Species.Count > 1 ? ObjectSpawnType.Weighted : ObjectSpawnType.Linear,
				RandomSpawnPosition = true,
				BoundingBoxSize = area.Size,
				SphereRadius = 0.5f,
				RandomRespawnTime = true,
				PrewarmHeadroom = 0,
				Pack = PackSettings(hunts),
			};
			definition.Spawnables.AddRange(Spawnables(area));
			return definition;
		}

		/// <summary>The spawner's entries for an area: one per species, its chance the species' weight.</summary>
		public static List<SpawnableSettings> Spawnables(WildlifeArea area)
		{
			var list = new List<SpawnableSettings>();
			float total = 0f;
			foreach (WildlifeWeight w in area.Species)
			{
				total += w.Weight;
			}
			foreach (WildlifeWeight w in area.Species)
			{
				WildlifeTemplate t = w.Species.Template;
				list.Add(new NPCSpawnableSettings
				{
					NetworkObject = w.Species.Prefab,
					MinimumRespawnTime = t.RespawnMinSeconds,
					MaximumRespawnTime = Mathf.Max(t.RespawnMinSeconds, t.RespawnMaxSeconds),
					SpawnChance = total > 0f ? w.Weight / total : 1f,
					PackRole = NPCGroupRole.DPS,
					MinimumScale = 0.9f,
					MaximumScale = 1.1f,
				});
			}
			return list;
		}

		/// <summary>A wildlife spawner's pack: on, so a herd flees together and a pack hunts together.</summary>
		public static NPCPackSettings PackSettings(bool hunts)
		{
			return new NPCPackSettings
			{
				Enabled = true,
				Tactic = hunts ? PackTactic.Surround : PackTactic.None,
				FocusTargeting = hunts,
			};
		}

		/// <summary>Whether a species lives on ground of <paramref name="habitat"/>: one of its habitats, and never a settlement or the sea.</summary>
		public static bool Lives(WildlifeTemplate t, BiomeHabitat habitat)
		{
			return t != null && (habitat & t.Habitats) != 0 && (habitat & (BiomeHabitat.Settlement | BiomeHabitat.Sea)) == 0;
		}

		/// <summary>Whether a point is ground the species lives on, away from towns, the sea and keep-outs.</summary>
		public static bool Suitable(WildlifeTemplate t, float x, float z, GroundSampler ground, Func<float, float, bool> keptOut,
			List<Vector3> settlements, out float altitude)
		{
			altitude = 0f;
			if (!ground(x, z, out BiomeHabitat habitat, out altitude))
			{
				return false;
			}
			if (!Lives(t, habitat))
			{
				return false;
			}
			if (keptOut != null && keptOut(x, z))
			{
				return false;
			}
			if (settlements != null)
			{
				for (int i = 0; i < settlements.Count; ++i)
				{
					Vector3 s = settlements[i];
					float dx = x - s.x, dz = z - s.z;
					if (dx * dx + dz * dz < s.y * s.y)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Settlement points of interest as (x, keep-clear radius, z).</summary>
		private static List<Vector3> Settlements(IReadOnlyList<PointOfInterestRecord> pois, float clearance)
		{
			var list = new List<Vector3>();
			if (pois == null)
			{
				return list;
			}
			for (int i = 0; i < pois.Count; ++i)
			{
				PointOfInterestRecord poi = pois[i];
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(poi.Kind);
				if (info.Group == PointOfInterestGroup.Settlement || (info.Traits & PointOfInterestTraits.Settlement) != 0)
				{
					list.Add(new Vector3(poi.Position.x, poi.Radius + clearance, poi.Position.z));
				}
			}
			return list;
		}

		/// <summary>FNV-1a of the scene name: the same placement every load, on every machine.</summary>
		public static int StableHash(string text)
		{
			unchecked
			{
				uint hash = 2166136261u;
				if (text != null)
				{
					for (int i = 0; i < text.Length; ++i)
					{
						hash = (hash ^ text[i]) * 16777619u;
					}
				}
				return (int)(hash & 0x7FFFFFFF) | 1;
			}
		}
	}

	/// <summary>One species' share of an area.</summary>
	public readonly struct WildlifeWeight
	{
		public readonly WildlifePlanner.Species Species;
		public readonly float Weight;

		public WildlifeWeight(WildlifePlanner.Species species, float weight)
		{
			Species = species;
			Weight = weight;
		}
	}

	/// <summary>One planned wildlife spawner: where, how big, which species in what share, and how many animals.</summary>
	public sealed class WildlifeArea
	{
		public string Name;
		public Vector3 Center;
		public Vector3 Size;
		public List<WildlifeWeight> Species = new List<WildlifeWeight>();
		public int Count;
		/// <summary>True for a den or a cave mouth (one species, small), false for an open-country area.</summary>
		public bool Den;
	}
}
