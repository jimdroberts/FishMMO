#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Carves the cave kinds (cave, grotto, sea cave, ice cave, lava tube) into the ground: plans the tunnel
	/// (<see cref="CaveSolid"/>) and holes the terrain at its mouth, then builds its rock shell as a collider on the Ground
	/// layer with a client-only visual, like the canyon walls (<see cref="CanyonWalls"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Carved, not placed</b> (Jim, 2026-10-10: "carve our own"). Unity's terrain has no caves, only holes, so the
	/// tunnel is a mesh of our own and the terrain is holed where it opens. Small caves are walk-in grottos and chambers on
	/// the surface scene; a large cave's chamber is where its dungeon's entrance goes (<see cref="TryChamber"/>), the
	/// dungeon itself a hand-made scene on another atlas layer.
	/// </para>
	/// <para>
	/// <b>A scene object, not a prop.</b> Props share one prefab's mesh between instances (<see cref="ScenePropBaker"/>);
	/// every cave is its own mesh. Its meshes are sub-assets of one asset per scene,
	/// <c>&lt;Scene&gt; Caves.asset</c> beside the terrain, rewritten in place (so a repaint keeps every reference) and
	/// pruned of caves the scene no longer has. The collider is on the cave's root, which the server keeps and the NavMesh
	/// bake collects (its floor is under 15° all the way to the chamber); the renderers are under a
	/// <see cref="ClientOnlyObject"/>, which server builds strip.
	/// </para>
	/// <para>
	/// <b>Plan once, build every time.</b> <see cref="Plan"/> runs on a cut (it holes the terrain, which a repaint must not
	/// do again) and stores the walk as a <see cref="PointOfInterestShape"/>; <see cref="Place"/> builds the same shell
	/// from it on every cut and repaint. The ground the shell's lip follows is read again at place time: the talus cones
	/// the cliffs raise afterwards keep out of the cave's footprint.
	/// </para>
	/// </remarks>
	public sealed class CaveShaper : IPointOfInterestShaper
	{
		public static readonly CaveShaper Instance = new CaveShaper();

		/// <summary>The shape name a cave's walk is stored under.</summary>
		public const string ShaperName = "Cave";

		/// <summary>The sea's surface, world y: generated scenes put it at 0 (fishmmo-world-scale).</summary>
		public const float SeaLevel = 0f;

		public bool Handles(POIType kind) => TryForm(kind, out _);

		/// <summary>The cave form a kind is carved as.</summary>
		public static bool TryForm(POIType kind, out CaveForm form)
		{
			switch (kind)
			{
				case POIType.Cave: form = CaveForm.Cave; return true;
				case POIType.Grotto: form = CaveForm.Grotto; return true;
				case POIType.SeaCave: form = CaveForm.SeaCave; return true;
				case POIType.IceCave: form = CaveForm.IceCave; return true;
				case POIType.LavaTube: form = CaveForm.LavaTube; return true;
				default: form = CaveForm.Cave; return false;
			}
		}

		/// <summary>A cave's own seed, from its site's.</summary>
		public static int SeedOf(PointOfInterestRecord site) => (int)(PointOfInterestPlanner.Mix(unchecked((uint)site.SiteSeed ^ 0xCA7E5EEDu)) & 0x7FFFFFFFu);

		// ── Plan ──────────────────────────────────────────────────

		public void Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			TryForm(site.Kind, out CaveForm form);
			TerrainTilePlan tiles = context.Tiles;
			CaveGround ground = SampleGround(context.GroundAt, tiles, site.Position.x, site.Position.z, CaveSolid.ProfileFor(form, site.SizeClass).LengthMax + 40f);
			CaveSolid solid = CaveSolid.Plan(form, site.SizeClass, site.Position, site.Yaw, SeedOf(site), ground, SeaLevel, out string problem);
			if (solid == null)
			{
				context.Reject(site, problem);
				return;
			}
			if (!HoleGrid(context.Terrains, tiles, out float step, out float gridX, out float gridZ))
			{
				context.Reject(site, "the scene has no terrain to open it in");
				return;
			}
			List<Vector2Int> cells = solid.HoleCells(context.GroundAt, step, gridX, gridZ);
			CutHoles(context.Terrains, tiles, cells, step, gridX, gridZ);

			// Nothing grows, falls or is stood in the mouth or on the lip round it.
			float yr = solid.Yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
			Vector3 front = solid.Origin + outward * solid.Radius;
			context.AddKeepOut(site, front.x, front.z, Mathf.Max(site.Radius, 1.5f * solid.Radius + solid.Collar));
			context.AddShape(ShapeOf(site, solid));
		}

		/// <summary>The stored form of a planned cave.</summary>
		public static PointOfInterestShape ShapeOf(PointOfInterestRecord site, CaveSolid solid)
			=> new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = solid.Origin,
				Yaw = solid.Yaw,
				Size = new Vector3(2f * solid.Radius, (1f + CaveSolid.FloorDepth) * solid.Radius * solid.Flat, solid.Length),
				Seed = solid.Seed,
				Values = solid.ToValues(),
			};

		/// <summary>
		/// The ground within <paramref name="reach"/> of (x, z), on the heightmap's own samples (the tiles' grid), so the
		/// mesher reads back exactly the terrain's interpolation.
		/// </summary>
		public static CaveGround SampleGround(System.Func<float, float, float> groundAt, TerrainTilePlan tiles, float x, float z, float reach)
			=> SampleGround(groundAt, tiles, x - reach, z - reach, x + reach, z + reach);

		public static CaveGround SampleGround(System.Func<float, float, float> groundAt, TerrainTilePlan tiles, float minX, float minZ, float maxX, float maxZ)
		{
			float spacing = tiles.Resolution > 1 ? tiles.MetresPerSample : 1f;
			return CaveGround.Sample(groundAt, minX, minZ, maxX, maxZ, spacing, -0.5f * tiles.WidthMetres, -0.5f * tiles.DepthMetres);
		}

		/// <summary>The terrain's hole grid: one cell per heightmap quad, lines from the scene's south-west corner.</summary>
		public static bool HoleGrid(Terrain[,] terrains, TerrainTilePlan tiles, out float step, out float gridX, out float gridZ)
		{
			step = 1f;
			gridX = -0.5f * tiles.WidthMetres;
			gridZ = -0.5f * tiles.DepthMetres;
			foreach (Terrain terrain in terrains)
			{
				if (terrain != null && terrain.terrainData != null)
				{
					step = tiles.TileMetres / Mathf.Max(1, terrain.terrainData.holesResolution);
					return true;
				}
			}
			return false;
		}

		/// <summary>Holes the terrain's quads in <paramref name="cells"/> (from <see cref="CaveSolid.HoleCells"/>).</summary>
		public static void CutHoles(Terrain[,] terrains, TerrainTilePlan tiles, List<Vector2Int> cells, float step, float gridX, float gridZ)
		{
			if (cells.Count == 0)
			{
				return;
			}
			var set = new HashSet<long>();
			int i0 = int.MaxValue, i1 = int.MinValue, k0 = int.MaxValue, k1 = int.MinValue;
			foreach (Vector2Int c in cells)
			{
				set.Add(((long)c.x << 32) ^ (uint)c.y);
				i0 = Mathf.Min(i0, c.x);
				i1 = Mathf.Max(i1, c.x);
				k0 = Mathf.Min(k0, c.y);
				k1 = Mathf.Max(k1, c.y);
			}
			var area = new Rect(gridX + i0 * step, gridZ + k0 * step, (i1 - i0 + 1) * step, (k1 - k0 + 1) * step);
			PointOfInterestTerrain.CutHoles(terrains, tiles, area, (east, north) =>
			{
				int i = Mathf.FloorToInt((east - gridX) / step), k = Mathf.FloorToInt((north - gridZ) / step);
				return set.Contains(((long)i << 32) ^ (uint)k);
			});
		}

		// ── Place ─────────────────────────────────────────────────

		public void Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			CaveSolid solid = SolidOf(context.Points, site.Id);
			if (solid == null)
			{
				context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}): no cave is stored for it; re-cut the scene.");
				return;
			}
			Bounds extent = solid.Extent;
			CaveGround ground = SampleGround(context.GroundAt, context.Tiles, solid.Origin.x + extent.min.x - 4f, solid.Origin.z + extent.min.z - 4f,
				solid.Origin.x + extent.max.x + 4f, solid.Origin.z + extent.max.z + 4f);
			MeshBuilder[] levels;
			try
			{
				levels = solid.BuildMeshes(ground, out _);
			}
			catch (System.InvalidOperationException ex)
			{
				context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}): its shell would not mesh ({ex.Message}); the terrain stays holed.");
				return;
			}
			string assetPath = string.IsNullOrEmpty(context.TerrainFolder) || context.Request == null ? null
				: AssetPath(context.TerrainFolder, context.Request.SceneName);
			Mesh[] meshes = WriteMeshes(assetPath, site.Id, levels, LiveCaves(context.Points));

			string rock = solid.Form == CaveForm.LavaTube ? "Basalt"
				: solid.Form == CaveForm.IceCave ? CliffRocks.Ice
				: PointOfInterestRock.At(context.Request, site, solid.Origin);
			Material material = PointOfInterestRock.MaterialOf(rock);
			if (material == null)
			{
				context.Notes?.Add($"Caves: no material for {rock} ('{CliffRocks.MaterialName(rock)}'); run Generate Biome Art, then repaint.");
			}
			Build(context.Root, solid, meshes, material);
		}

		/// <summary>The scene's cave mesh asset: <c>&lt;Scene&gt; Caves.asset</c> in its terrain folder.</summary>
		public static string AssetPath(string terrainFolder, string sceneName) => $"{terrainFolder}/{WorldEditorAssets.Sanitize(sceneName)} Caves.asset";

		/// <summary>The cave stored for a site, or null.</summary>
		public static CaveSolid SolidOf(ScenePointsOfInterest points, int siteId)
		{
			if (points == null || points.Shapes == null)
			{
				return null;
			}
			foreach (PointOfInterestShape shape in points.Shapes)
			{
				if (shape != null && shape.SiteId == siteId && shape.Shaper == ShaperName)
				{
					return CaveSolid.FromValues(shape.Values, shape.Position, shape.Seed);
				}
			}
			return null;
		}

		/// <summary>
		/// Where a cave's chamber floor is (world), how far from it the floor reaches and which way the chamber runs: the spot
		/// a camp, a nest, a shrine or a large cave's dungeon entrance stands on. False for a site that is not a carved cave.
		/// </summary>
		public static bool TryChamber(ScenePointsOfInterest points, int siteId, out Vector3 floor, out float radius, out float yaw)
		{
			CaveSolid solid = SolidOf(points, siteId);
			floor = solid != null ? solid.ChamberFloorWorld : Vector3.zero;
			radius = solid != null ? solid.ChamberFloorRadius : 0f;
			yaw = solid != null ? solid.ChamberYaw : 0f;
			return solid != null;
		}

		private static HashSet<int> LiveCaves(ScenePointsOfInterest points)
		{
			var live = new HashSet<int>();
			if (points != null)
			{
				foreach (PointOfInterestRecord record in points.Points)
				{
					if (record != null && TryForm(record.Kind, out _))
					{
						live.Add(record.Id);
					}
				}
			}
			return live;
		}

		/// <summary>
		/// A cave's levels as meshes: sub-assets of <paramref name="assetPath"/> named <c>Cave &lt;id&gt; LOD&lt;n&gt;</c>, an
		/// existing one overwritten in place (its references survive a repaint), and the meshes of caves no longer in
		/// <paramref name="live"/> removed. In memory only when the path is null (tests, renders).
		/// </summary>
		public static Mesh[] WriteMeshes(string assetPath, int siteId, MeshBuilder[] levels, ICollection<int> live)
		{
			var meshes = new Mesh[levels.Length];
			for (int l = 0; l < levels.Length; l++)
			{
				meshes[l] = levels[l].ToMesh($"Cave {siteId} LOD{l}");
			}
			if (assetPath == null)
			{
				return meshes;
			}
			// The main object is a placeholder that never goes, so any cave's meshes can be added and removed under it.
			var container = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (container == null)
			{
				WorldEditorAssets.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));
				container = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(assetPath) };
				AssetDatabase.CreateAsset(container, assetPath);
			}
			var existing = new Dictionary<string, Mesh>();
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
			{
				if (asset is Mesh mesh && asset != container)
				{
					if (CaveIdOf(mesh.name, out int id) && live != null && id != siteId && !live.Contains(id))
					{
						AssetDatabase.RemoveObjectFromAsset(mesh);
						Object.DestroyImmediate(mesh, true);
						continue;
					}
					existing[mesh.name] = mesh;
				}
			}
			for (int l = 0; l < meshes.Length; l++)
			{
				if (existing.TryGetValue(meshes[l].name, out Mesh old))
				{
					EditorUtility.CopySerialized(meshes[l], old);
					Object.DestroyImmediate(meshes[l]);
					meshes[l] = old;
				}
				else
				{
					AssetDatabase.AddObjectToAsset(meshes[l], assetPath);
				}
			}
			EditorUtility.SetDirty(container);
			AssetDatabase.SaveAssetIfDirty(container);
			return meshes;
		}

		private static bool CaveIdOf(string name, out int id)
		{
			id = 0;
			const string prefix = "Cave ";
			if (name == null || !name.StartsWith(prefix, System.StringComparison.Ordinal))
			{
				return false;
			}
			int end = name.IndexOf(' ', prefix.Length);
			return end > prefix.Length && int.TryParse(name.Substring(prefix.Length, end - prefix.Length), out id);
		}

		/// <summary>
		/// The cave's objects under a site: the shell's collider on the Ground layer at the mouth (the server keeps it),
		/// its renderers by level under a client-only "Visual" child, and an empty "Chamber" marker on the chamber's floor.
		/// </summary>
		public static GameObject Build(Transform parent, CaveSolid solid, Mesh[] meshes, Material material)
		{
			int layer = CliffPlacer.ColliderLayer;
			var cave = new GameObject("Cave Shell") { layer = layer };
			cave.transform.SetParent(parent, false);
			cave.transform.SetPositionAndRotation(solid.Origin, Quaternion.identity);
			GameObjectUtility.SetStaticEditorFlags(cave, CliffPlacer.Flags);
			var collider = cave.AddComponent<MeshCollider>();
			collider.convex = false;
			collider.sharedMesh = meshes[0];

			var visual = new GameObject(CliffPlacer.VisualName) { layer = layer };
			visual.transform.SetParent(cave.transform, false);
			GameObjectUtility.SetStaticEditorFlags(visual, CliffPlacer.Flags);
			visual.AddComponent<ClientOnlyObject>();
			var lods = new LOD[meshes.Length];
			for (int l = 0; l < meshes.Length; l++)
			{
				var child = new GameObject("LOD" + l) { layer = layer };
				child.transform.SetParent(visual.transform, false);
				GameObjectUtility.SetStaticEditorFlags(child, CliffPlacer.Flags);
				child.AddComponent<MeshFilter>().sharedMesh = meshes[l];
				var renderer = child.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = ShadowCastingMode.On;
				renderer.receiveShadows = true;
				lods[l] = new LOD(CliffRocks.LodHeights[Mathf.Min(l, CliffRocks.LodHeights.Length - 1)], new Renderer[] { renderer });
			}
			LODGroup group = visual.AddComponent<LODGroup>();
			group.SetLODs(lods);
			group.RecalculateBounds();

			var chamber = new GameObject("Chamber");
			chamber.transform.SetParent(parent, false);
			chamber.transform.SetPositionAndRotation(solid.ChamberFloorWorld, Quaternion.Euler(0f, solid.ChamberYaw, 0f));
			return cave;
		}
	}

	/// <summary>Registers the terrain shapers (caves, overhangs and arches) with the point-of-interest stage.</summary>
	[InitializeOnLoad]
	public static class PointOfInterestTerrainShapers
	{
		static PointOfInterestTerrainShapers() => Register();

		/// <summary>Registers both (again harmlessly): for code that runs before the editor's load hook, such as a test.</summary>
		public static void Register()
		{
			PointOfInterestShapers.Register(CaveShaper.Instance);
			PointOfInterestShapers.Register(OverhangShaper.Instance);
		}
	}
}
#endif
