using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Core;
using FishMMO.Shared.NameGeneration;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The scene cut's housing step: plans the lots (<see cref="HousingPlotPlanner"/>), levels them and
	/// keeps trees and rocks off them, then lays a plot (the Housing module's <c>PlotFoundation</c>, laid through <see cref="PlotLayers"/>) per lot and writes the
	/// scene's <see cref="ScenePlotManifest"/>.
	/// </summary>
	/// <remarks>
	/// <para><b>Where it runs.</b> <see cref="Plan"/> runs in <c>SceneGenerator.PaintBiomes</c> straight
	/// after the points of interest are planned and before their keep-outs are read for the scatter:
	/// the only point where levelling the ground and keeping trees off still works. <see cref="Build"/>
	/// runs at the end of <see cref="PointOfInterestStage.Place"/>, on the finished ground.</para>
	///
	/// <para><b>Owned land never moves.</b> The previous manifest is read before a re-cut clears the old
	/// terrain folder (<see cref="ForCut"/>), and every lot in it is laid again at its old centre and
	/// floor whatever the new ground looks like. A repaint plans nothing new and levels nothing; it lays
	/// the manifest's lots again so the scatter keeps off them.</para>
	///
	/// <para>A plot is a scene object, not a prefab instance: a NetworkObject (with a SceneId made from
	/// its key, so a re-cut writes the same id), a small collider on the deed post, the foundation and
	/// its look — the same way the cut lays dungeon entrances.</para>
	/// </remarks>
	public sealed class HousingPlotStage
	{
		/// <summary>The scene root every plot is laid under; replaced whole each time.</summary>
		public const string RootName = "Housing Plots";

		/// <summary>Where the plot look's materials are kept.</summary>
		public const string MaterialFolder = "Assets/Prefabs/Shared/Entity/Interactables/Housing/Materials";

		/// <summary>Height of every plot volume, metres.</summary>
		public const float VolumeHeight = 12f;

		/// <summary>How far below the post (or the lot's floor, if lower) the plot volume starts, metres.</summary>
		public const float VolumeFloorDrop = 1f;

		/// <summary>How far a lot's levelling blends into the ground round it, metres.</summary>
		public const float SkirtMetres = 4f;

		/// <summary>How far in from its lot's front edge the deed post stands, metres.</summary>
		public const float PostInset = 1f;

		/// <summary>Habitats no lot stands in, beside open water (<see cref="BiomeHabitats.IsWater"/>): the water bits alone also
		/// mark land biomes' creatures (Forest is Forest | FreshWater), and refused every forest.</summary>
		private const BiomeHabitat Unbuildable = BiomeHabitat.Wetland | BiomeHabitat.Cave;

		private readonly string terrainFolder;
		private readonly string sceneName;
		private readonly int seed;
		private readonly List<ScenePlotManifest.Lot> previous = new List<ScenePlotManifest.Lot>();

		/// <summary>True for a repaint: the stored lots are laid again, nothing new is planned or levelled.</summary>
		public readonly bool Reused;

		/// <summary>The lots planned, once <see cref="Plan"/> has run.</summary>
		public HousingPlotPlanner.Result Planned { get; private set; }

		/// <summary>
		/// The ways the lots want, handed to the path network (<see cref="PathPlanInput.ExtraSpurs"/>): a cart track to each
		/// district's lane end, a footpath to each countryside group. Empty on a repaint, which lays no ways.
		/// </summary>
		public readonly List<PathSpurEnd> Spurs = new List<PathSpurEnd>();

		/// <summary>The keep-out id every lot's disc carries in the sites' plan: no site has it, so every way routes round lots.</summary>
		public const int LotKeepOutId = -1000000;

		/// <summary>The first spur id; each spur takes the next one down.</summary>
		public const int SpurIdBase = -2000000;

		private HousingPlotStage(string terrainFolder, string sceneName, int seed, bool reused)
		{
			this.terrainFolder = terrainFolder;
			this.sceneName = sceneName;
			this.seed = seed;
			Reused = reused;
		}

		/// <summary>The manifest's path for a scene's terrain folder.</summary>
		public static string ManifestPath(string terrainFolder, string sceneName)
			=> ScenePlotManifest.AssetPath(terrainFolder, WorldEditorAssets.Sanitize(sceneName));

		/// <summary>The stage for a cut: reads the previous manifest now, before a re-cut deletes it.</summary>
		public static HousingPlotStage ForCut(SceneGenerationRequest request, string terrainFolder)
		{
			// No module lays plots (Housing not installed): no lots, no spurs, no posts.
			if (PlotLayers.Active == null)
			{
				return null;
			}
			var stage = new HousingPlotStage(terrainFolder, request.SceneName, SeedFor(request), false);
			stage.ReadPrevious();
			return stage;
		}

		/// <summary>The stage for a repaint: the stored lots only.</summary>
		public static HousingPlotStage ForRepaint(SceneGenerationRequest request, string terrainFolder)
		{
			// No module lays plots (Housing not installed): no lots, no spurs, no posts.
			if (PlotLayers.Active == null)
			{
				return null;
			}
			var stage = new HousingPlotStage(terrainFolder, request.SceneName, SeedFor(request), true);
			stage.ReadPrevious();
			return stage;
		}

		private static int SeedFor(SceneGenerationRequest request)
			=> (int)(PointOfInterestPlanner.Mix(SceneGenerator.SceneSeed(request) ^ 0x484F5553u) & 0x7FFFFFFFu);

		private void ReadPrevious()
		{
			ScenePlotManifest manifest = AssetDatabase.LoadAssetAtPath<ScenePlotManifest>(ManifestPath(terrainFolder, sceneName));
			if (manifest != null && manifest.Format == ScenePlotManifest.CurrentFormat && manifest.Lots != null)
			{
				previous.AddRange(manifest.Lots);
			}
		}

		// ── Plan ────────────────────────────────────────────────────

		/// <summary>
		/// Plans the lots on the ground the sites left, levels them, and reserves them (and the
		/// district lanes) so the scatter keeps trees and rocks off. Call before the stage's keep-outs
		/// are read.
		/// </summary>
		public void Plan(PointOfInterestStage poi, TerrainTilePlan tiles, Terrain[,] terrains, SceneBiomeField field, SceneWater water, List<string> notes)
		{
			if (poi == null || terrains == null)
			{
				return;
			}
			int sampled = 0, refusedSamples = 0;
			var input = new HousingPlotPlanner.Input
			{
				Seed = seed,
				HalfWidth = tiles.WidthMetres * 0.5f,
				HalfDepth = tiles.DepthMetres * 0.5f,
				GroundAt = (x, z) => SceneGenerator.GroundAltitude(terrains, tiles, x, z),
				Kept = new List<ScenePlotManifest.Lot>(previous),
				Districts = !Reused,
				Countryside = !Reused,
			};
			if (!Reused)
			{
				Func<float, float, bool> blocked = BlockedQuery(poi, tiles, terrains, field, water);
				input.Blocked = (x, z) =>
				{
					sampled++;
					bool refused = blocked(x, z);
					if (refused)
					{
						refusedSamples++;
					}
					return refused;
				};
				FillSites(input, poi.Plan?.Records);
			}

			HousingPlotPlanner.Result planned = HousingPlotPlanner.Plan(input);
			Planned = planned;

			if (!Reused)
			{
				/* Into the sites' own keep-outs (stored with them): the ways route round every lot, the wayside sites keep off
				 * them, and the wildlife and the scatter read them like any site's. The lanes only keep trees off (Reserved): a
				 * district's way ends on its lane. */
				foreach (ScenePlotManifest.Lot lot in planned.Lots)
				{
					poi.Plan?.KeepOuts.Add(new PointOfInterestKeepOut
					{
						Id = LotKeepOutId,
						X = lot.Centre.x,
						Z = lot.Centre.z,
						Radius = lot.Size.x * 0.7072f + 1.5f,
						Outline = Array.Empty<float>(),
					});
				}
				Spurs.Clear();
				List<HousingPlotPlanner.SpurEnd> ends = HousingPlotPlanner.SpurEnds(planned.Lots, input.Settlements);
				for (int i = 0; i < ends.Count; i++)
				{
					Vector2 at = ends[i].Position;
					Spurs.Add(new PathSpurEnd
					{
						Id = SpurIdBase - i,
						Position = new Vector3(at.x, SceneGenerator.GroundAltitude(terrains, tiles, at.x, at.y), at.y),
						Way = ends[i].District ? ScenePathClass.CartTrack : ScenePathClass.Footpath,
					});
				}
				int levelled = Level(terrains, tiles, planned.Lots);
				string note = $"Housing: {planned.Lots.Count} lot(s) — {planned.Kept} kept from the last cut, {planned.NewDistrictLots} new in {planned.NewDistricts} district(s), {planned.NewCountryLots} new in the countryside; {input.Settlements.Count} city/capital site(s); {refusedSamples}/{sampled} ground sample(s) refused as water, sites, ways or habitat; {levelled} height sample(s) levelled.";
				notes?.Add(note);
				// The batch re-cut does not print the cut's notes; this line is how a headless run reads the count.
				Debug.Log($"[Housing plots] '{sceneName}' {note}");
			}
			foreach (HousingPlotPlanner.Disc disc in planned.Clear)
			{
				poi.Reserved.Add(new PointOfInterestKeepOut { Id = -1, X = disc.X, Z = disc.Z, Radius = disc.Radius, Outline = Array.Empty<float>() });
			}
		}

		/// <summary>
		/// Levels the lots' footprints again, after the ways were carved: a way carved beside a lot may have cut its edge.
		/// Nothing on a repaint, which levels nothing.
		/// </summary>
		public void RelevelFootprints(Terrain[,] terrains, TerrainTilePlan tiles)
		{
			if (Reused || Planned == null || terrains == null)
			{
				return;
			}
			foreach (ScenePlotManifest.Lot lot in Planned.Lots)
			{
				float hx = lot.Size.x * 0.5f, hz = lot.Size.y * 0.5f, floor = lot.Centre.y;
				PointOfInterestTerrain.EditHeights(terrains, tiles,
					Rect.MinMaxRect(lot.Centre.x - hx, lot.Centre.z - hz, lot.Centre.x + hx, lot.Centre.z + hz), (x, z, metres) => floor);
			}
		}

		/// <summary>True where no lot may stand: sites, ways, pads, holes, water, and unbuildable biomes.</summary>
		private static Func<float, float, bool> BlockedQuery(PointOfInterestStage poi, TerrainTilePlan tiles, Terrain[,] terrains, SceneBiomeField field, SceneWater water)
		{
			Func<float, float, bool> kept = poi.Excludes;
			Func<float, float, bool> avoided = poi.Avoided.Count > 0 ? PointOfInterestStage.KeepOutQuery(poi.Avoided) : null;
			Func<float, float, bool> hole = PointOfInterestTerrain.HoleQuery(terrains, tiles, 4f);
			List<PointOfInterestPad> pads = poi.Plan != null ? poi.Plan.Pads : null;
			float sea = water != null && !float.IsNegativeInfinity(water.SeaLevel) ? water.SeaLevel + 1.5f : float.NegativeInfinity;
			return (x, z) =>
			{
				if ((kept != null && kept(x, z)) || (avoided != null && avoided(x, z)) || (hole != null && hole(x, z)))
				{
					return true;
				}
				if (pads != null)
				{
					for (int i = 0; i < pads.Count; i++)
					{
						if (pads[i].Contains(x, z))
						{
							return true;
						}
					}
				}
				if (water != null)
				{
					if (!float.IsNegativeInfinity(water.RiverSurfaceNear(x, z)) || !float.IsNegativeInfinity(water.LakeSurfaceNear(x, z)))
					{
						return true;
					}
					if (!float.IsNegativeInfinity(sea) && SceneGenerator.GroundAltitude(terrains, tiles, x, z) < sea)
					{
						return true;
					}
				}
				if (field != null)
				{
					BiomeTemplate biome = field.DominantAt(x, z);
					if (biome != null && ((biome.LiveHabitat & Unbuildable) != 0 || BiomeHabitats.IsWater(biome.LiveHabitat)))
					{
						return true;
					}
				}
				return false;
			};
		}

		/// <summary>The sites the countryside keeps away from, and the settlements that get districts.</summary>
		private static void FillSites(HousingPlotPlanner.Input input, IReadOnlyList<PointOfInterestRecord> records)
		{
			if (records == null)
			{
				return;
			}
			foreach (PointOfInterestRecord record in records)
			{
				if (record == null)
				{
					continue;
				}
				input.Sites.Add(new HousingPlotPlanner.Disc { X = record.Position.x, Z = record.Position.z, Radius = Mathf.Max(0f, record.Radius) });
				if (record.Kind == POIType.City || record.Kind == POIType.Capital)
				{
					input.Settlements.Add(new HousingPlotPlanner.Settlement
					{
						Id = record.Id,
						Name = record.Name,
						X = record.Position.x,
						Z = record.Position.z,
						Radius = Mathf.Max(0f, record.Radius),
						Capital = record.Kind == POIType.Capital,
					});
				}
			}
		}

		/// <summary>
		/// Levels every lot to its floor and blends a skirt round it into the ground. Skirts first, then
		/// every footprint, so a neighbour's skirt never tilts a lot already levelled.
		/// </summary>
		public static int Level(Terrain[,] terrains, TerrainTilePlan tiles, IReadOnlyList<ScenePlotManifest.Lot> lots)
		{
			int changed = 0;
			foreach (ScenePlotManifest.Lot lot in lots)
			{
				float hx = lot.Size.x * 0.5f, hz = lot.Size.y * 0.5f, floor = lot.Centre.y;
				float cx = lot.Centre.x, cz = lot.Centre.z;
				var area = Rect.MinMaxRect(cx - hx - SkirtMetres, cz - hz - SkirtMetres, cx + hx + SkirtMetres, cz + hz + SkirtMetres);
				changed += PointOfInterestTerrain.EditHeights(terrains, tiles, area, (x, z, metres) =>
				{
					float dx = Mathf.Max(0f, Mathf.Abs(x - cx) - hx), dz = Mathf.Max(0f, Mathf.Abs(z - cz) - hz);
					float d = Mathf.Sqrt(dx * dx + dz * dz);
					if (d >= SkirtMetres)
					{
						return metres;
					}
					float t = Mathf.SmoothStep(0f, 1f, d / SkirtMetres);
					return Mathf.Lerp(floor, metres, t);
				});
			}
			foreach (ScenePlotManifest.Lot lot in lots)
			{
				float hx = lot.Size.x * 0.5f, hz = lot.Size.y * 0.5f, floor = lot.Centre.y;
				var area = Rect.MinMaxRect(lot.Centre.x - hx, lot.Centre.z - hz, lot.Centre.x + hx, lot.Centre.z + hz);
				changed += PointOfInterestTerrain.EditHeights(terrains, tiles, area, (x, z, metres) => floor);
			}
			return changed;
		}

		// ── Build ───────────────────────────────────────────────────

		/// <summary>Lays the planned lots into the scene and writes the manifest.</summary>
		public void Build(Scene scene, List<string> notes, List<string> wrote)
		{
			/* A plan that never ran (it threw, or this stage was never handed to the paint) still lays the
			 * stored lots and writes them back: a re-cut has already deleted the old manifest with the old
			 * terrain folder, and the lots in it are land players may own. */
			List<ScenePlotManifest.Lot> lots = Planned != null ? Planned.Lots : previous;
			if (lots.Count == 0)
			{
				// Nothing stored and nothing planned: leave whatever the scene holds alone.
				return;
			}
			int laid = Lay(scene, lots, null, notes);
			string path = WriteManifest(terrainFolder, sceneName, lots);
			if (path != null)
			{
				wrote?.Add(path);
			}
			if (laid > 0 && Reused)
			{
				notes?.Add($"Housing: {laid} stored lot(s) laid again.");
			}
		}

		/// <summary>Writes (or overwrites) a scene's manifest; its path, or null when nothing was written.</summary>
		public static string WriteManifest(string terrainFolder, string sceneName, IReadOnlyList<ScenePlotManifest.Lot> lots)
		{
			if (string.IsNullOrEmpty(terrainFolder))
			{
				return null;
			}
			string path = ManifestPath(terrainFolder, sceneName);
			ScenePlotManifest manifest = AssetDatabase.LoadAssetAtPath<ScenePlotManifest>(path);
			if (manifest == null)
			{
				manifest = ScriptableObject.CreateInstance<ScenePlotManifest>();
				AssetDatabase.CreateAsset(manifest, path);
			}
			manifest.Format = ScenePlotManifest.CurrentFormat;
			manifest.Lots = new List<ScenePlotManifest.Lot>(lots);
			EditorUtility.SetDirty(manifest);
			AssetDatabase.SaveAssetIfDirty(manifest);
			return path;
		}

		/// <summary>
		/// Replaces the scene's plot root with one plot per lot, grouped by the lot's group. The deed
		/// post stands on the ground at <paramref name="groundAt"/> when given (the tool, which levels
		/// nothing), else on the lot's floor (the cut, which levelled it).
		/// </summary>
		public static int Lay(Scene scene, IReadOnlyList<ScenePlotManifest.Lot> lots, Func<float, float, float> groundAt, List<string> notes)
		{
			foreach (GameObject existing in scene.GetRootGameObjects())
			{
				if (existing.name == RootName)
				{
					UnityEngine.Object.DestroyImmediate(existing);
				}
			}
			if (lots == null || lots.Count == 0)
			{
				return 0;
			}

			IPlotLayer layer = PlotLayers.Active;
			if (layer == null)
			{
				return 0;
			}

			var root = new GameObject(RootName);
			SceneManager.MoveGameObjectToScene(root, scene);
			var groups = new Dictionary<string, Transform>();
			int laid = 0;
			foreach (ScenePlotManifest.Lot lot in lots)
			{
				string groupName = string.IsNullOrEmpty(lot.Group) ? (lot.District ? "District" : "Countryside") : lot.Group;
				if (!groups.TryGetValue(groupName, out Transform group))
				{
					group = new GameObject(groupName).transform;
					group.SetParent(root.transform, false);
					groups[groupName] = group;
				}

				Vector3 front = HousingPlotPlanner.FrontDirection(lot.Front);
				float halfAlong = Mathf.Abs(front.x) > 0.5f ? lot.Size.x * 0.5f : lot.Size.y * 0.5f;
				Vector3 post = lot.Centre + front * (halfAlong - PostInset);
				post.y = groundAt != null ? groundAt(post.x, post.z) : lot.Centre.y;

				var plot = new GameObject("Housing Plot");
				plot.transform.SetParent(group, false);
				plot.transform.SetPositionAndRotation(post, Quaternion.LookRotation(front, Vector3.up));
				NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(plot);
				BoxCollider box = plot.AddComponent<BoxCollider>();
				box.center = new Vector3(0.2f, 1.2f, 0f);
				box.size = new Vector3(0.9f, 2.4f, 0.4f);

				// The volume starts below the post (and below the lot's floor), so ground that dips under the post still counts.
				Vector3 offset = lot.Centre - post;
				offset.y = Mathf.Min(0f, lot.Centre.y - post.y) - VolumeFloorDrop;
				Component[] dressed = layer.Lay(plot, lot.Key, lot.Price, lot.Size, VolumeHeight + VolumeFloorDrop, offset, EnsureMaterial);

				PointOfInterestFeatureKit.AssignSceneId(networkObject, StableHash.FNV32("plot:" + lot.Key));
				foreach (Component component in dressed)
				{
					EditorUtility.SetDirty(component);
				}
				laid++;
			}
			EditorSceneManager.MarkSceneDirty(scene);
			return laid;
		}

		private static Material EnsureMaterial(string name, Color colour)
		{
			string path = $"{MaterialFolder}/{name}.mat";
			Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material != null)
			{
				return material;
			}
			EnsureFolder(MaterialFolder);
			Shader shader = Shader.Find("FishMMO/Weather Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
			material = new Material(shader) { name = name };
			material.SetColor("_BaseColor", colour);
			if (material.HasProperty("_Smoothness"))
			{
				material.SetFloat("_Smoothness", 0.1f);
			}
			AssetDatabase.CreateAsset(material, path);
			return material;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
		}
	}
}
