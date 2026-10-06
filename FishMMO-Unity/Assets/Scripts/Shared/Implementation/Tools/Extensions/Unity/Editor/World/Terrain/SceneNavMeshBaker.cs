#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes a generated scene's NavMesh: its terrain and colliders as Unity collects them, and its baked props'
	/// collision (<see cref="ScenePropCollisionSet"/>) fed to the builder directly, since props have no colliders
	/// until <see cref="PropColliderStreamer"/> places them at run time. Small props have no collision, so NPCs path
	/// through bushes as players walk through them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A <see cref="NavMeshSurface"/> like the hand-made scenes'.</b> Physics colliders, a height mesh (without one
	/// an agent stands on the coarse polygons, centimetres to a metre off the ground: see the NavMesh audit), the
	/// default agent. The data is written beside the terrain and assigned to the surface for authoring; builds and
	/// play mode strip the surface, and the scene server loads the data from <c>SceneNavMeshCatalogue</c>, which the
	/// bake updates.
	/// </para>
	/// <para>
	/// <b>Voxels.</b> A generated scene is tens of square kilometres; at the default 1/6 m voxel its bake is enormous.
	/// <see cref="VoxelMetres"/> is a third of a metre: still finer than an agent's radius, a quarter of the work.
	/// </para>
	/// </remarks>
	public static class SceneNavMeshBaker
	{
		/// <summary>The name of the NavMesh surface's object, at the scene's root.</summary>
		public const string ObjectName = "NavMesh";

		/// <summary>Voxel size for a generated scene's bake, metres.</summary>
		public const float VoxelMetres = 1f / 3f;

		/// <summary>Water deeper than this is deep water: NPCs wade a ford; only swimmers cross the rest.</summary>
		public const float WadeMetres = 1f;

		/// <summary>The NavMesh area deep water is baked as (ProjectSettings' NavMesh areas): walkable, but only by swimmers' agents.</summary>
		public const string DeepWaterArea = "Deep Water";

		/// <summary>The NavMesh area molten lava is baked as: walkable only by agents that can swim in it (fire-immune).</summary>
		public const string LavaArea = "Lava";

		/// <summary>How far under the surface a deep-water box stops: anything standing out of the water (a floe, a berg, a pier, a bridge) stays plain walkable.</summary>
		public const float BoxTopUnderSurfaceMetres = 0.5f;

		/// <summary>The grid the water is read on, metres.</summary>
		public const float WaterCellMetres = 4f;

		/// <summary>
		/// Bakes <paramref name="scene"/>'s NavMesh with its terrain, colliders and baked props. Returns false when it has no
		/// ground. <paramref name="heightMesh"/> and <paramref name="voxelMetres"/> change the surface's own settings first.
		/// With <paramref name="surfaceAt"/> (the water's surface over a scene position, the sea's or a lake's or a river's,
		/// negative infinity on dry ground) and <paramref name="groundAt"/>, water deeper than <see cref="WadeMetres"/> is
		/// baked as <see cref="DeepWaterArea"/>: the sea floor and lake beds keep their NavMesh, for swimmers only.
		/// </summary>
		public static bool Bake(Scene scene, List<string> notes, bool? heightMesh = null, float? voxelMetres = null,
			Func<float, float, float> surfaceAt = null, Func<float, float, float> groundAt = null, float lavaLevel = float.NegativeInfinity)
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			NavMeshSurface surface = EnsureSurface(scene);
			if (heightMesh.HasValue)
			{
				surface.buildHeightMesh = heightMesh.Value;
			}
			if (voxelMetres.HasValue)
			{
				surface.overrideVoxelSize = true;
				surface.voxelSize = voxelMetres.Value;
			}

			// The scene's own geometry: its terrain and every collider in it (not other open scenes').
			var bounds = new Bounds();
			bool any = false;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					if (terrain.terrainData == null)
					{
						continue;
					}
					var b = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
					if (any)
					{
						bounds.Encapsulate(b);
					}
					else
					{
						bounds = b;
						any = true;
					}
				}
			}
			if (!any)
			{
				notes?.Add("NavMesh: not baked, the scene has no terrain.");
				return false;
			}
			bounds.Expand(new Vector3(0f, 200f, 0f));
			var collected = new List<NavMeshBuildSource>();
			NavMeshBuilder.CollectSources(bounds, surface.layerMask, NavMeshCollectGeometry.PhysicsColliders, surface.defaultArea, new List<NavMeshBuildMarkup>(), collected);
			var sources = new List<NavMeshBuildSource>(collected.Count);
			foreach (NavMeshBuildSource source in collected)
			{
				if (source.component != null && source.component.gameObject.scene == scene)
				{
					sources.Add(source);
				}
			}
			int fromScene = sources.Count, props = 0;

			// The baked props' collision, straight from the data.
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (!root.TryGetComponent(out ScenePropColliders colliders))
				{
					continue;
				}
				foreach (ScenePropCollisionSet set in colliders.Sets)
				{
					if (set == null)
					{
						continue;
					}
					foreach (ScenePropCollisionSet.Instance instance in set.Instances)
					{
						if (instance.Prototype < 0 || instance.Prototype >= set.Prototypes.Length)
						{
							continue;
						}
						ScenePropCollisionSet.Prototype kind = set.Prototypes[instance.Prototype];
						// As the surface collects scene colliders: only the layers it bakes.
						if (kind.Mesh == null || (kind.Layer >= 0 && (surface.layerMask.value & (1 << kind.Layer)) == 0))
						{
							continue;
						}
						Mesh mesh = kind.Mesh;
						sources.Add(new NavMeshBuildSource
						{
							shape = NavMeshBuildSourceShape.Mesh,
							sourceObject = mesh,
							transform = Matrix4x4.TRS(instance.Position, instance.Rotation, instance.Scale),
							area = surface.defaultArea,
						});
						props++;
					}
				}
			}

			int waterBoxes = surfaceAt != null && groundAt != null ? WaterModifiers(bounds, surfaceAt, groundAt, sources, lavaLevel) : 0;

			NavMeshBuildSettings settings = surface.GetBuildSettings();
			NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
			if (data == null)
			{
				notes?.Add("NavMesh: the builder returned nothing.");
				return false;
			}
			data.name = $"{scene.name} NavMesh";
			/* Out of the world before the asset is overwritten: the copy below rewrites the data the surface added, and
			 * rewriting live NavMesh data whose tile layout changed (another voxel size, another extent) hung the editor
			 * on the next bake. */
			surface.RemoveData();
			string path = ScenePropBaker.AssetPath(scene, "NavMesh");
			if (path != null)
			{
				/* A sub-asset of a binary container: as text the data is written as hex, twice its size. Into the existing
				 * data when there is one, so the scene's reference to it survives a rebake. */
				var container = AssetDatabase.LoadAssetAtPath<BakedSceneData>(path);
				if (container == null)
				{
					if (AssetDatabase.LoadMainAssetAtPath(path) != null)
					{
						AssetDatabase.DeleteAsset(path);   // an older bake written as text
					}
					container = ScriptableObject.CreateInstance<BakedSceneData>();
					container.name = $"{scene.name} NavMesh";
					container.What = "NavMesh";
					AssetDatabase.CreateAsset(container, path);
				}
				NavMeshData existing = null;
				foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
				{
					existing ??= sub as NavMeshData;
				}
				if (existing != null)
				{
					EditorUtility.CopySerialized(data, existing);
					Object.DestroyImmediate(data);
					data = existing;
				}
				else
				{
					AssetDatabase.AddObjectToAsset(data, container);
				}
				EditorUtility.SetDirty(container);
				AssetDatabase.SaveAssets();
			}
			surface.navMeshData = data;
			if (surface.isActiveAndEnabled)
			{
				surface.AddData();
			}
			EditorUtility.SetDirty(surface);
			EditorSceneManager.MarkSceneDirty(scene);
			// The scene server's copy: builds and play mode strip the surface, so the catalogue is the only route.
			if (path != null)
			{
				SceneNavMeshCatalogueEditor.Sync(scene);
			}
			notes?.Add($"NavMesh: baked from {fromScene:N0} scene source(s) and {props:N0} prop(s), water deeper than {WadeMetres:0.#} m marked {DeepWaterArea} in {waterBoxes:N0} box(es), height mesh {(settings.buildHeightMesh ? "on" : "off")}, {settings.voxelSize:0.###} m voxels, in {clock.Elapsed.TotalSeconds:0.0} s.");
			return true;
		}

		/// <summary>
		/// Marks the water too deep to wade as <see cref="DeepWaterArea"/>: the water read on a <see cref="WaterCellMetres"/>
		/// grid, the deep cells merged into rectangles of one level, each a modifier box from below its bed to
		/// <see cref="BoxTopUnderSurfaceMetres"/> under its surface (so a berg's top, a floe, a pier or a bridge stays plain
		/// walkable). Returns how many boxes.
		/// </summary>
		/// <remarks>
		/// The sea floor, lake beds and deep river channels stay NavMesh, for swimmers: an NPC that cannot swim has the
		/// area masked out of its agent. A box rather than a carve: the builder only takes boxes as modifiers, and a
		/// run-merged grid keeps their number to thousands on a large scene.
		/// </remarks>
		/// <param name="lavaLevel">A lava sea's surface: ground under it is <see cref="LavaArea"/> at any depth (negative infinity for none).</param>
		internal static int WaterModifiers(Bounds bounds, Func<float, float, float> surfaceAt, Func<float, float, float> groundAt, List<NavMeshBuildSource> into,
			float lavaLevel = float.NegativeInfinity)
		{
			int deepArea = NavMesh.GetAreaFromName(DeepWaterArea);
			if (deepArea < 0)
			{
				Debug.LogWarning($"[NavMesh] The project has no '{DeepWaterArea}' NavMesh area; deep water is baked not walkable instead.");
				deepArea = NavMesh.GetAreaFromName("Not Walkable");
			}
			int lavaArea = NavMesh.GetAreaFromName(LavaArea);
			if (lavaArea < 0)
			{
				lavaArea = NavMesh.GetAreaFromName("Not Walkable");
			}
			float cell = WaterCellMetres;
			int width = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cell)), depth = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cell));
			float x0 = bounds.min.x, z0 = bounds.min.z;
			var surface = new float[width * depth];
			var bed = new float[width * depth];
			// Per cell: 0 dry or wadeable, 1 deep water, 2 lava.
			var kind = new byte[width * depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float east = x0 + (x + 0.5f) * cell, north = z0 + (z + 0.5f) * cell;
					float ground = groundAt(east, north);
					int i = z * width + x;
					bed[i] = ground;
					if (lavaLevel > ground)
					{
						surface[i] = lavaLevel;
						kind[i] = 2;
						continue;
					}
					float top = surfaceAt(east, north);
					if (float.IsNegativeInfinity(top) || float.IsNaN(top))
					{
						continue;
					}
					surface[i] = top;
					kind[i] = top - ground > WadeMetres ? (byte)1 : (byte)0;
				}
			}
			// Runs along each row, then each run grown down the rows while the next row has the same run.
			var used = new bool[width * depth];
			int boxes = 0;
			// A cell joins a box when it is deep, not yet boxed, and at the box's level: a box spans one water surface.
			bool Joins(int i, byte of, float level) => kind[i] == of && !used[i] && Mathf.Abs(surface[i] - level) <= BoxTopUnderSurfaceMetres;
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					byte of = kind[z * width + x];
					if (of == 0 || used[z * width + x])
					{
						continue;
					}
					int x1 = x;
					float level = surface[z * width + x];
					while (x1 + 1 < width && Joins(z * width + x1 + 1, of, level))
					{
						x1++;
					}
					int z1 = z;
					while (z1 + 1 < depth)
					{
						bool same = true;
						for (int k = x; k <= x1 && same; k++)
						{
							same = Joins((z1 + 1) * width + k, of, level);
						}
						if (!same)
						{
							break;
						}
						z1++;
					}
					float low = float.PositiveInfinity, lowest = float.PositiveInfinity;
					for (int r = z; r <= z1; r++)
					{
						for (int k = x; k <= x1; k++)
						{
							int i = r * width + k;
							used[i] = true;
							low = Mathf.Min(low, bed[i]);
							lowest = Mathf.Min(lowest, surface[i]);
						}
					}
					// From under the bed to under the surface: every bed in the box is deeper than WadeMetres, so it is inside.
					float bottom = low - 2f, top = lowest - BoxTopUnderSurfaceMetres;
					var centre = new Vector3(x0 + (x + x1 + 1) * 0.5f * cell, (bottom + top) * 0.5f, z0 + (z + z1 + 1) * 0.5f * cell);
					var size = new Vector3((x1 - x + 1) * cell, top - bottom, (z1 - z + 1) * cell);
					into.Add(new NavMeshBuildSource
					{
						shape = NavMeshBuildSourceShape.ModifierBox,
						transform = Matrix4x4.Translate(centre),
						size = size,
						area = of == 2 ? lavaArea : deepArea,
					});
					boxes++;
				}
			}
			return boxes;
		}

		/// <summary>The scene's NavMesh surface, made at its root (physics colliders, height mesh, the default agent) when it has none.</summary>
		public static NavMeshSurface EnsureSurface(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == ObjectName && root.TryGetComponent(out NavMeshSurface found))
				{
					return found;
				}
			}
			var go = new GameObject(ObjectName);
			SceneManager.MoveGameObjectToScene(go, scene);
			NavMeshSurface surface = go.AddComponent<NavMeshSurface>();
			surface.agentTypeID = 0;
			surface.collectObjects = CollectObjects.All;
			surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
			surface.buildHeightMesh = true;
			surface.overrideVoxelSize = true;
			surface.voxelSize = VoxelMetres;
			return surface;
		}
	}
}
#endif
