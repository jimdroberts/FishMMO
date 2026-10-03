#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Gives every array-drawn terrain in a scene a client copy of its terrain data while a client
	/// build processes it: everything the ground needs, with terrain layers that carry no textures.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A generated scene's terrain is drawn by the array shader
	/// (<see cref="TerrainArrayBinder.ShaderName"/>) from the scene's baked texture arrays, which the
	/// client build ships in their own addressable group. But the terrain data still lists its terrain
	/// layers, and every layer references its albedo, normal and mask — so the build carried each
	/// ground texture twice: once as a slice of an array, and once as the layer's own texture, which
	/// nothing ever samples. On a world of 1024² ground families that is tens of megabytes per scene.
	/// </para>
	/// <para>
	/// <b>What the copy keeps.</b> Heights and holes (the collider and the shape). The alphamaps —
	/// the array shader reads them as its control maps, so they must survive exactly. The terrain
	/// layers, in the same count and order (the alphamap channel IS the layer index IS the array
	/// slice) with the same tiling and remaps, but as fresh layers whose albedo, normal and mask are
	/// empty. The detail layers and prototypes, and the trees. Only the textures behind the layers go.
	/// </para>
	/// <para>
	/// <b>Only array terrains.</b> A terrain whose material is not on the array shader still draws
	/// its layers' textures through Unity's own terrain shader, and is left exactly as it is.
	/// </para>
	/// <para>
	/// <b>Built fresh, never <c>Object.Instantiate</c>d</b>, for the reason
	/// <see cref="ServerTerrainStripper"/> gives: a clone can share alphamap textures with the
	/// committed original, and changing the clone's layers can then damage the original in memory,
	/// to be saved damaged by the next <c>SaveAssets</c>. The original terrain data and layers are
	/// only ever read; the scene file on disk is never touched.
	/// </para>
	/// <para>
	/// <b>Why the copy reaches the player</b> — the same path the server copy takes: the build writes
	/// every object a processed scene references that is not an asset into the scene's own file. The
	/// terrain data copy, its alphamap textures and the texture-less layers are all such objects. The
	/// terrain AND its collider are pointed at the copy, so nothing in the scene references the
	/// original any more and it is left out. Nothing here may be <see cref="HideFlags.DontSave"/>.
	/// </para>
	/// <para>
	/// Client builds only (server builds keep <see cref="ServerTerrainStripper"/>'s copy, which drops
	/// layers and alphamaps altogether), and only in a build: <c>report</c> is null when a scene is
	/// processed for play mode.
	/// </para>
	/// </remarks>
	public sealed class ClientTerrainLayerStripper : IProcessSceneWithReport
	{
		/// <summary>The name suffix that marks a copy as the client's in the build log and the memory profiler.</summary>
		public const string CopySuffix = " (client)";

		public int callbackOrder => 10;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (report == null || ClientOnlySceneStripper.IsServerBuild())
			{
				return;
			}
			int copied = Strip(scene);
			if (copied > 0)
			{
				Debug.Log($"[Client terrain stripper] Replaced {copied} array-drawn terrain data asset(s) in '{scene.name}' with client copies: heights, holes, alphamaps, details and trees kept; terrain layer textures (already in the texture arrays) left out of the client build.");
			}
		}

		/// <summary>True for a terrain drawn by the array shader, whose layers' textures nothing samples.</summary>
		public static bool IsArrayTerrain(Terrain terrain)
		{
			Material material = terrain != null ? terrain.materialTemplate : null;
			return material != null && material.shader != null && material.shader.name == TerrainArrayBinder.ShaderName;
		}

		/// <summary>
		/// Points every array-drawn terrain in a scene, and its collider, at a client copy of its data.
		/// Returns how many distinct terrain data were copied. Never modifies the originals.
		/// </summary>
		public static int Strip(Scene scene)
		{
			var copies = new Dictionary<TerrainData, TerrainData>();
			var layers = new Dictionary<TerrainLayer, TerrainLayer>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					TerrainData original = terrain.terrainData;
					if (original == null || !IsArrayTerrain(terrain) || copies.ContainsValue(original))
					{
						continue;
					}
					if (!copies.TryGetValue(original, out TerrainData copy))
					{
						copy = ClientCopy(original, layers);
						copies.Add(original, copy);
					}
					terrain.terrainData = copy;
				}
			}
			if (copies.Count == 0)
			{
				return 0;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				// Every collider on copied data, including a collider-only tile; never one beside a terrain
				// that kept its original (a terrain on another material sharing the data).
				foreach (TerrainCollider collider in root.GetComponentsInChildren<TerrainCollider>(true))
				{
					TerrainData original = collider.terrainData;
					if (original == null || !copies.TryGetValue(original, out TerrainData copy))
					{
						continue;
					}
					Terrain beside = collider.GetComponent<Terrain>();
					if (beside == null || beside.terrainData == copy)
					{
						collider.terrainData = copy;
					}
				}
			}
			return copies.Count;
		}

		/// <summary>
		/// A new terrain data with everything the original has except its layers' textures: heights,
		/// holes, size, alphamaps, layers (same count, order and tiling, no textures), details and
		/// trees. The original is only read. <paramref name="layerCopies"/> shares one texture-less
		/// layer per original layer across all the copies made from it.
		/// </summary>
		public static TerrainData ClientCopy(TerrainData original, Dictionary<TerrainLayer, TerrainLayer> layerCopies = null)
		{
			if (original == null)
			{
				throw new ArgumentNullException(nameof(original));
			}
			layerCopies ??= new Dictionary<TerrainLayer, TerrainLayer>();
			var copy = new TerrainData
			{
				name = original.name + CopySuffix,
			};

			// Resolution before size: setting the resolution rescales the size it is given against.
			copy.heightmapResolution = original.heightmapResolution;
			copy.size = original.size;
			int resolution = original.heightmapResolution;
			copy.SetHeights(0, 0, original.GetHeights(0, 0, resolution, resolution));

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

			/* Layers and alphamaps: resolution first, then the layers (which sizes the splat maps to
			 * their count), then the weights — every channel, exactly. The shader reads the alphamap
			 * textures themselves, and float weights read from bytes write back to the same bytes. */
			copy.alphamapResolution = original.alphamapResolution;
			copy.baseMapResolution = original.baseMapResolution;
			TerrainLayer[] originalLayers = original.terrainLayers;
			var layers = new TerrainLayer[originalLayers.Length];
			for (int i = 0; i < originalLayers.Length; i++)
			{
				layers[i] = TexturelessCopy(originalLayers[i], layerCopies);
			}
			copy.terrainLayers = layers;
			if (layers.Length > 0)
			{
				int alpha = original.alphamapResolution;
				copy.SetAlphamaps(0, 0, original.GetAlphamaps(0, 0, alpha, alpha));
			}

			// Details: decorative grass and pebbles, drawn by the client exactly as authored.
			copy.SetDetailScatterMode(original.detailScatterMode);
			copy.detailPrototypes = original.detailPrototypes;
			copy.SetDetailResolution(original.detailResolution, original.detailResolutionPerPatch);
			copy.wavingGrassStrength = original.wavingGrassStrength;
			copy.wavingGrassAmount = original.wavingGrassAmount;
			copy.wavingGrassSpeed = original.wavingGrassSpeed;
			copy.wavingGrassTint = original.wavingGrassTint;
			int detailWidth = original.detailWidth, detailHeight = original.detailHeight;
			for (int layer = 0; layer < original.detailPrototypes.Length; layer++)
			{
				copy.SetDetailLayer(0, 0, layer, original.GetDetailLayer(0, 0, detailWidth, detailHeight, layer));
			}

			// Trees last, onto the finished heights. Positions are normalised to the size, which is the same.
			copy.treePrototypes = original.treePrototypes;
			copy.RefreshPrototypes();
			copy.SetTreeInstances(original.treeInstances, false);
			return copy;
		}

		/// <summary>
		/// A fresh layer with the original's name, tiling, normal scale, surface values and remaps, and
		/// no textures. A missing layer stays missing (its channel still exists).
		/// </summary>
		public static TerrainLayer TexturelessCopy(TerrainLayer original, Dictionary<TerrainLayer, TerrainLayer> layerCopies = null)
		{
			if (original == null)
			{
				return null;
			}
			if (layerCopies != null && layerCopies.TryGetValue(original, out TerrainLayer existing))
			{
				return existing;
			}
			var layer = new TerrainLayer
			{
				name = original.name + CopySuffix,
				diffuseTexture = null,
				normalMapTexture = null,
				maskMapTexture = null,
				tileSize = original.tileSize,
				tileOffset = original.tileOffset,
				normalScale = original.normalScale,
				metallic = original.metallic,
				smoothness = original.smoothness,
				specular = original.specular,
				diffuseRemapMin = original.diffuseRemapMin,
				diffuseRemapMax = original.diffuseRemapMax,
				maskMapRemapMin = original.maskMapRemapMin,
				maskMapRemapMax = original.maskMapRemapMax,
			};
			layerCopies?.Add(original, layer);
			return layer;
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
