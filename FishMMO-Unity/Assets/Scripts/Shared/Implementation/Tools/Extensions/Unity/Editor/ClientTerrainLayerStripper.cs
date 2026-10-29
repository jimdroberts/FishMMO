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
	/// <b>What the copy keeps.</b> Heights and holes (the collider and the shape). The detail layers
	/// and prototypes, and the trees. And either:
	/// </para>
	/// <list type="bullet">
	/// <item><b>no alphamaps and no layers</b>, where the scene's bake (<see cref="SceneTerrainBake"/>)
	/// stores the tile's index map and that map is, texel for texel, the CPU bake of the alphamaps as
	/// they are now (<see cref="KeepAlphamapsReason"/>). A player draws the stored map
	/// (TerrainArrayBinder), and the ground colour and the grass's surface layers read it on a tile
	/// with no alphamaps; the arrays and the set hold everything else a layer was. Decision 24
	/// (2026-10-16): "let client builds strip alphamaps". On a generated scene that is 6 maps of 512²
	/// RGBA with mips (8 MB) a tile against its 2 MB index map.</item>
	/// <item>otherwise (no stored map, stored before the alphamaps were last written, another size or
	/// layer count, or a texel that differs: one warning naming the reason) <b>the alphamaps</b>,
	/// exactly — the binder bakes the tile's index map from them where it is drawn — and the terrain
	/// layers, in the same count and order (the tile's own channels: the scene layer each holds is on
	/// its <see cref="TerrainTileLayers"/>, which the build writes for the player) with the same tiling
	/// and remaps, as fresh layers whose albedo, normal and mask are empty.</item>
	/// </list>
	/// <para>
	/// <b>The bake follows the copies.</b> The scene's <see cref="TerrainDetailBake"/> names each tile by
	/// its terrain data, so it is replaced on the build's copy of the scene by a clone that names the
	/// copies (<see cref="RemapBakes"/>): otherwise a player would find none of its tiles (grass,
	/// scatter, ground colour and index maps all built or baked again at load), and the bake's
	/// references to the originals would pull them — layer textures and all — into the build. (A
	/// plain data clone, unlike a terrain data's: it holds arrays and references, no sub-objects.)
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
			TerrainIndexMapBuildGuard.Validate(scene, report.summary.platform);
			var notes = new List<string>();
			int copied = Strip(scene, notes);
			if (copied > 0)
			{
				Debug.Log($"[Client terrain stripper] Replaced {copied} array-drawn terrain data asset(s) in '{scene.name}' with client copies: heights, holes, details and trees kept; terrain layer textures (already in the texture arrays) left out of the client build. {string.Join(" ", notes)}");
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
		public static int Strip(Scene scene) => Strip(scene, null);

		/// <inheritdoc cref="Strip(Scene)"/>
		/// <param name="notes">What happened to the alphamaps (one line each), when given; every tile that kept them is also logged as a warning.</param>
		public static int Strip(Scene scene, List<string> notes)
		{
			var copies = new Dictionary<TerrainData, TerrainData>();
			var layers = new Dictionary<TerrainLayer, TerrainLayer>();
			List<SceneTerrainBake> holders = BakesIn(scene);
			// Tiles whose stored map was refused: the build's bake drops it, so a player bakes from the alphamaps kept instead.
			var refused = new HashSet<TerrainData>();
			int stripped = 0, kept = 0;
			long alphamapBytes = 0, indexBytes = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					TerrainData original = terrain.terrainData;
					if (original == null || !IsArrayTerrain(terrain) || copies.ContainsValue(original))
					{
						continue;
					}
					/* A tile carrying only the layers it paints (TerrainTileLayers): its stored map names scene layers, so the check
					 * renames the alphamaps' bake the same way; and the map is written on the build's tile, for a player that keeps
					 * its alphamaps and bakes from them (it has no provenance to ask). */
					int[] sceneLayers = TerrainTileLayers.Of(terrain);
					WorldDesign.TerrainTileLayerSync.Record(terrain, sceneLayers);
					if (!copies.TryGetValue(original, out TerrainData copy))
					{
						bool keep = true;
						if (original.alphamapLayers > 0)
						{
							TerrainDetailBake.IndexMap stored = StoredIndexOf(holders, original);
							string reason = KeepAlphamapsReason(stored, original.alphamapResolution, original.alphamapLayers,
								() => TerrainIndexMap.Same(stored.Map, TerrainTileLayerMap.Bake(original.GetAlphamaps(0, 0, original.alphamapResolution, original.alphamapResolution), sceneLayers)));
							keep = reason != null;
							if (keep)
							{
								kept++;
								if (stored != null && stored.Format != 0)
								{
									refused.Add(original);
								}
								Debug.LogWarning($"[Client terrain stripper] '{scene.name}' / '{terrain.name}': alphamaps KEPT in the client build: {reason}. Repaint the scene (or Dashboard → Biomes → \"Bake terrain grass and scatter (open scene)\") and save it to store its index maps.");
							}
							else
							{
								stripped++;
								alphamapBytes += AlphamapBytes(original);
								indexBytes += (long)stored.Map.Length * sizeof(uint);
							}
						}
						copy = ClientCopy(original, layers, keepAlphamaps: keep);
						copies.Add(original, copy);
					}
					terrain.terrainData = copy;
				}
			}
			if (copies.Count == 0)
			{
				return 0;
			}
			int remapped = RemapBakes(holders, copies, refused);
			notes?.Add(stripped > 0
				? $"Alphamaps and layers left out of {stripped} tile(s) whose stored index maps match them texel for texel ({alphamapBytes / 1048576.0:0.0} MB of alphamaps for {indexBytes / 1048576.0:0.0} MB of index maps){(kept > 0 ? $"; KEPT on {kept} tile(s) (warnings above)" : "")}."
				: kept > 0 ? $"Alphamaps KEPT on all {kept} tile(s): no usable stored index maps (warnings above)." : "No alphamaps to leave out.");
			if (remapped > 0)
			{
				notes?.Add($"The scene's terrain detail bake now names the client copies ({remapped} reference(s)).");
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
		/// Why a tile must keep its alphamaps in a client build, or null when its stored index map may stand in for them: the
		/// map must be stored (this <see cref="TerrainIndexMap.StoredFormat"/>), not marked stale since, of the alphamaps' size
		/// and layer count, and (<paramref name="sameTexels"/>, asked last as it bakes the whole tile) the CPU bake of the
		/// alphamaps as they are now, texel for texel — the map a player draws must be the ground as painted.
		/// </summary>
		public static string KeepAlphamapsReason(TerrainDetailBake.IndexMap stored, int alphamapResolution, int alphamapLayers, Func<bool> sameTexels)
		{
			if (alphamapLayers <= 0)
			{
				return "it has no alphamaps";
			}
			if (stored == null || stored.Format == 0)
			{
				return "the scene's terrain detail bake stores no index map for it";
			}
			if (stored.Format != TerrainIndexMap.StoredFormat)
			{
				return $"its stored index map is of format {stored.Format}, not {TerrainIndexMap.StoredFormat}";
			}
			if (stored.Stale)
			{
				return "its alphamaps were written after its index map was stored";
			}
			if (stored.Resolution != alphamapResolution || stored.Layers != alphamapLayers)
			{
				return $"its stored index map is {stored.Resolution}² of {stored.Layers} layers, its alphamaps {alphamapResolution}² of {alphamapLayers}";
			}
			if (!TerrainIndexMap.IsUsable(stored.Format, stored.Stale, stored.Resolution, stored.Layers, stored.Map != null ? stored.Map.Length : 0, alphamapResolution, alphamapLayers))
			{
				return "its stored index map is incomplete";
			}
			if (sameTexels != null && !sameTexels())
			{
				return "its stored index map differs from its alphamaps";
			}
			return null;
		}

		/// <summary>The scene's terrain detail bakes (normally one, on its root "Terrain Detail Bake").</summary>
		private static List<SceneTerrainBake> BakesIn(Scene scene)
		{
			var result = new List<SceneTerrainBake>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (SceneTerrainBake holder in root.GetComponentsInChildren<SceneTerrainBake>(true))
				{
					if (holder.Bake != null)
					{
						result.Add(holder);
					}
				}
			}
			return result;
		}

		/// <summary>A terrain data's stored index map in any of the scene's bakes, or null.</summary>
		private static TerrainDetailBake.IndexMap StoredIndexOf(List<SceneTerrainBake> holders, TerrainData data)
		{
			foreach (SceneTerrainBake holder in holders)
			{
				TerrainDetailBake.Tile tile = holder.Bake.TileOf(data);
				if (tile != null && tile.Index != null && tile.Index.Format != 0)
				{
					return tile.Index;
				}
			}
			return null;
		}

		/// <summary>
		/// Points every bake of the scene at a clone of itself that names the client copies in place of the originals (its
		/// tiles' and its ground colour map's terrain data): the clone, not an asset, is written into the built scene, and the
		/// committed bake is only read. The stored index map of a tile in <paramref name="refused"/> (its alphamaps kept because
		/// the map was not theirs) is left out of the clone, so a player bakes that tile from its alphamaps rather than trust it.
		/// Returns how many references were moved.
		/// </summary>
		public static int RemapBakes(List<SceneTerrainBake> holders, Dictionary<TerrainData, TerrainData> copies, ICollection<TerrainData> refused = null)
		{
			int moved = 0;
			var clones = new Dictionary<TerrainDetailBake, TerrainDetailBake>();
			foreach (SceneTerrainBake holder in holders)
			{
				TerrainDetailBake bake = holder.Bake;
				if (bake == null)
				{
					continue;
				}
				if (!clones.TryGetValue(bake, out TerrainDetailBake clone))
				{
					clone = UnityEngine.Object.Instantiate(bake);
					clone.name = bake.name + CopySuffix;
					foreach (TerrainDetailBake.Tile tile in clone.Tiles)
					{
						if (tile == null || tile.Data == null)
						{
							continue;
						}
						if (refused != null && refused.Contains(tile.Data))
						{
							tile.Index = new TerrainDetailBake.IndexMap();
						}
						if (copies.TryGetValue(tile.Data, out TerrainData copy))
						{
							tile.Data = copy;
							moved++;
						}
					}
					TerrainData[] grounds = clone.Ground != null ? clone.Ground.Terrains : null;
					for (int i = 0; grounds != null && i < grounds.Length; i++)
					{
						if (grounds[i] != null && copies.TryGetValue(grounds[i], out TerrainData copy))
						{
							grounds[i] = copy;
							moved++;
						}
					}
					clones.Add(bake, clone);
				}
				holder.Bake = clone;
			}
			return moved;
		}

		/// <summary>The bytes of a terrain data's alphamap textures, every mip.</summary>
		private static long AlphamapBytes(TerrainData data)
		{
			long bytes = 0;
			for (int k = 0; k < data.alphamapTextureCount; k++)
			{
				Texture2D map = data.GetAlphamapTexture(k);
				for (int m = 0; map != null && m < map.mipmapCount; m++)
				{
					bytes += 4L * Mathf.Max(1, map.width >> m) * Mathf.Max(1, map.height >> m);
				}
			}
			return bytes;
		}

		/// <summary>
		/// A new terrain data with everything the original has except its layers' textures: heights,
		/// holes, size, alphamaps, layers (same count, order and tiling, no textures), details and
		/// trees — or, without <paramref name="keepAlphamaps"/>, no alphamaps and no layers at all (the
		/// alphamap resolution kept, for what reads it). The original is only read.
		/// <paramref name="layerCopies"/> shares one texture-less layer per original layer across all the
		/// copies made from it.
		/// </summary>
		public static TerrainData ClientCopy(TerrainData original, Dictionary<TerrainLayer, TerrainLayer> layerCopies = null, bool keepAlphamaps = true)
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
			TerrainLayer[] originalLayers = keepAlphamaps ? original.terrainLayers : new TerrainLayer[0];
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
