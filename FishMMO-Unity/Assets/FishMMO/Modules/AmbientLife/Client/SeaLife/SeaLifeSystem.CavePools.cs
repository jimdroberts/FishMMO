using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Client
{
	public static partial class SeaLifeSystem
	{
		/*
		 * The seas in the caves (2026-10-26, phase I2n-b, decision 83 Q3: "sea life swims in cave water via cave-life spots carrying the
		 * interior id"): a sea hall's warm sea under an ice shell, on a world whose own seas are frozen through (no sea surface at all), is
		 * its interior biome's own sea, and that biome's Sea Life swims in it, placed and moved exactly as in the open sea
		 * (SeaLifePlacement.TryPlace / Evaluate) over the hall's pool as its sea floor (CavePoolGround). Only the kinds the interior lists
		 * live there (nothing drifts in from elsewhere: no sea joins it), seeded by the pool's own place (every client loads the same spot),
		 * so every player sees the same eelpout in the same cleft. Nothing where no cave-life spot holds such a pool (every scene until a
		 * frozen-through cryovolcanic world's sea halls are cut): one version check a frame.
		 */

		/// <summary>The least radius a cave pool spot must have for its own sea life (a sea hall's sea, not a puddle), metres.</summary>
		public const float CavePoolLeast = 8f;

		/// <summary>One cave sea: its pool, the interior biome whose sea life lives in it, its placed cells.</summary>
		private sealed class CavePool
		{
			public Vector3 Surface;
			public float Radius, Depth;
			public BiomeTemplate Biome;
			public uint Seed;
			public bool Placed;
			public CavePoolGround Ground;
			public readonly List<Cell> Cells = new List<Cell>();
		}

		private static readonly List<CavePool> cavePools = new List<CavePool>();
		private static readonly List<CavePool> nearPools = new List<CavePool>();
		private static int poolsGathered = -1;

		private static void ReleasePools()
		{
			cavePools.Clear();
			nearPools.Clear();
			poolsGathered = -1;
		}

		/// <summary>Whether a cave-life spot is a cave sea of its own: a sea hall's Pool spot, wide enough, carrying its interior biome.</summary>
		public static bool IsCaveSea(in SceneCaveLifeSpot spot)
			=> spot.Kind == SceneCaveLifeKind.Pool && spot.Form == SceneCaveForm.SeaHall && spot.Biome != 0 && spot.Extent.x >= CavePoolLeast && spot.Extent.y > 1f;

		/// <summary>The cave seas within reach of the eye placed (all their cells at once: a hall is a few hundred metres); true when any is near.</summary>
		private static bool PlacePools(SeaLifeSettings settings, Vector3 eye)
		{
			if (poolsGathered != SceneCaveLife.Version)
			{
				poolsGathered = SceneCaveLife.Version;
				cavePools.Clear();
				foreach (SceneCaveLife.Set set in SceneCaveLife.Sets)
				{
					IReadOnlyList<SceneCaveLifeSpot> spots = set.Spots;
					for (int i = 0; spots != null && i < spots.Count; i++)
					{
						SceneCaveLifeSpot spot = spots[i];
						if (!IsCaveSea(in spot) || !BiomeRegistry.TryGetByID(spot.Biome, out BiomeTemplate biome) || biome == null || biome.SeaLife == null || biome.SeaLife.Count == 0)
						{
							continue;
						}
						var pool = new CavePool { Surface = spot.Position, Radius = spot.Extent.x, Depth = spot.Extent.y, Biome = biome, Seed = SceneCaveWater.SeedOf(spot.Position) };
						pool.Ground = new CavePoolGround(pool.Surface, pool.Radius, pool.Depth, biome);
						cavePools.Add(pool);
					}
				}
			}
			nearPools.Clear();
			if (cavePools.Count == 0 || settings.Kinds == null)
			{
				return false;
			}
			float cellMetres = Mathf.Max(16f, settings.CellMetres);
			foreach (CavePool pool in cavePools)
			{
				float reach = settings.DrawDistance + pool.Radius;
				if ((new Vector3(eye.x, pool.Surface.y, eye.z) - pool.Surface).sqrMagnitude > reach * reach || Mathf.Abs(eye.y - pool.Surface.y) > pool.Depth + settings.MaxCameraHeight)
				{
					continue;
				}
				if (!pool.Placed)
				{
					pool.Placed = true;
					pool.Cells.Clear();
					int i0 = Mathf.FloorToInt((pool.Surface.x - pool.Radius) / cellMetres), i1 = Mathf.FloorToInt((pool.Surface.x + pool.Radius) / cellMetres);
					int k0 = Mathf.FloorToInt((pool.Surface.z - pool.Radius) / cellMetres), k1 = Mathf.FloorToInt((pool.Surface.z + pool.Radius) / cellMetres);
					for (int k = k0; k <= k1; k++)
					{
						for (int i = i0; i <= i1; i++)
						{
							var cell = new Cell();
							for (int kind = 0; kind < settings.Kinds.Length; kind++)
							{
								// Only what the interior lists lives in its sea: no kind of elsewhere reaches a sea sealed under the ice.
								if (pool.Ground.HeaviestHome(settings.Kinds[kind]) > 0f)
								{
									SeaLifePlacement.TryPlace(settings.Kinds[kind], kind, pool.Seed, i, k, cellMetres, pool.Surface.y, pool.Ground, cell.Groups);
								}
							}
							if (cell.Groups.Count > 0)
							{
								pool.Cells.Add(cell);
							}
						}
					}
				}
				nearPools.Add(pool);
			}
			return nearPools.Count > 0;
		}

		/// <summary>Collects every near cave sea's animals with the open sea's collector (its pool as both surface and level).</summary>
		private static void CollectPools(Action<IEnumerable<Cell>, ISeaGround, float, float> collect)
		{
			foreach (CavePool pool in nearPools)
			{
				if (pool.Cells.Count > 0)
				{
					collect(pool.Cells, pool.Ground, pool.Surface.y, pool.Surface.y);
				}
			}
		}

		/// <summary>
		/// A cave sea as the sea life's floor (pure, for tests): flat at its depth under its surface within its pool's radius (a little in
		/// from the waterline, so nothing swims into the wall), no water beyond; every kind its interior lists at home there, at its weight,
		/// in warm water; none else.
		/// </summary>
		public sealed class CavePoolGround : ISeaGround
		{
			private readonly Vector3 surface;
			private readonly float radius, depth;
			private readonly BiomeTemplate biome;

			/// <summary>The share of the pool's radius its sea life keeps within.</summary>
			public const float Within = 0.85f;
			/// <summary>The water's temperature on the placement's scale (a vent's warm sea, ~10 °C).</summary>
			public const float Warm = 0.3f;

			public CavePoolGround(Vector3 surface, float radius, float depth, BiomeTemplate biome)
			{
				this.surface = surface;
				this.radius = radius;
				this.depth = depth;
				this.biome = biome;
			}

			public bool TryGround(float x, float z, out float y)
			{
				float dx = x - surface.x, dz = z - surface.z;
				y = dx * dx + dz * dz <= radius * Within * radius * Within ? surface.y - depth : float.PositiveInfinity;
				return true;
			}

			public void Habitat(Vector3 position, SeaCreatureKind kind, out bool home, out float homeWeight, out float temperature)
			{
				temperature = Warm;
				homeWeight = 0f;
				home = biome != null && SeaLifePlacement.TryHomeWeight(kind, biome.SeaLife, out homeWeight);
			}

			public float HeaviestHome(SeaCreatureKind kind)
				=> biome != null && SeaLifePlacement.TryHomeWeight(kind, biome.SeaLife, out float weight) ? Mathf.Max(0f, weight) : 0f;
		}
	}
}
