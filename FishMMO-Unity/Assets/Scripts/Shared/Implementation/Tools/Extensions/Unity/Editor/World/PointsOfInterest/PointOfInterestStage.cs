#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// One scene's point-of-interest stage across a cut, a repaint or a Regenerate POIs: the PLAN made with the ground
	/// (sites, pads, keep-outs, shapes) and the PLACE made with the scene (the asset, the site objects, the props).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two passes, because the ground is decided in between.</b> <see cref="PlanSites"/> runs inside
	/// <see cref="SceneGenerator"/>'s paint, right after the biome field and before the splat: the pads it flattens and
	/// the holes a shaper cuts are then painted, and the scatter, cliffs, boulders and fall ledges keep out of every
	/// site (<see cref="Excludes"/>). <see cref="Place"/> runs after the river flow is baked and before the props and
	/// NavMesh are, so what the sites build is in the NavMesh.
	/// </para>
	/// <para>
	/// <b>A repaint does not plan.</b> It reads the plan back from the scene's asset (<see cref="ForRepaint"/>) and
	/// places it again: the ground already carries the pads, and planning again on flattened ground would move them.
	/// A re-cut regenerates everything (2026-10-10).
	/// </para>
	/// </remarks>
	public sealed class PointOfInterestStage
	{
		public readonly SceneGenerationRequest Request;
		/// <summary>The terrain folder the asset and props are written to.</summary>
		public readonly string TerrainFolder;
		/// <summary>True when the plan was read back rather than made: nothing is flattened or carved.</summary>
		public readonly bool Reused;
		/// <summary>The erosion the cut ran (plateaus), when there was one.</summary>
		public SceneErosionReport Erosion;
		/// <summary>The cut's height field, to read <see cref="Erosion"/>'s per-sample plateau weight.</summary>
		public SceneHeightField Field;
		/// <summary>The previous cut's records (before a re-cut cleared its folder), for carrying unlock indices over.</summary>
		public readonly List<PointOfInterestRecord> Previous = new List<PointOfInterestRecord>();
		/// <summary>
		/// The previous plan's path slices: still right after a Regenerate POIs, which keeps the palette (a cut's paint sets
		/// them afresh).
		/// </summary>
		private Vector4 previousPathLayers = new Vector4(-1f, -1f, -1f, -1f);
		private float[] previousEarthMap = Array.Empty<float>(), previousRoadMap = Array.Empty<float>(), previousStoneMap = Array.Empty<float>();

		public PointOfInterestPlan Plan { get; private set; }
		/// <summary>The template each built site was given, by record id.</summary>
		public readonly Dictionary<int, PointOfInterestTemplate> Templates = new Dictionary<int, PointOfInterestTemplate>();
		/// <summary>The settings asset's path, read before anything could unload it, to revive it by.</summary>
		private readonly string settingsPath;

		private PointOfInterestStage(SceneGenerationRequest request, string terrainFolder, bool reused)
		{
			Request = request;
			TerrainFolder = terrainFolder;
			Reused = reused;
			settingsPath = request?.PointsOfInterest != null ? AssetDatabase.GetAssetPath(request.PointsOfInterest) : null;
		}

		/// <summary>
		/// The stage of a fresh cut. Call it before a re-cut clears the old terrain folder: it reads the old scene's
		/// records (for unlock indices) while they are still there, and the settings asset's path while it is live.
		/// </summary>
		public static PointOfInterestStage ForCut(SceneGenerationRequest request, string terrainFolder)
		{
			var stage = new PointOfInterestStage(request, terrainFolder, false);
			ScenePointsOfInterest old = AssetDatabase.LoadAssetAtPath<ScenePointsOfInterest>(AssetPath(terrainFolder, request.SceneName));
			if (old != null)
			{
				stage.previousPathLayers = old.PathLayers;
				stage.previousEarthMap = old.PathEarthMap ?? Array.Empty<float>();
				stage.previousRoadMap = old.PathRoadMap ?? Array.Empty<float>();
				stage.previousStoneMap = old.PathStoneMap ?? Array.Empty<float>();
				foreach (PointOfInterestRecord record in old.Points)
				{
					if (record != null)
					{
						stage.Previous.Add(new PointOfInterestRecord { Id = record.Id, Kind = record.Kind, Position = record.Position, UnlockIndex = record.UnlockIndex });
					}
				}
			}
			return stage;
		}

		/// <summary>
		/// The stage of a repaint: the plan read back from the scene's asset. With no asset (a scene cut before POIs) it
		/// plans afresh at the first <see cref="PlanSites"/> but flattens nothing, since a repaint keeps the heights.
		/// </summary>
		public static PointOfInterestStage ForRepaint(SceneGenerationRequest request, string terrainFolder, List<string> notes)
		{
			ScenePointsOfInterest asset = AssetDatabase.LoadAssetAtPath<ScenePointsOfInterest>(AssetPath(terrainFolder, request.SceneName));
			if (asset == null && request.Body != null)
			{
				// A LOCAL copy repainted for the first time reads the committed scene's plan.
				asset = AssetDatabase.LoadAssetAtPath<ScenePointsOfInterest>(AssetPath(SceneGenerator.TerrainFolder(request.Body, request.SceneName), request.SceneName));
			}
			if (asset == null || asset.Format != ScenePointsOfInterest.CurrentFormat)
			{
				notes?.Add(asset == null
					? "No points-of-interest plan is stored for this scene; one is planned now without pads (a re-cut flattens them)."
					: "The stored points-of-interest plan is an old format; one is planned now without pads (a re-cut flattens them).");
				return new PointOfInterestStage(request, terrainFolder, true);
			}
			var stage = new PointOfInterestStage(request, terrainFolder, true) { Plan = PointOfInterestPlan.FromAsset(asset) };
			stage.ResolveTemplates();
			return stage;
		}

		/// <summary>The scene's POI asset path in a terrain folder.</summary>
		public static string AssetPath(string terrainFolder, string sceneName)
			=> ScenePointsOfInterest.AssetPath(terrainFolder, WorldEditorAssets.Sanitize(sceneName));

		/// <summary>
		/// Discs the cut keeps clear of sites, ways, trees and rocks besides the plan's own: a cloud world's bridge ends and
		/// landing stages, laid after the sites.
		/// </summary>
		public readonly List<PointOfInterestKeepOut> Reserved = new List<PointOfInterestKeepOut>();

		/// <summary>Kinds this cut may not build (a cloud world's caves, which would dig out through an island's underside); null allows every kind.</summary>
		public Func<POIType, bool> Forbids;

		/// <summary>True when the scene has a capital whatever its settings say (a cloud world's city island).</summary>
		public bool WantsCapital;

		/// <summary>True when the terrain's holes are open sky (a cloud world's), handed to the site builders as <see cref="PointOfInterestSiteContext.SkyAt"/>.</summary>
		public bool HolesAreSky;

		/// <summary>The settings the scene asked for, revived by path if a refresh left the reference fake-null.</summary>
		public PointOfInterestSettings Settings
		{
			get
			{
				if (Request.PointsOfInterest == null && !string.IsNullOrEmpty(settingsPath))
				{
					Request.PointsOfInterest = AssetDatabase.LoadAssetAtPath<PointOfInterestSettings>(settingsPath);
				}
				return Request.PointsOfInterest;
			}
		}

		/// <summary>True when the plan has ways.</summary>
		public bool HasPaths => Plan != null && Plan.Paths.Count > 0;

		/// <summary>The palette slices the ways are surfaced from, set by the paint once the palette is built.</summary>
		public Vector4 PathLayers
		{
			get => Plan != null ? Plan.PathLayers : new Vector4(-1f, -1f, -1f, -1f);
			set
			{
				if (Plan != null)
				{
					Plan.PathLayers = value;
				}
			}
		}

		/// <summary>Sets each layer's own biome's path, road and cobble slices (<see cref="ScenePointsOfInterest.PathEarthMap"/>).</summary>
		public void SetPathLayerMaps(float[] earth, float[] road, float[] stone)
		{
			if (Plan != null)
			{
				Plan.PathEarthMap = earth ?? Array.Empty<float>();
				Plan.PathRoadMap = road ?? Array.Empty<float>();
				Plan.PathStoneMap = stone ?? Array.Empty<float>();
			}
		}

		/// <summary>
		/// True where the plan keeps everything that stands out (trees, rocks, cliffs, river boulders): the sites' footprints
		/// and the ways with their clearance. Null when it keeps nothing out.
		/// </summary>
		public Func<float, float, bool> Excludes
		{
			get
			{
				Func<float, float, bool> sites = Plan != null && Plan.KeepOuts.Count > 0 ? KeepOutQuery(Plan.KeepOuts) : null;
				if (Reserved.Count > 0)
				{
					Func<float, float, bool> reserved = KeepOutQuery(Reserved), planned = sites;
					sites = planned != null ? (x, z) => planned(x, z) || reserved(x, z) : reserved;
				}
				if (!HasPaths)
				{
					return sites;
				}
				var ways = new PathExclusion(Plan.Paths);
				return sites != null ? (x, z) => sites(x, z) || ways.Trees(x, z) : (Func<float, float, bool>)ways.Trees;
			}
		}

		/// <summary>
		/// <see cref="Excludes"/> for the details (grass, flowers): a road's trodden width only. A site's grounds keep their
		/// grass between the buildings (2026-10-11: a village stood on a bare disc of terrain); the grass and details are
		/// thinned per blade on its streets, yards and paths, and kept out of its buildings, by the path field
		/// (FishGroundPaths.hlsl, the yards PropsFeature writes). Null when it keeps nothing out.
		/// </summary>
		public Func<float, float, bool> DetailExcludes
		{
			get
			{
				if (!HasPaths)
				{
					return null;
				}
				var ways = new PathExclusion(Plan.Paths);
				return ways.Details;
			}
		}

		/// <summary>
		/// True inside any of the discs: bucketed on a 64 m grid, since the scatter asks it for millions of samples. Read
		/// only once built, so it is safe from the scatter's worker threads.
		/// </summary>
		public static Func<float, float, bool> KeepOutQuery(IReadOnlyList<PointOfInterestKeepOut> keepOuts)
		{
			const float bucket = 64f;
			var buckets = new Dictionary<long, List<PointOfInterestKeepOut>>();
			foreach (PointOfInterestKeepOut keepOut in keepOuts)
			{
				int x0 = Mathf.FloorToInt((keepOut.X - keepOut.Radius) / bucket), x1 = Mathf.FloorToInt((keepOut.X + keepOut.Radius) / bucket);
				int z0 = Mathf.FloorToInt((keepOut.Z - keepOut.Radius) / bucket), z1 = Mathf.FloorToInt((keepOut.Z + keepOut.Radius) / bucket);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						long key = ((long)x << 32) ^ (uint)z;
						if (!buckets.TryGetValue(key, out List<PointOfInterestKeepOut> list))
						{
							list = new List<PointOfInterestKeepOut>();
							buckets[key] = list;
						}
						list.Add(keepOut);
					}
				}
			}
			return (x, z) =>
			{
				long key = ((long)Mathf.FloorToInt(x / bucket) << 32) ^ (uint)Mathf.FloorToInt(z / bucket);
				if (!buckets.TryGetValue(key, out List<PointOfInterestKeepOut> list))
				{
					return false;
				}
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Contains(x, z))
					{
						return true;
					}
				}
				return false;
			};
		}

		/// <summary>How far sites and ways keep from a holed patch of ground (a canyon wall's), metres.</summary>
		public const float CanyonWallClearMetres = 4f;

		/// <summary>The scene's POI seed: the scene's own seed and a "POI" salt.</summary>
		public static int SeedFor(SceneGenerationRequest request)
			=> (int)(PointOfInterestPlanner.Mix(SceneGenerator.SceneSeed(request) ^ 0x504F4953u) & 0x7FFFFFFFu);

		// ── Plan ────────────────────────────────────────────────────

		/// <summary>
		/// Plans the scene's sites on its finished ground (once; a reused plan returns at once): the planner, races,
		/// climate variants, templates, pads flattened, shapers' plans, names and unlock indices.
		/// </summary>
		/// <param name="field">The scene's biome field; null plans with no biomes.</param>
		public void PlanSites(Scene scene, TerrainTilePlan tiles, Terrain[,] terrains, SceneBiomeField field, SceneWater water, List<string> notes)
		{
			if (Plan != null)
			{
				return;
			}
			if (water != null && !water.Any)
			{
				water = null;
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(Request.Body);
			PointOfInterestRules rules = PointOfInterestCatalogue.Resolve();
			PointOfInterestTemplateLibrary library = PointOfInterestTemplateLibrary.Load();
			Func<float, float, float> ground = (east, north) => SceneGenerator.GroundAltitude(terrains, tiles, east, north);
			Func<float, float, float, float> hardness = Hardness(system);
			PointOfInterestPlanInput input = Input(tiles, terrains, field, water, system, rules, ground, hardness);
			Func<POIType, bool> forbids = Forbids;
			input.Builds = kind => (forbids == null || !forbids(kind)) && (library.Has(kind) || PointOfInterestShapers.Handles(kind));

			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(input);
			plan.PathLayers = previousPathLayers;
			plan.PathEarthMap = previousEarthMap;
			plan.PathRoadMap = previousRoadMap;
			plan.PathStoneMap = previousStoneMap;
			if (Reused)
			{
				// A repaint keeps the heights: no pads.
				plan.Pads.Clear();
			}
			Plan = plan;

			var biomes = new Dictionary<int, BiomeTemplate>();
			if (field != null)
			{
				foreach (BiomeTemplate biome in field.Biomes)
				{
					if (biome != null)
					{
						biomes[BiomeRegistry.IDOf(biome)] = biome;
					}
				}
			}
			ScenePlacementClimate climate = Request.Body != null
				? ScenePlacementClimate.For(system, Request.Body, Request.Footprint, Request.ResolvedRadiusKm, true)
				: null;

			foreach (PointOfInterestRecord record in plan.Records)
			{
				biomes.TryGetValue(record.BiomeID, out BiomeTemplate biome);
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(record.Kind);
				if (info.Has(PointOfInterestTraits.UsesRace))
				{
					record.Race = PickRace(record, rules.RuleFor(record.Kind));
				}
				if (biome != null && climate != null)
				{
					ClimateSample sample = climate.SampleAt(record.Position, out _);
					record.VariantIndex = PointOfInterestNaming.VariantIndexOf(biome, biome.ResolveOwnVariant(sample.Temperature, sample.Humidity));
				}
				// The template's weight in the site's biome is the biome template's for its variant (2026-10-16); a cave whose host
				// links its form to a cave biome weighs the two templates' variants merged (2026-10-25, I2n, decision 51).
				Func<PointOfInterestTemplate, float> weights = BiomeTemplateArt.SiteVariantWeights(biome);
				if (CaveShaper.TryForm(record.Kind, out _))
				{
					CaveBiome caves = CaveInteriorArt.OfSite(Request, record);
					if (caves.Linked)
					{
						weights = CaveInteriorArt.SiteVariantWeights(in caves);
					}
				}
				PointOfInterestTemplate template = library.Pick(record, weights, RaceCategoryOf(record.Race));
				if (template != null)
				{
					record.Template = template.name;
					Templates[record.Id] = template;
				}
			}
			// A site that will build nothing (no template fitted it, no shaper takes it) keeps no pad and no keep-out.
			plan.Pads.RemoveAll(p => !Builds(plan.Find(p.Id)));
			plan.KeepOuts.RemoveAll(k => !Builds(plan.Find(k.Id)));

			if (!Reused)
			{
				int flattened = 0;
				// A pad's bank keeps off the water: a river's channel and its banks, a lake (PointOfInterestTerrain.BankReach).
				Func<float, float, bool> padWet = water == null ? null : (Func<float, float, bool>)((x, z) =>
					!float.IsNegativeInfinity(water.RiverSurfaceNear(x, z)) || !float.IsNegativeInfinity(water.LakeSurfaceNear(x, z)));
				// A shore pad (a mill's yard) also leaves the channel itself alone inside its radius.
				Func<float, float, bool> padChannel = water == null ? null : (Func<float, float, bool>)((x, z) =>
					!float.IsNegativeInfinity(water.RiverSurfaceNear(x, z, 0f)) || !float.IsNegativeInfinity(water.LakeSurfaceNear(x, z)));
				/* Nor is a bank cut under the water beside it: a mill's yard levelled to its floor (its own water and a freeboard)
				 * cut the bank upstream, where the river past it stands higher, 0.64 m under the river (Flo Monolith's Sha Mill Race,
				 * 2026-10-15), and the water measured on the finished ground stood that much lower. As the ways' carve keeps it. */
				Func<float, float, float> padFloor = water == null ? null : (Func<float, float, float>)((x, z) =>
				{
					float near = Mathf.Max(water.RiverSurfaceNear(x, z, PathBankReachMetres), water.LakeSurfaceNear(x, z));
					return float.IsNegativeInfinity(near) ? float.NegativeInfinity : near + PathBankFreeboardMetres;
				});
				foreach (PointOfInterestPad pad in plan.Pads)
				{
					float flatness = Templates.TryGetValue(pad.Id, out PointOfInterestTemplate template) ? template.PadFlatness : 1f;
					if (PointOfInterestTerrain.FlattenPad(terrains, tiles, pad, flatness, padWet, padChannel, padFloor) > 0)
					{
						flattened++;
					}
				}
				if (flattened > 0)
				{
					notes?.Add($"Points of interest: {flattened} pad(s) flattened.");
				}

				var context = new PointOfInterestPlanContext
				{
					Scene = scene,
					Request = Request,
					Tiles = tiles,
					Terrains = terrains,
					Water = water,
					Plan = plan,
					Seed = input.Seed,
					GroundAt = ground,
					HardnessAt = hardness,
					Notes = notes,
				};
				// Water read back from a scene's hydrology carries the last plan's moats: they are dug again below.
				MoatShaper.ClearWater(water);
				foreach (PointOfInterestRecord record in plan.Records.ToArray())
				{
					IPointOfInterestShaper shaper = PointOfInterestShapers.For(record.Kind);
					if (shaper == null)
					{
						continue;
					}
					try
					{
						shaper.Plan(context, record);
					}
					catch (Exception ex)
					{
						context.Reject(record, $"its shaper threw: {ex.Message}");
						Debug.LogException(ex);
					}
				}
				if (context.Rejected.Count > 0)
				{
					plan.Records.RemoveAll(r => context.Rejected.Contains(r.Id));
					plan.Pads.RemoveAll(p => context.Rejected.Contains(p.Id));
					plan.KeepOuts.RemoveAll(k => context.Rejected.Contains(k.Id));
					plan.Shapes.RemoveAll(s => s == null || context.Rejected.Contains(s.SiteId));
					foreach (int id in context.Rejected)
					{
						Templates.Remove(id);
					}
				}

				// The ways between the sites, on the ground the pads and shapers left: routed, bridged and carved in.
				PlanPaths(tiles, terrains, water, input, ground, library, biomes, notes);
			}

			int seed = input.Seed;
			string bodySeed = Request.Body != null ? Request.Body.ResolvedTerrainSeed.ToString() : "1";
			PointOfInterestNaming.NameAll(plan.Records, PointOfInterestNaming.Generator(Request.SceneName, bodySeed, seed),
				record =>
				{
					biomes.TryGetValue(record.BiomeID, out BiomeTemplate biome);
					return PointOfInterestNaming.Describe(record, biome != null ? biome.ResolvedDisplayName : null);
				});
			if (!NameGenerator.IsReady)
			{
				notes?.Add("Points of interest: the naming templates are not loaded, so sites carry their kinds' names.");
			}
			PointOfInterestUnlocks.Assign(plan.Records, Previous, PointOfInterestUnlocks.Unlockable);
			plan.Notes.ForEach(n => notes?.Add($"Points of interest: {n}"));
			notes?.Add($"Points of interest: {Summary(plan)}.");
		}

		/// <summary>
		/// Plans the scene's ways (<see cref="PathNetworkPlanner"/>), turns the bridges they need into Bridge sites, and
		/// carves the ways into the ground (<see cref="PathCarver"/>).
		/// </summary>
		/// <summary>How far above the water beside it a way's carve leaves a bank, metres.</summary>
		public const float PathBankFreeboardMetres = 0.15f;

		/// <summary>How far past a river's banks a way's carve is held above its water, metres.</summary>
		public const float PathBankReachMetres = 8f;

		private void PlanPaths(TerrainTilePlan tiles, Terrain[,] terrains, SceneWater water, PointOfInterestPlanInput input,
			Func<float, float, float> ground, PointOfInterestTemplateLibrary library, Dictionary<int, BiomeTemplate> biomes, List<string> notes)
		{
			// A waterfall's reach is crossed by no way and bridged by none: the way goes above the lip or below the pool.
			FallReaches falls = FallReaches.Of(water != null ? water.Rivers : null, PointOfInterestPlanner.MinFallMetres);
			PathCellWater WaterAt(float x, float z)
			{
				if (input.HasSea && ground(x, z) < input.SeaLevel + 0.3f)
				{
					return PathCellWater.Open;
				}
				switch (water != null ? water.KindAt(x, z) : WaterKind.None)
				{
					case WaterKind.River: return falls.Contains(x, z) ? PathCellWater.Open : PathCellWater.River;
					case WaterKind.Lake: return PathCellWater.Open;
					case WaterKind.DryWash:
					case WaterKind.Bar:
					case WaterKind.Playa:
						return PathCellWater.Rough;
					default: return PathCellWater.Dry;
				}
			}
			var pathInput = new PathPlanInput
			{
				WidthMetres = tiles.WidthMetres,
				DepthMetres = tiles.DepthMetres,
				Ground = ground,
				WaterAt = WaterAt,
				// A river's depth under its own level where its centre line passes (SceneWater.RiverSurfaceNear): the grid's
				// surface is the highest of its samples about a point, which down a falling stream reads it deeper by the fall
				// over a sample, so a ford or a bridge turned on where the way's points fell.
				WaterDepth = (x, z) =>
				{
					if (water == null)
					{
						return 0f;
					}
					float level = water.RiverSurfaceNear(x, z);
					return (float.IsNegativeInfinity(level) ? water.SurfaceAt(x, z) : level) - ground(x, z);
				},
				Blocked = input.Blocked,
				Records = Plan.Records,
				KeepOuts = Plan.KeepOuts,
				Shapes = Plan.Shapes,
				Pads = Plan.Pads,
				HasStreets = r => Templates.TryGetValue(r.Id, out PointOfInterestTemplate t)
					&& (t.Layout == PointOfInterestLayout.Street || t.Layout == PointOfInterestLayout.Walled),
				Seed = input.Seed,
			};
			var clock = System.Diagnostics.Stopwatch.StartNew();
			PathPlan paths = PathNetworkPlanner.Plan(pathInput);
			MarkBoardwalks(paths.Paths, input, ground, biomes, (x, z) => WaterAt(x, z) == PathCellWater.Dry, notes);

			// A bridge the rule placed between two towns that no way was laid over stands on no path: it is dropped.
			var unused = new HashSet<int>();
			foreach (PointOfInterestRecord r in Plan.Records)
			{
				if (r.Kind == POIType.Bridge && !paths.UsedBridges.Contains(r.Id))
				{
					unused.Add(r.Id);
				}
			}
			if (unused.Count > 0)
			{
				Plan.Records.RemoveAll(r => unused.Contains(r.Id));
				Plan.Pads.RemoveAll(p => unused.Contains(p.Id));
				Plan.KeepOuts.RemoveAll(k => unused.Contains(k.Id));
				Plan.Shapes.RemoveAll(s => s == null || unused.Contains(s.SiteId));
				foreach (int id in unused)
				{
					Templates.Remove(id);
				}
				notes?.Add($"Points of interest: {unused.Count} bridge(s) no way crosses dropped.");
			}

			// Each bridge a way needs and no standing bridge serves becomes a Bridge site: named, on the map, built from its template.
			var ids = new HashSet<int>();
			foreach (PointOfInterestRecord r in Plan.Records)
			{
				ids.Add(r.Id);
			}
			int bridged = 0, unbuilt = 0;
			foreach (PathBridgeSite site in paths.Bridges)
			{
				Vector3 c = site.Centre;
				int id = PointOfInterestPlanner.StableId(POIType.Bridge, c.x, c.z);
				while (!ids.Add(id))
				{
					id = id == int.MaxValue ? 1 : id + 1;
				}
				int biomeId = 0;
				if (input.BiomeAt != null && input.Biomes.Count > 0)
				{
					/* A bridge belongs to the banks it joins, not to the water under its middle (decision 35 (i), 2026-10-18): over a
					 * river 8 m and more wide the centre is the River biome's, which built every such bridge in the River's palette. */
					int b = Mathf.Clamp(PathCrossings.BridgeBiome(c, site.Yaw, site.Span, input.BiomeAt,
						i => input.Biomes[i] != null && biomes.TryGetValue(input.Biomes[i].Id, out BiomeTemplate t) && PathCrossings.IsWaterBiome(t != null ? t.WaterRole : BiomeWaterRole.None),
						input.Biomes.Count), 0, input.Biomes.Count - 1);
					biomeId = input.Biomes[b] != null ? input.Biomes[b].Id : 0;
				}
				var record = new PointOfInterestRecord
				{
					Id = id,
					Kind = POIType.Bridge,
					Position = new Vector3(Mathf.Round(c.x * 100f) / 100f, Mathf.Round(c.y * 100f) / 100f, Mathf.Round(c.z * 100f) / 100f),
					Yaw = Mathf.Round(site.Yaw),
					Radius = PathBridgeSite.RecordRadius(site.Span),
					SizeClass = 1,
					BiomeID = biomeId,
					DetailTier = PointOfInterestKinds.Info(POIType.Bridge).DetailTier,
					RequiresDiscovery = true,
					ParentId = -1,
					RiverId = -1,
					PlanetRiver = -1,
					LakeId = -1,
					Template = string.Empty,
					Race = string.Empty,
					Name = string.Empty,
					Description = string.Empty,
					SiteSeed = PointOfInterestPlanner.SiteSeed(input.Seed, id),
					UnlockIndex = -1,
				};
				NearestRiver(input.Water, c.x, c.z, record);
				biomes.TryGetValue(biomeId, out BiomeTemplate biome);
				PointOfInterestTemplate template = library.Pick(record, BiomeTemplateArt.SiteVariantWeights(biome), string.Empty);
				if (template == null)
				{
					unbuilt++;
					continue;
				}
				record.Template = template.name;
				Templates[id] = template;
				Plan.Records.Add(record);
				Plan.KeepOuts.Add(new PointOfInterestKeepOut { Id = id, X = record.Position.x, Z = record.Position.z, Radius = record.Radius });
				bridged++;
			}

			Plan.Paths.AddRange(paths.Paths);
			/* Where a way crosses a moat, the far bank is levelled to the drawbridge's hinge and the way ramps down to it (MoatShaper
			 * .Landings): before the ways are carved, so the road grades into the landing. */
			var laid = new List<MoatShaper.Landing>();
			int landings = MoatShaper.Landings(terrains, tiles, Plan.Shapes, Plan.Paths, ground, laid);
			if (landings > 0)
			{
				notes?.Add($"Paths: {landings} moat crossing(s) given a landing at the drawbridge's level.");
			}
			// A way across a moat keeps the banks' level (it crosses on the drawbridge) and leaves the ditch undug.
			List<PointOfInterestShape> shapes = Plan.Shapes;
			/* And on a landing past the ditch, at the drawbridge's level, left as it was levelled: carved as any way's stretch, its
			 * level taken from the ground either side past the landing's edges, the road filled the landing back up toward the far
			 * bank's own height (Flo Monolith's Mossy Elkring Keep, 2026-10-15). */
			Func<float, float, float> deck = (x, z) => MoatShaper.BankLevel(shapes, x, z, ground, out float bank) ? bank
				: MoatShaper.OnLanding(laid, x, z, out MoatShaper.Landing landing) ? landing.Hinge : ground(x, z);
			// Never cut under the water beside it: a way along a bank keeps the bank above the river's or lake's surface.
			Func<float, float, float> aboveWater = water == null ? null : (Func<float, float, float>)((x, z) =>
			{
				/* Out to well past the river's own margin: a way cut under the water just past it left the water's edge standing
				 * over the lower ground beside it (Flo Monolith, 2026-10-13, a metre and a half under 4 m past the bank). */
				float near = Mathf.Max(water.RiverSurfaceNear(x, z, PathBankReachMetres), water.LakeSurfaceNear(x, z));
				return float.IsNegativeInfinity(near) ? float.NegativeInfinity : near + PathBankFreeboardMetres;
			});
			int carved = PathCarver.Carve(terrains, tiles, Plan.Paths, deck, (x, z) => WaterAt(x, z) != PathCellWater.Dry, Plan.Pads,
				(x, z) => MoatShaper.Contains(shapes, x, z) || MoatShaper.OnLandingOrRamp(laid, x, z), aboveWater);
			// A gorge's stone arch is laid level between raked rims: its approaches are built up on the low rim and cut into
			// the high one here, after the ways (which would even them back toward the ground beside), so the site finds rims
			// that meet its deck.
			int approaches = 0;
			foreach (PointOfInterestRecord record in Plan.Records)
			{
				if (record.Kind == POIType.Bridge && PropsFeature.CarveArchApproaches(terrains, tiles, record, ground, water, Plan.Records) > 0)
				{
					approaches++;
				}
			}
			if (approaches > 0)
			{
				notes?.Add($"Paths: {approaches} gorge arch(es) given approaches built up and cut to their level decks.");
			}
			// The ways' heights on the ground as carved (over a moat, its banks' level: the drawbridge's).
			foreach (ScenePath path in Plan.Paths)
			{
				for (int i = 0; i < path.Count; i++)
				{
					Vector3 p = path.Points[i];
					path.Points[i] = new Vector3(p.x, deck(p.x, p.z), p.z);
				}
			}
			paths.Notes.ForEach(n => notes?.Add($"Paths: {n}"));
			notes?.Add($"Paths: {bridged} bridge site(s) added, {carved:N0} heightmap sample(s) carved, in {clock.ElapsedMilliseconds:N0} ms." +
				(unbuilt > 0 ? $" {unbuilt} crossing(s) found no bridge template and are waded." : string.Empty));
		}

		/// <summary>
		/// Flags the stretches of the ways their biomes raise as boardwalks (2026-10-18, I2i; PathBoardwalks.MarkWays), before they
		/// are carved, so the carve and the path field leave the ground under each deck as it lies. Nothing when no biome of the
		/// scene raises a way (every biome's default).
		/// </summary>
		private static void MarkBoardwalks(List<ScenePath> ways, PointOfInterestPlanInput input, Func<float, float, float> ground,
			Dictionary<int, BiomeTemplate> biomes, Func<float, float, bool> dry, List<string> notes)
		{
			if (ways == null || input.BiomeAt == null || input.Biomes.Count == 0 || biomes == null)
			{
				return;
			}
			var settings = new BiomeBoardwalks[input.Biomes.Count];
			bool any = false;
			for (int b = 0; b < settings.Length; b++)
			{
				settings[b] = input.Biomes[b] != null && biomes.TryGetValue(input.Biomes[b].Id, out BiomeTemplate biome) ? BiomeTemplateArt.Boardwalks(biome) : BiomeBoardwalks.None;
				any |= settings[b] != BiomeBoardwalks.None;
			}
			if (!any)
			{
				return;
			}
			float Slope(float x, float z)
			{
				float dx = ground(x + 1f, z) - ground(x - 1f, z), dz = ground(x, z + 1f) - ground(x, z - 1f);
				return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) * 0.5f) * Mathf.Rad2Deg;
			}
			int marked = PathBoardwalks.MarkWays(ways, (x, z) => settings[Mathf.Clamp(input.BiomeAt(x, z), 0, settings.Length - 1)], Slope, dry);
			if (marked > 0)
			{
				notes?.Add($"Paths: {marked:N0} point(s) of way raised on boardwalks.");
			}
		}

		/// <summary>Gives a bridge the river it spans: the nearest river point within 40 m.</summary>
		private static void NearestRiver(PointOfInterestWater water, float x, float z, PointOfInterestRecord record)
		{
			if (water == null || water.Rivers == null)
			{
				return;
			}
			float best = 40f * 40f;
			foreach (RiverPath river in water.Rivers)
			{
				if (river == null || river.X == null)
				{
					continue;
				}
				for (int k = 0; k < river.X.Length; k++)
				{
					float dx = river.X[k] - x, dz = river.Z[k] - z;
					float d = dx * dx + dz * dz;
					if (d < best)
					{
						best = d;
						record.RiverId = river.Id;
						record.PlanetRiver = river.PlanetRiver;
					}
				}
			}
		}

		private bool Builds(PointOfInterestRecord record)
			=> record != null && (Templates.ContainsKey(record.Id) || PointOfInterestShapers.Handles(record.Kind));

		/// <summary>The planner's input for this scene's finished ground.</summary>
		private PointOfInterestPlanInput Input(TerrainTilePlan tiles, Terrain[,] terrains, SceneBiomeField field, SceneWater water,
			SolarSystemProfile system, PointOfInterestRules rules, Func<float, float, float> ground, Func<float, float, float, float> hardness)
		{
			bool underground = Request.Layer != null && Request.Layer.Underground;
			float levelMetres = 0f;
			SurfaceLiquid liquid = underground ? SurfaceLiquid.None : SurfaceLiquids.For(system, Request.Body, out levelMetres);
			float sea = liquid != SurfaceLiquid.None ? levelMetres * Request.VerticalScale : 0f;

			var biomes = new List<PointOfInterestBiome>();
			Func<float, float, int> biomeAt = null;
			if (field != null && field.Biomes.Count > 0)
			{
				// Lived-in kinds only where the world's air is breathable (decision 39, 2026-10-18): a thin or airless world's biomes
				// place their natural sites and relics alone; so do a methane, nitrogen, acid or molten-rock world's (decision 62).
				// Kinds that work open water only where the world has liquid water on its surface (decision 68; no kind by default).
				BiomeWorldConditions world = BiomeWorldConditions.For(system, Request.Body);
				bool breathable = BiomeAir.IsBreathable(in world), liquidWater = world.HasLiquidWater;
				foreach (BiomeTemplate biome in field.Biomes)
				{
					// The kinds the biome hosts are its template's (2026-10-16), not the catalogue rows' names.
					biomes.Add(biome != null
						? new PointOfInterestBiome(biome.name, BiomeRegistry.IDOf(biome), biome.ResolvedDisplayName, BiomeTemplateArt.SiteKinds(biome, rules, breathable, liquidWater))
						{
							// How deep its underwater sites stand (2026-10-25, phase I2m): (0, 0), each kind's own, unless its round says.
							UnderwaterDepth = biome.UnderwaterSiteDepth,
						}
						: null);
				}
				var scratch = new float[field.Biomes.Count];
				biomeAt = (east, north) => field.DominantIndexAt(east, north, scratch);
			}

			Func<float, float, float> plateau = null;
			if (Erosion != null && Erosion.PlateauWeight != null && Field != null && Erosion.PlateauWeight.Length == Field.Metres.Length)
			{
				float[] weight = Erosion.PlateauWeight;
				SceneHeightField f = Field;
				plateau = (east, north) =>
				{
					int x = Mathf.Clamp(Mathf.RoundToInt((east + tiles.WidthMetres * 0.5f) / f.Spacing), 0, f.Width - 1);
					int z = Mathf.Clamp(Mathf.RoundToInt((north + tiles.DepthMetres * 0.5f) / f.Spacing), 0, f.Depth - 1);
					return weight[z * f.Width + x];
				};
			}

			Func<float, float, bool> blocked = PointOfInterestTerrain.HoleQuery(terrains, tiles, CanyonWallClearMetres);
			if (Reserved.Count > 0)
			{
				Func<float, float, bool> holes = blocked, reserved = KeepOutQuery(Reserved);
				blocked = holes != null ? (x, z) => holes(x, z) || reserved(x, z) : reserved;
			}
			PointOfInterestPlanSettings settings = PointOfInterestPlanSettings.From(Settings);
			settings.Capital |= WantsCapital;
			return new PointOfInterestPlanInput
			{
				WidthMetres = tiles.WidthMetres,
				DepthMetres = tiles.DepthMetres,
				Ground = ground,
				SeaLevel = sea,
				HasSea = liquid != SurfaceLiquid.None,
				SeaIsWater = liquid == SurfaceLiquid.Water,
				Water = PointOfInterestWater.From(water),
				Biomes = biomes,
				BiomeAt = biomeAt,
				HardnessAt = hardness,
				PlateauAt = plateau,
				// Clear of the canyon walls' beds as well as their holes, and of what the cut has reserved.
				Blocked = blocked,
				Settings = settings,
				Rules = rules,
				Seed = SeedFor(Request),
			};
		}

		/// <summary>The planet's rock hardness under a scene position at an altitude, its columns read on a 32 m grid.</summary>
		private Func<float, float, float, float> Hardness(SolarSystemProfile system)
		{
			if (Request.Body == null)
			{
				return null;
			}
			PlanetGeology geology = PlanetGeology.For(Request, system);
			AtlasFootprint footprint = Request.Footprint;
			double radiusKm = Request.ResolvedRadiusKm;
			var cache = new Dictionary<long, GeologyColumn>();
			return (x, z, altitude) =>
			{
				int gx = Mathf.FloorToInt(x / 32f), gz = Mathf.FloorToInt(z / 32f);
				long key = ((long)gx << 32) ^ (uint)gz;
				if (!cache.TryGetValue(key, out GeologyColumn column))
				{
					Vector3 direction = AtlasGeometry.SceneToUnit(footprint, (gx + 0.5) * 0.032, (gz + 0.5) * 0.032, radiusKm).ToVector3();
					column = geology.ColumnAt(direction);
					cache[key] = column;
				}
				return column.HardnessAt(altitude);
			};
		}

		/// <summary>A seeded pick among the races with an affinity for the site's biome, in the kind's categories first.</summary>
		private static string PickRace(PointOfInterestRecord record, PointOfInterestKindRule rule)
		{
			var candidates = new List<PointOfInterestRaces.Candidate>();
			foreach ((RaceTemplate race, float weight) in RaceRegistry.RacesForBiome(record.BiomeID))
			{
				if (race != null)
				{
					candidates.Add(new PointOfInterestRaces.Candidate(race.NamingKey, race.Category, weight));
				}
			}
			return PointOfInterestRaces.Pick(candidates, rule != null ? rule.RaceCategories : null, record.SiteSeed);
		}

		private static string RaceCategoryOf(string race)
			=> !string.IsNullOrEmpty(race) && RaceRegistry.TryGet(race, out RaceTemplate template) ? template.Category : string.Empty;

		/// <summary>Finds the template objects a stored plan's records name.</summary>
		private void ResolveTemplates()
		{
			PointOfInterestTemplateLibrary library = PointOfInterestTemplateLibrary.Load();
			foreach (PointOfInterestRecord record in Plan.Records)
			{
				PointOfInterestTemplate template = library.Named(record.Kind, record.Template);
				if (template != null)
				{
					Templates[record.Id] = template;
				}
			}
		}

		/// <summary>"41 sites: 9 detected, 32 placed (12 built), 8 pads".</summary>
		public static string Summary(PointOfInterestPlan plan)
		{
			int detected = 0;
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (string.IsNullOrEmpty(record.Template) && PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Detected))
				{
					detected++;
				}
			}
			int built = 0;
			foreach (PointOfInterestRecord record in plan.Records)
			{
				if (!string.IsNullOrEmpty(record.Template))
				{
					built++;
				}
			}
			return $"{plan.Records.Count} site(s), {detected} found in the ground, {built} built from templates, {plan.Pads.Count} pad(s), {plan.KeepOuts.Count} keep-out(s)";
		}

		// ── Place ───────────────────────────────────────────────────

		/// <summary>
		/// Writes the plan into the scene: the POI asset (rewritten in place), the "Points of Interest" root with one
		/// <see cref="ScenePointOfInterest"/> per record, each shaper's and template's build, the registered site builders,
		/// and the POI prop set. Plans first if nothing has (a scene no biome painted).
		/// </summary>
		public ScenePointsOfInterest Place(Scene scene, TerrainTilePlan tiles, Terrain[,] terrains, SceneWater water, List<string> notes, List<string> wrote)
		{
			if (water != null && !water.Any)
			{
				water = null;
			}
			if (Plan == null)
			{
				PlanSites(scene, tiles, terrains, null, water, notes);
			}
			/* The last run's ridge and furrow out of the ground before the root that records it is replaced (a repaint and
			 * Regenerate POIs have taken it out already, before anything read the ground): the fields carve it again from the
			 * ground as it was (PointOfInterestFieldCarve), never on top of itself. */
			PointOfInterestFieldCarve.Restore(scene, terrains, notes);
			// And the last run's paddies' water (2026-10-18, I2i): each field lays its own again below.
			PointOfInterestPaddies.ClearWater(TerrainFolder, Request.SceneName);
			// The hot springs' pools the biome stage dug (2026-10-18, I2q): in the paddies' range, so cleared just now; written again.
			GeothermalGroundCarve.WriteWater(scene, TerrainFolder, Request.SceneName, notes);
			ScenePointsOfInterest asset = WriteAsset(wrote);
			GameObject root = EnsureRoot(scene, asset);

			Func<float, float, float> ground = (east, north) => SceneGenerator.GroundAltitude(terrains, tiles, east, north);
			Func<float, float, float> slope = (east, north) =>
			{
				float dx = ground(east + 1f, north) - ground(east - 1f, north), dz = ground(east, north + 1f) - ground(east, north - 1f);
				return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) * 0.5f) * Mathf.Rad2Deg;
			};
			Func<float, float, bool> skyAt = HolesAreSky ? PointOfInterestTerrain.HoleQuery(terrains, tiles) : null;
			var sink = new PointOfInterestPropSink();
			var placeContext = new PointOfInterestPlaceContext
			{
				Scene = scene,
				Request = Request,
				Tiles = tiles,
				Terrains = terrains,
				Water = water,
				Points = asset,
				TerrainFolder = TerrainFolder,
				GroundAt = ground,
				Notes = notes,
				Sink = sink,
			};
			// Every site's fields claim their ground as they are laid, so a later site's keep off them (2026-10-18, I2k).
			var fieldClaims = new PointOfInterestFieldClaims();

			foreach (PointOfInterestRecord record in asset.Points)
			{
				var site = new GameObject(string.IsNullOrWhiteSpace(record.Name) ? PointOfInterestKinds.Info(record.Kind).DisplayName : record.Name);
				site.transform.SetParent(root.transform, false);
				site.AddComponent<ScenePointOfInterest>().Apply(record);

				IPointOfInterestShaper shaper = PointOfInterestShapers.For(record.Kind);
				if (shaper != null)
				{
					placeContext.Root = site.transform;
					try
					{
						shaper.Place(placeContext, record);
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						notes?.Add($"{site.name}: its shaper threw while building: {ex.Message}");
					}
				}

				Templates.TryGetValue(record.Id, out PointOfInterestTemplate template);
				// The site's own biome: a site is small beside the biome field's blend, and a repaint has no field to ask.
				BiomeRegistry.TryGetByID(record.BiomeID, out BiomeTemplate siteBiome);
				// One list of the site's doors and one of its rooms, shared by every context made for it: the props feature
				// records them as it lays the buildings, the doors and interiors builders read them after. (A fresh pair per
				// context left the builders empty-handed: no door or room was ever built in a cut.)
				var siteDoors = new List<PointOfInterestSiteContext.PlacedDoor>();
				var siteRooms = new List<PointOfInterestSiteContext.PlacedRoom>();
				// And of every prop laid at it, for the builders that dress what the features laid (ivy on its walls).
				var siteLaid = new List<PointOfInterestSiteContext.PlacedProp>();
				PointOfInterestSiteContext Context(int salt) => new PointOfInterestSiteContext
				{
					Doors = siteDoors,
					Rooms = siteRooms,
					Laid = siteLaid,
					Scene = scene,
					Root = site.transform,
					Record = record,
					Template = template,
					Random = new DeterministicRNG((int)PointOfInterestPlanner.Mix(unchecked((uint)record.SiteSeed ^ (uint)(salt * 0x9E3779B1u)))),
					Points = asset,
					Request = Request,
					TerrainFolder = TerrainFolder,
					Water = water,
					GroundAt = ground,
					Terrains = terrains,
					Tiles = tiles,
					SkyAt = skyAt,
					SlopeAt = slope,
					BiomeAt = (east, north) => siteBiome,
					FieldClaims = fieldClaims,
					Notes = notes,
					Sink = sink,
				};
				if (template != null && template.Features != null)
				{
					for (int f = 0; f < template.Features.Count; f++)
					{
						PointOfInterestFeature feature = template.Features[f];
						if (feature == null)
						{
							continue;
						}
						try
						{
							feature.Build(Context(f + 1));
						}
						catch (Exception ex)
						{
							Debug.LogException(ex);
							notes?.Add($"{site.name}: feature {feature.GetType().Name} of '{template.name}' threw: {ex.Message}");
						}
					}
				}
				for (int b = 0; b < PointOfInterestSiteBuilders.All.Count; b++)
				{
					IPointOfInterestSiteBuilder builder = PointOfInterestSiteBuilders.All[b];
					if (!builder.Handles(record))
					{
						continue;
					}
					try
					{
						builder.Build(Context(1000 + b));
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						notes?.Add($"{site.name}: site builder {builder.GetType().Name} threw: {ex.Message}");
					}
				}
				// Every way in on the map (2026-10-20): a dungeon door always an entrance, each of a cave's mouths its own entry.
				try
				{
					PointOfInterestEntrances.Mark(Context(PointOfInterestEntrances.Salt));
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					notes?.Add($"{site.name}: marking its entrances threw: {ex.Message}");
				}
			}

			// Ships under way between the harbours and out to sea, solid on every peer (PointOfInterestSeaTraffic, FloatingBoats).
			try
			{
				PointOfInterestSeaTraffic.Build(root.transform, asset, Request, tiles.WidthMetres, tiles.DepthMetres, ground, water, notes);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				notes?.Add($"Sea traffic threw: {ex.Message}");
			}

			// The ways (with the streets the layouts just laid) baked for the client's path surface.
			// The boardwalks along the routed ways their biomes raise (2026-10-18, I2i; a settlement laid its streets' own).
			PathBoardwalks.LayWays(asset.Paths, sink, ground, PathBoardwalks.MapBiomes(TerrainFolder, Request.SceneName), unchecked((int)SceneGenerator.SceneSeed(Request)), notes);
			// The linear features but the fields' edges (2026-10-18, I2f): lanes' hedges and walls, woods' edges, small sites' bounds,
			// folds and the leats to the fields, in their biomes' styles; nothing (and nothing read) where no biome names one.
			try
			{
				PointOfInterestLinearFeatures.LayScene(scene, root.transform, asset, Request, TerrainFolder, terrains, tiles, water, ground, slope, fieldClaims, sink, notes);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				notes?.Add($"Linear features threw: {ex.Message}");
			}
			// The scatter's trees and bushes off the buildings' floors, as the grass is (before the trees are baked into props).
			PointOfInterestTreeClearance.Clear(terrains, asset.Paths, notes);
			ScenePathBake.Write(scene, root.transform, TerrainFolder, Request.SceneName, asset, wrote, notes);
			// And what the laid pieces give off (chimney smoke, fires, forges, lanterns), offered to the client.
			if (asset.Emitters != null && asset.Emitters.Count > 0)
			{
				var emitters = new GameObject(SceneSiteEmitters.ObjectName);
				emitters.transform.SetParent(root.transform, false);
				emitters.AddComponent<ClientOnlyObject>();
				emitters.AddComponent<SceneSiteEmitters>().Points = asset;
				notes?.Add($"Points of interest: {asset.Emitters.Count:N0} chimney(s), fire(s) and lamp(s) for smoke and light.");
			}
			EditorUtility.SetDirty(asset);

			if (sink.Props.Count > 0)
			{
				int collidable = ScenePropBaker.Write(scene, PointOfInterestGenerator.PropSource, sink.Prototypes, sink.Props);
				notes?.Add($"Points of interest: {sink.Props.Count:N0} prop(s) laid, {collidable:N0} collidable.");
			}
			else
			{
				ScenePropBaker.Clear(scene, PointOfInterestGenerator.PropSource);
			}
			PointOfInterestGenerator.RaisePlaced(scene, asset, notes);
			EditorUtility.SetDirty(root);
			return asset;
		}

		/// <summary>Writes the plan into the scene's POI asset, in place when it exists so its GUID survives.</summary>
		private ScenePointsOfInterest WriteAsset(List<string> wrote)
		{
			string path = AssetPath(TerrainFolder, Request.SceneName);
			var asset = AssetDatabase.LoadAssetAtPath<ScenePointsOfInterest>(path);
			bool created = asset == null;
			if (created)
			{
				asset = ScriptableObject.CreateInstance<ScenePointsOfInterest>();
			}
			Plan.WriteTo(asset, Request.SceneName);
			if (created)
			{
				WorldEditorAssets.EnsureFolder(TerrainFolder);
				AssetDatabase.CreateAsset(asset, path);
			}
			else
			{
				EditorUtility.SetDirty(asset);
			}
			wrote?.Add(path);
			return asset;
		}

		/// <summary>A fresh "Points of Interest" root (an old one is replaced) carrying the scene's settings and asset.</summary>
		private GameObject EnsureRoot(Scene scene, ScenePointsOfInterest asset)
		{
			foreach (GameObject existing in scene.GetRootGameObjects())
			{
				if (existing.name == PointOfInterestGenerator.RootName)
				{
					UnityEngine.Object.DestroyImmediate(existing);
				}
			}
			var root = new GameObject(PointOfInterestGenerator.RootName);
			SceneManager.MoveGameObjectToScene(root, scene);
			ScenePointOfInterestSettings settings = root.AddComponent<ScenePointOfInterestSettings>();
			settings.Settings = Settings;
			settings.Points = asset;
			return root;
		}
	}
}
#endif
