#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Gives every terrain in a scene a server-only copy of its terrain data while a server build
	/// processes it: the same ground and trees, none of the art.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What a dedicated server needs from a terrain.</b> The heights and holes, because the
	/// terrain collider is built from them and everything stands on it; and the tree prototypes and
	/// instances, because the collider builds a collider for every tree whose prefab has one.
	/// Nothing else. The detail layers (grass, flowers, pebbles — decorative, no colliders, by
	/// design), the splat maps that blend the ground textures, and the terrain layers that point at
	/// those textures are drawn and never touched by gameplay: no runtime code reads an alphamap or a
	/// detail layer (a server never renders a frame). On a generated scene they are most of the
	/// asset — a detail layer per grass prototype at detail resolution, a splat channel per biome
	/// texture at alphamap resolution — and every terrain layer pulls its textures into the server
	/// build behind it.
	/// </para>
	/// <para>
	/// <b>The committed asset is never modified.</b> Each terrain data is replaced on the build's copy
	/// of the scene by a new, in-memory terrain data built from the original's heights, holes and
	/// trees; the original is only read. The terrain and its collider are both pointed at the copy
	/// (a collider left on the original would pull the whole original into the build), and terrains
	/// that shared one data share one copy.
	/// </para>
	/// <para>
	/// <b>Why built fresh and not <c>Object.Instantiate</c>d.</b> A terrain data's alphamaps are
	/// texture sub-objects it references. A clone copies the references, not necessarily the textures,
	/// and clearing the clone's layers makes it drop — and destroy — alphamaps that may still be the
	/// original's; the original would then be damaged in memory, and saved damaged by the next
	/// <c>AssetDatabase.SaveAssets</c>. Starting from an empty terrain data and copying across only
	/// what is kept has no such sharing to get wrong.
	/// </para>
	/// <para>
	/// <b>Why the copy reaches the player.</b> Scene processors run on the build's copy of each scene
	/// before it is written, and the build writes every object the scene references that is not
	/// already an asset into the scene's own file — the same path Unity's static batching uses for
	/// the combined meshes it makes at build time. The copy is therefore serialized into the built
	/// scene, and the original asset, referenced by nothing the server keeps, is left out. The copy
	/// must not be <see cref="HideFlags.DontSave"/>, or the build would skip it and leave the terrain
	/// with no data.
	/// </para>
	/// <para>
	/// Runs after <see cref="ClientOnlySceneStripper"/> (whose terrain-free removals it does not care
	/// about either way), only in a build and only for the server, under the same test. The scene
	/// file on disk is never touched.
	/// </para>
	/// <para>
	/// Not done here, and left for a second phase: the tree prototypes still reference the client's
	/// full tree prefabs (meshes, LOD groups, materials, billboards), which the server only needs for
	/// their colliders. Swapping each prototype for a collider-only proxy prefab would take those
	/// out too.
	/// </para>
	/// </remarks>
	public sealed class ServerTerrainStripper : IProcessSceneWithReport
	{
		/// <summary>Unity's smallest alphamap resolution.</summary>
		public const int MinimumAlphamapResolution = 16;

		/// <summary>Unity's smallest base map resolution.</summary>
		public const int MinimumBaseMapResolution = 16;

		/// <summary>The smallest detail grid: one patch of the smallest patch size. There are no layers in it.</summary>
		public const int MinimumDetailResolution = 8;

		/// <summary>The name suffix that marks a copy as the server's in the build log and the memory profiler.</summary>
		public const string CopySuffix = " (server)";

		public int callbackOrder => 10;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (report == null || !ClientOnlySceneStripper.IsServerBuild())
			{
				return;
			}
			int copied = Strip(scene);
			if (copied > 0)
			{
				Debug.Log($"[Server terrain stripper] Replaced {copied} terrain data asset(s) in '{scene.name}' with server copies: heights, holes and trees kept; detail layers, splat maps and terrain layers left out of the server build.");
			}
		}

		/// <summary>
		/// Points every terrain and terrain collider in a scene at a server copy of its data. Returns
		/// how many distinct terrain data were copied. Never modifies the originals.
		/// </summary>
		public static int Strip(Scene scene)
		{
			var copies = new Dictionary<TerrainData, TerrainData>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					TerrainData original = terrain.terrainData;
					if (original == null)
					{
						continue;
					}
					terrain.terrainData = CopyFor(original, copies);
				}
				// Every collider, including one on an object with no Terrain (a collider-only tile).
				foreach (TerrainCollider collider in root.GetComponentsInChildren<TerrainCollider>(true))
				{
					TerrainData original = collider.terrainData;
					if (original == null || copies.ContainsValue(original))
					{
						continue;
					}
					collider.terrainData = CopyFor(original, copies);
				}
			}
			return copies.Count;
		}

		private static TerrainData CopyFor(TerrainData original, Dictionary<TerrainData, TerrainData> copies)
		{
			if (copies.ContainsValue(original))
			{
				// Already a server copy (a terrain whose collider was reached first).
				return original;
			}
			if (!copies.TryGetValue(original, out TerrainData copy))
			{
				copy = ServerCopy(original);
				copies.Add(original, copy);
			}
			return copy;
		}

		/// <summary>
		/// A new terrain data with the original's heights, holes, size and trees, and no detail
		/// layers, splat maps or terrain layers. The original is only read.
		/// </summary>
		public static TerrainData ServerCopy(TerrainData original)
		{
			if (original == null)
			{
				throw new ArgumentNullException(nameof(original));
			}
			var copy = new TerrainData
			{
				name = original.name + CopySuffix,
			};
			// Resolution before size: setting the resolution rescales the size it is given against.
			copy.heightmapResolution = original.heightmapResolution;
			copy.size = original.size;
			int resolution = original.heightmapResolution;
			copy.SetHeights(0, 0, original.GetHeights(0, 0, resolution, resolution));

			// Holes only where there are any: an all-solid terrain has no holes texture, and writing
			// an all-solid map would make one.
			int holes = original.holesResolution;
			if (holes > 0)
			{
				bool[,] solid = original.GetHoles(0, 0, holes, holes);
				if (HasHole(solid))
				{
					copy.enableHolesTextureCompression = original.enableHolesTextureCompression;
					copy.SetHoles(0, 0, solid);
				}
			}

			// No art. Each at Unity's minimum, so the empty grids cost nothing either.
			copy.terrainLayers = Array.Empty<TerrainLayer>();
			copy.alphamapResolution = MinimumAlphamapResolution;
			copy.baseMapResolution = MinimumBaseMapResolution;
			copy.detailPrototypes = Array.Empty<DetailPrototype>();
			copy.SetDetailResolution(MinimumDetailResolution, MinimumDetailResolution);

			// Trees last, onto the finished heights: the collider builds a collider for every instance
			// whose prototype's prefab has one. Positions are normalised to the size, which is the same.
			copy.treePrototypes = original.treePrototypes;
			copy.RefreshPrototypes();
			copy.SetTreeInstances(original.treeInstances, false);
			return copy;
		}

		private static bool HasHole(bool[,] solid)
		{
			int rows = solid.GetLength(0), columns = solid.GetLength(1);
			for (int y = 0; y < rows; y++)
			{
				for (int x = 0; x < columns; x++)
				{
					if (!solid[y, x])
					{
						return true;
					}
				}
			}
			return false;
		}
	}
}
#endif
