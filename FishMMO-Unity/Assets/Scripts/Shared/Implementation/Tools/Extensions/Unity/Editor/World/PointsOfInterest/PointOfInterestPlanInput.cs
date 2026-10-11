#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One biome present in a scene, as the planner knows it: plain strings and the registry ID.</summary>
	public sealed class PointOfInterestBiome
	{
		/// <summary>The biome asset's name, what catalogue rows match ("Pine Forest").</summary>
		public string Name;
		/// <summary>The <see cref="Biomes.BiomeRegistry"/> cached ID, written to records; 0 for none.</summary>
		public int Id;
		public string DisplayName;

		public PointOfInterestBiome(string name, int id, string displayName = null)
		{
			Name = name;
			Id = id;
			DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
		}
	}

	/// <summary>One lake as the planner marks it.</summary>
	public sealed class PointOfInterestLake
	{
		public int Id;
		public float Level;
		/// <summary>The centroid of what it covers, scene metres.</summary>
		public float X;
		public float Z;
		public float AreaM2;
	}

	/// <summary>
	/// The scene's inland water as the planner asks it: plain functions and lists, so a test can describe a river
	/// without building a <see cref="SceneWater"/>.
	/// </summary>
	public sealed class PointOfInterestWater
	{
		/// <summary>What stands at (east, north); null reads as dry ground everywhere.</summary>
		public Func<float, float, WaterKind> KindAt;
		/// <summary>Metres from (east, north) to the nearest river or lake (out to a few hundred metres); null derives it from <see cref="KindAt"/>.</summary>
		public Func<float, float, float> DistanceAt;
		public IReadOnlyList<RiverPath> Rivers = Array.Empty<RiverPath>();
		public IReadOnlyList<PointOfInterestLake> Lakes = Array.Empty<PointOfInterestLake>();

		/// <summary>The planner's view of a scene's laid water; null for none.</summary>
		public static PointOfInterestWater From(SceneWater water)
		{
			if (water == null || !water.Any)
			{
				return null;
			}
			var lakes = new List<PointOfInterestLake>();
			SceneWaterGrid grid = water.Grid;
			foreach (SceneLake lake in water.Lakes)
			{
				double sx = 0.0, sz = 0.0;
				int count = 0;
				if (grid != null)
				{
					for (int z = 0; z < grid.Depth; z++)
					{
						for (int x = 0; x < grid.Width; x++)
						{
							int i = z * grid.Width + x;
							if (grid.Kind[i] == WaterKind.Lake && grid.Body[i] == lake.Id)
							{
								sx += grid.EastOf(x);
								sz += grid.NorthOf(z);
								count++;
							}
						}
					}
				}
				float area = grid != null ? count * grid.Spacing * grid.Spacing : 0f;
				if (count == 0)
				{
					// Not marked (no grid): the seeds it floods from.
					foreach (Vector2 seed in lake.Seeds)
					{
						sx += seed.x;
						sz += seed.y;
						count++;
					}
					area = lake.Bounds.width * lake.Bounds.height * 0.5f;
				}
				if (count == 0)
				{
					continue;
				}
				lakes.Add(new PointOfInterestLake { Id = lake.Id, Level = lake.Level, X = (float)(sx / count), Z = (float)(sz / count), AreaM2 = area });
			}
			return new PointOfInterestWater
			{
				KindAt = water.KindAt,
				DistanceAt = water.DistanceAt,
				Rivers = water.Rivers,
				Lakes = lakes,
			};
		}
	}

	/// <summary>
	/// A scene's point-of-interest choices as the planner reads them: plain values, so tests need no asset
	/// (<see cref="From"/> reads a <see cref="PointOfInterestSettings"/>).
	/// </summary>
	public sealed class PointOfInterestPlanSettings
	{
		public bool Capital;
		/// <summary>Only the detected kinds; nothing placed.</summary>
		public bool NaturalOnly;
		public float DensityScale = 1f;
		public float Multiplier = 1f;
		public List<PointOfInterestKindOverride> Overrides = new List<PointOfInterestKindOverride>();

		/// <summary>A scene's settings; null is Normal density with no capital (Jim, 2026-10-10).</summary>
		public static PointOfInterestPlanSettings From(PointOfInterestSettings settings)
		{
			if (settings == null)
			{
				return new PointOfInterestPlanSettings();
			}
			return new PointOfInterestPlanSettings
			{
				Capital = settings.Capital,
				NaturalOnly = settings.Density == PointOfInterestDensity.NaturalOnly,
				DensityScale = settings.DensityScale,
				Multiplier = settings.Multiplier,
				Overrides = settings.Density == PointOfInterestDensity.NaturalOnly ? new List<PointOfInterestKindOverride>() : new List<PointOfInterestKindOverride>(settings.Overrides),
			};
		}

		/// <summary>The override for a kind, or null.</summary>
		public PointOfInterestKindOverride OverrideFor(POIType kind)
		{
			if (Overrides != null)
			{
				foreach (PointOfInterestKindOverride entry in Overrides)
				{
					if (entry != null && entry.Kind == kind)
					{
						return entry;
					}
				}
			}
			return null;
		}
	}

	/// <summary>
	/// Everything the planner reads about a scene. Pure values and functions of (east, north) in scene metres, the
	/// scene spanning x ∈ [−W/2, W/2], z ∈ [−D/2, D/2]; no scene, no terrain and no editor API.
	/// </summary>
	public sealed class PointOfInterestPlanInput
	{
		public float WidthMetres;
		public float DepthMetres;
		/// <summary>The ground, scene metres above sea level.</summary>
		public Func<float, float, float> Ground;
		/// <summary>The sea's (or lava's) surface, world y.</summary>
		public float SeaLevel;
		/// <summary>Whether the scene has a sea at <see cref="SeaLevel"/>: land must stand clear of it, and sea-floor kinds need it.</summary>
		public bool HasSea;
		/// <summary>False when the "sea" is lava: nothing is sited under it.</summary>
		public bool SeaIsWater = true;
		/// <summary>The rivers and lakes; null for none.</summary>
		public PointOfInterestWater Water;
		/// <summary>The biomes in the scene; <see cref="BiomeAt"/> indexes it.</summary>
		public IReadOnlyList<PointOfInterestBiome> Biomes = Array.Empty<PointOfInterestBiome>();
		/// <summary>The dominant biome's index at (east, north), −1 for none; null for none anywhere.</summary>
		public Func<float, float, int> BiomeAt;
		/// <summary>Rock hardness 0 … 1 at (east, north, altitude); null reads 0.5 everywhere.</summary>
		public Func<float, float, float, float> HardnessAt;
		/// <summary>How much of a plateau the ground became, 0 … 1; null for none (a repaint has none).</summary>
		public Func<float, float, float> PlateauAt;
		/// <summary>True where nothing may stand: a terrain hole, a canyon wall. Null for nowhere.</summary>
		public Func<float, float, bool> Blocked;
		public PointOfInterestPlanSettings Settings = new PointOfInterestPlanSettings();
		public PointOfInterestRules Rules;
		/// <summary>The scene's POI seed.</summary>
		public int Seed;
		/// <summary>
		/// Whether a kind will be built (it has a template or a shaper): only built sites get a pad and a keep-out. Null
		/// builds every kind. A kind that is not built is still sited and named — a marker on the map.
		/// </summary>
		public Func<POIType, bool> Builds;
		/// <summary>How far inside the scene's edge every site's footprint keeps, metres.</summary>
		public float EdgeMarginMetres = 48f;
		/// <summary>The scoring grid, metres.</summary>
		public float CellMetres = 16f;
		/// <summary>The least gap between two placed sites' footprints, metres.</summary>
		public float SiteGapMetres = 24f;
		/// <summary>
		/// The least distance between two placed sites' centres whatever their kinds, metres, so the best ground does
		/// not gather a shrine, a tower and an obelisk into one clump. A kind that belongs beside a settlement (a
		/// graveyard) keeps only the footprint gap from it.
		/// </summary>
		public float MinSiteSpacingMetres = 150f;
		/// <summary>How far above the sea land sites stand at least, metres: above the highest tide and its swash.</summary>
		public float LandClearMetres = 3f;
	}

	/// <summary>What the planner decided: the sites, the pads it flattens and the discs nothing else may stand in.</summary>
	public sealed class PointOfInterestPlan
	{
		public int Seed;
		public readonly List<PointOfInterestRecord> Records = new List<PointOfInterestRecord>();
		public readonly List<PointOfInterestPad> Pads = new List<PointOfInterestPad>();
		public readonly List<PointOfInterestKeepOut> KeepOuts = new List<PointOfInterestKeepOut>();
		/// <summary>Shaper data kept with the plan (a cave's mouth and tunnel), so a repaint can build again without planning.</summary>
		public readonly List<PointOfInterestShape> Shapes = new List<PointOfInterestShape>();
		public readonly List<string> Notes = new List<string>();
		/// <summary>The routed ways (not the settlements' streets, which their layouts lay at every place).</summary>
		public readonly List<ScenePath> Paths = new List<ScenePath>();
		/// <summary>The palette slices the ways are surfaced from (<see cref="ScenePointsOfInterest.PathLayers"/>).</summary>
		public Vector4 PathLayers = new Vector4(-1f, -1f, -1f, -1f);
		/// <summary>Per layer, its biome's path and road slices (<see cref="ScenePointsOfInterest.PathEarthMap"/>).</summary>
		public float[] PathEarthMap = Array.Empty<float>();
		public float[] PathRoadMap = Array.Empty<float>();

		/// <summary>Whether (x, z) lies in any keep-out disc.</summary>
		public bool IsKeptOut(float x, float z)
		{
			foreach (PointOfInterestKeepOut keepOut in KeepOuts)
			{
				if (keepOut.Contains(x, z))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The record with this id, or null.</summary>
		public PointOfInterestRecord Find(int id)
		{
			foreach (PointOfInterestRecord record in Records)
			{
				if (record.Id == id)
				{
					return record;
				}
			}
			return null;
		}

		/// <summary>The plan a scene's asset holds: what a repaint reuses rather than planning again.</summary>
		public static PointOfInterestPlan FromAsset(ScenePointsOfInterest asset)
		{
			var plan = new PointOfInterestPlan();
			if (asset == null)
			{
				return plan;
			}
			plan.Seed = asset.Seed;
			plan.Records.AddRange(asset.Points);
			plan.Pads.AddRange(asset.Pads);
			plan.KeepOuts.AddRange(asset.KeepOuts);
			if (asset.Shapes != null)
			{
				plan.Shapes.AddRange(asset.Shapes);
			}
			if (asset.Paths != null)
			{
				plan.Paths.AddRange(asset.Paths.FindAll(p => p != null && p.Class != ScenePathClass.Street));
			}
			plan.PathLayers = asset.PathLayers;
			plan.PathEarthMap = asset.PathEarthMap ?? Array.Empty<float>();
			plan.PathRoadMap = asset.PathRoadMap ?? Array.Empty<float>();
			return plan;
		}

		/// <summary>Writes the plan into a scene's asset, replacing what it held.</summary>
		public void WriteTo(ScenePointsOfInterest asset, string sceneName)
		{
			asset.Format = ScenePointsOfInterest.CurrentFormat;
			asset.Seed = Seed;
			asset.SceneName = sceneName;
			asset.Points = new List<PointOfInterestRecord>(Records);
			asset.Pads = new List<PointOfInterestPad>(Pads);
			asset.KeepOuts = new List<PointOfInterestKeepOut>(KeepOuts);
			asset.Shapes = new List<PointOfInterestShape>(Shapes);
			asset.Paths = new List<ScenePath>(Paths);
			asset.PathLayers = PathLayers;
			asset.PathEarthMap = PathEarthMap;
			asset.PathRoadMap = PathRoadMap;
		}
	}
}
#endif
