#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A sea hall dressed (2026-10-26, phase I2n-b, decision 83): its smokers' chimneys from the stored list (CaveSystem.Smokers), its
	/// flooded floor grown with the sea biome's own aquatic rules (the interior's Sea() rules, decision 83 Q3: "flooded cave floors grow
	/// the interior's Sea() rules"), heat-banded by the smokers' heat; and the spots the client reads the hall by: its dome (the roof the
	/// brinicles hang from, the smoke ceiling under it), its sea (a Pool spot, where its sea life swims), its smokers (the plumes) and its
	/// brine pools (the mirrors). Each on its own stream after every other pass, so nothing laid before moves; nothing for any other cave.
	/// </summary>
	public sealed partial class PointOfInterestCaveDressing
	{
		/// <summary>The shapes a smoker's chimney is drawn from: the rock and the formation shape by kind (RockTypes Sulphide, RegionalRocks Anhydrite).</summary>
		public static (string Rock, string Shape, float Height, float Width) SmokerArt(in CaveSmoker smoker)
		{
			if (smoker.Great)
			{
				return ("Sulphide", "Pillar", 30f, 4f);
			}
			switch (smoker.Kind)
			{
				case CaveSmokerKind.White:
					return ("Anhydrite", "WhiteSmoker", 5f, 1.5f);
				case CaveSmokerKind.Beehive:
					return ("Sulphide", "Beehive", 1.2f, 1f);
				default:
					return ("Sulphide", "Smoker", 9f, 2.6f);
			}
		}

		/// <summary>
		/// Whether a template's rule grows on a flooded cave floor (decision 83 Q3): a live rule with art that names the sea in its waters
		/// (what its round writes with Under(Sea(...)) / In(BiomeBedWaters.Sea, ...)).
		/// </summary>
		public static bool GrowsOnFloodedFloor(PrefabSpawnRule rule)
			=> rule != null && rule.enableSpawning && rule.prefabs != null && rule.prefabs.Length > 0 && (rule.waters & BiomeBedWaters.Sea) != 0;

		private sealed partial class Dresser
		{
			/// <summary>The most props the sea's own growth lays over a hall's floor.</summary>
			private const int MaxSeaFloor = 2400;

			private int chimneys, seaFloor, hallSpots;

			/// <summary>The sea hall's own passes (nothing for any other cave).</summary>
			private void SeaHall()
			{
				if (system.Form != CaveForm.SeaHall || !system.TryHallDome(out CaveChamber dome))
				{
					return;
				}
				Chimneys(new DeterministicRNG(unchecked(system.Seed * 0x5E47 + 0x1A2B)));
				SeaFloor(dome);
				HallSpots(dome);
				PointOfInterestFeatureKit.Note(context, $"sea hall: {system.Smokers.Count} smokers ({chimneys} chimneys laid), {system.HallPools.Count} floor basins, "
					+ $"{seaFloor} of the sea's growth on its floor, {hallSpots} spots for the client (dome, sea, smokers, brine)");
			}

			// ── The chimneys ──────────────────────────────────

			/// <summary>Each stored smoker's chimney, its foot sunk a little into the floor, scaled to its stored height and girth.</summary>
			private void Chimneys(DeterministicRNG random)
			{
				foreach (CaveSmoker smoker in system.Smokers)
				{
					// Every draw before any test.
					int variant = random.Range(0, RockTypes.VariantCount);
					float yaw = random.Range(0f, 360f), lean = random.Range(-3f, 3f);
					(string rock, string shape, float height, float width) = SmokerArt(in smoker);
					string name = RockArtNames.FormationPrefab(rock, shape, variant);
					GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
					if (prefab == null)
					{
						Missing.Add($"{rock} {shape}");
						continue;
					}
					float sy = smoker.Height / height, sxz = 2f * smoker.Radius / width;
					Vector3 foot = smoker.Base - Vector3.up * (0.15f * smoker.Radius);
					if (Place(prefab, foot, Quaternion.Euler(smoker.Great ? 0f : lean, yaw, 0f), new Vector3(sxz, sy, sxz)))
					{
						chimneys++;
					}
				}
			}

			// ── The sea's own growth on the flooded floor ─────

			/// <summary>
			/// The interior's aquatic rules over the dome's flooded floor (and its basins' rims): a lattice at each rule's spacing, each point
			/// under the water and on the floor, kept by the rule's chance times its heat band at the floor's heat (CaveSystem.HeatAt, the
			/// smokers' and the brine's), off the chimneys' feet. A linked interior's only: a host's sea rules never go under its ice.
			/// </summary>
			private void SeaFloor(in CaveChamber dome)
			{
				if (!caves.Linked)
				{
					return;
				}
				var rules = new List<PrefabSpawnRule>();
				foreach (TerrainTextureLayer layer in caves.Interior.GetAllTextureLayersInOrder())
				{
					if (layer?.prefabSpawnRules == null)
					{
						continue;
					}
					foreach (PrefabSpawnRule rule in layer.prefabSpawnRules)
					{
						if (GrowsOnFloodedFloor(rule))
						{
							rules.Add(rule);
						}
					}
				}
				if (rules.Count == 0)
				{
					return;
				}
				float water = system.WaterLevel - system.Origin.y;
				float reach = Mathf.Max(dome.Radii.x, dome.Radii.z);
				for (int r = 0; r < rules.Count && seaFloor < MaxSeaFloor; r++)
				{
					PrefabSpawnRule rule = rules[r];
					float spacing = Mathf.Clamp(rule.minSpacing, 1.5f, 8f) * 1.4f;
					float chance = Mathf.Clamp01(0.35f * rule.spawnProbabilityMultiplier);
					uint salt = unchecked((uint)system.Seed * 0x9E3779B1u ^ (uint)(r * 0x2545F491) ^ (uint)rule.seedOffset);
					int n = Mathf.CeilToInt(reach / spacing);
					for (int j = -n; j <= n && seaFloor < MaxSeaFloor; j++)
					{
						for (int i = -n; i <= n && seaFloor < MaxSeaFloor; i++)
						{
							uint h = FloatingPlants.Hash(salt, i, j, 1);
							float keep = FloatingPlants.Unit(h), jx = FloatingPlants.Unit(FloatingPlants.Hash(salt, i, j, 2)), jz = FloatingPlants.Unit(FloatingPlants.Hash(salt, i, j, 3));
							float yaw = FloatingPlants.Unit(FloatingPlants.Hash(salt, i, j, 4)) * 360f, size = FloatingPlants.Unit(FloatingPlants.Hash(salt, i, j, 5));
							int pick = (int)(FloatingPlants.Hash(salt, i, j, 6) % (uint)rule.prefabs.Length);
							if (keep >= chance)
							{
								continue;
							}
							var p = new Vector3(dome.Centre.x + (i + 0.8f * (jx - 0.5f)) * spacing, dome.Floor + 1.5f, dome.Centre.z + (j + 0.8f * (jz - 0.5f)) * spacing);
							if (!Ground(p, 8f, out float floor) || floor > water - 0.5f)
							{
								continue;
							}
							var at = new Vector3(p.x, floor, p.z);
							float heat = system.HeatAt(at);
							if (keep >= chance * rule.HeatWeight(heat) || OnChimney(at))
							{
								continue;
							}
							GameObject prefab = rule.prefabs[pick];
							Vector2 range = rule.useNonUniformScaling ? rule.heightScaleRange : rule.uniformScaleRange;
							float s = Mathf.Lerp(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y), size);
							if (Place(prefab, at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * Mathf.Max(0.05f, s)))
							{
								seaFloor++;
							}
						}
					}
				}
			}

			/// <summary>Whether a floor point stands on a chimney's foot (nothing grows inside its rock).</summary>
			private bool OnChimney(Vector3 at)
			{
				foreach (CaveSmoker s in system.Smokers)
				{
					if (new Vector2(at.x - s.Base.x, at.z - s.Base.z).magnitude < s.Radius + 0.3f)
					{
						return true;
					}
				}
				return false;
			}

			// ── What the client reads the hall by ─────────────

			/// <summary>
			/// The hall's spots, after the cave life's own: its dome, its sea (a Pool spot over the dome's middle, as wide as its waterline
			/// and as deep as its water, carrying the interior for the sea life), each smoker (its orifice, the clear height over it, its
			/// kind in the flags), each brine pool's mirror. World metres; the interior's id on each, as on every linked cave's spot.
			/// </summary>
			private void HallSpots(in CaveChamber dome)
			{
				if (context.Points == null)
				{
					return;
				}
				int inside = caves.Linked ? BiomeRegistry.IDOf(caves.Interior) : 0;
				List<SceneCaveLifeSpot> spots = HallSpotsOf(system, context.Record.Id, inside);
				context.Points.CaveLife ??= new List<SceneCaveLifeSpot>();
				context.Points.CaveLife.AddRange(spots);
				hallSpots = spots.Count;
				LifeSpots += spots.Count;
			}
		}

		/// <summary>
		/// A sea hall's own spots (pure; world metres): its dome, its sea, its smokers and its brine pools, in that order (see
		/// SceneCaveLifeKind.Hall, Pool, Smoker, Brine). Empty for any other cave.
		/// </summary>
		public static List<SceneCaveLifeSpot> HallSpotsOf(CaveSystem system, int siteId, int interiorId)
		{
			var spots = new List<SceneCaveLifeSpot>();
			if (system == null || !system.TryHallDome(out CaveChamber dome))
			{
				return spots;
			}
			float water = system.WaterLevel - system.Origin.y;
			void Add(SceneCaveLifeSpot spot)
			{
				spot.Form = SceneCaveForm.SeaHall;
				spot.SiteId = siteId;
				spot.Biome = interiorId;
				spot.Dark = 1f;
				spot.Position += system.Origin;
				spots.Add(spot);
			}
			Add(new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Hall, Position = dome.Centre, Normal = Vector3.up, Extent = dome.Radii, Yaw = dome.Yaw });
			float h = (water - dome.Centre.y) / dome.Radii.y;
			float waterline = Mathf.Abs(h) < 1f ? Mathf.Sqrt(1f - h * h) * Mathf.Min(dome.Radii.x, dome.Radii.z) : 0f;
			Add(new SceneCaveLifeSpot
			{
				Kind = SceneCaveLifeKind.Pool, Flags = SceneCaveLifeFlags.Damp | SceneCaveLifeFlags.Salt,
				Position = new Vector3(dome.Centre.x, water, dome.Centre.z), Normal = Vector3.up, Extent = new Vector3(waterline, water - dome.Floor, 0f),
			});
			for (int i = 0; i < system.Smokers.Count; i++)
			{
				CaveSmoker s = system.Smokers[i];
				Vector3 orifice = s.Orifice;
				float roof = RoofOver(dome, orifice);
				SceneCaveLifeFlags flags = SceneCaveLifeFlags.Salt | (s.Great ? SceneCaveLifeFlags.Great : 0)
					| (s.Kind == CaveSmokerKind.White ? SceneCaveLifeFlags.White : s.Kind == CaveSmokerKind.Beehive ? SceneCaveLifeFlags.Beehive : 0);
				Add(new SceneCaveLifeSpot
				{
					Kind = SceneCaveLifeKind.Smoker, Flags = flags, Position = orifice, Normal = Vector3.up,
					Extent = new Vector3(Mathf.Max(0.15f, 0.35f * s.Radius), s.Height, Mathf.Max(0f, roof - orifice.y)),
					// Its own turn: where its flanges face (a hash of its place, the same for everyone).
					Yaw = Mathf.Repeat(orifice.x * 37.1f + orifice.z * 11.3f + i * 97f, 360f),
				});
			}
			foreach (CaveHallPool pool in system.HallPools)
			{
				if (pool.Kind != CaveHallPoolKind.Brine)
				{
					continue;
				}
				Add(new SceneCaveLifeSpot
				{
					Kind = SceneCaveLifeKind.Brine, Flags = SceneCaveLifeFlags.Salt, Position = new Vector3(pool.Rim.x, pool.Mirror, pool.Rim.z), Normal = Vector3.up,
					Extent = new Vector3(pool.Radius, pool.Rim.y - pool.Mirror, 0f),
				});
			}
			return spots;
		}

		/// <summary>The dome's roof over a point (system frame): its ellipsoid's top over the point's plan, the floor where it is outside.</summary>
		public static float RoofOver(in CaveChamber dome, Vector3 p)
		{
			float r = dome.Yaw * Mathf.Deg2Rad;
			var along = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			var side = new Vector3(along.z, 0f, -along.x);
			var o = new Vector3(p.x - dome.Centre.x, 0f, p.z - dome.Centre.z);
			float u = Vector3.Dot(o, along) / dome.Radii.x, v = Vector3.Dot(o, side) / dome.Radii.z;
			float q = 1f - u * u - v * v;
			return q <= 0f ? dome.Floor : dome.Centre.y + dome.Radii.y * Mathf.Sqrt(q);
		}
	}
}
#endif
