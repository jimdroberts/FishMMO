using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The terrain's own height and splat weights, copied to the GPU for the objects standing on it: a tree trunk, a
	/// rock or a pebble finds the ground under each of its base vertices (FishGroundColour.hlsl,
	/// <c>FishContactVertex</c>) and draws the terrain's real layers over a band at its base — "vertex paint on
	/// placement", traced per vertex on the GPU instead of baked, because these objects are instanced: one mesh
	/// drawn thousands of times cannot carry per-instance vertex colours.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Data, then GPU only.</b> At scene load every terrain's heights go into one slice of <c>_FishGroundHeights</c>
	/// (uploaded once from <see cref="TerrainData.GetHeights"/>), and its index map is baked from its alphamaps on the GPU
	/// into one slice of <c>_FishGroundIndex</c>, as the terrain's own is (<see cref="TerrainIndexMapBaker"/>: the same four
	/// layers per texel, the same filter). Everything after that — the trace, the
	/// skirt, the paint — runs in the shaders; nothing is read back. The heights are NOT copied from
	/// <see cref="TerrainData.heightmapTexture"/>: Unity fills that render texture lazily, at the terrain's first draw, so
	/// a copy at scene load (this builds at the start of a frame) took uninitialised memory — traced ground 1–2 km in
	/// the air, the skirt thrown into the sky (2026-10-08). The layers' art is the scene's
	/// <see cref="TerrainArraySet"/> — the arrays and per-layer tiling, tint and surface the terrain itself is drawn
	/// with (TerrainArrayBinder) — published under their own global names, the numbers as a layer table
	/// (<see cref="TerrainLayerTable"/>, <c>_FishGroundLayerTable</c>).
	/// </para>
	/// <para>
	/// <b>One set.</b> Layer indices are the set's, so only terrains drawn from the most common set in the loaded
	/// scenes become tiles; any other terrain falls back to the ground colour map's average colour.
	/// </para>
	/// <para>
	/// Built and released with <see cref="GroundColourMap"/> (scene loads and unloads). Play mode, client only.
	/// </para>
	/// </remarks>
	public static class GroundMaterialMap
	{
		/// <summary>Matches FISH_GROUND_MAX_TILES in FishGroundColour.hlsl.</summary>
		public const int MaxTiles = 32;

		/// <summary>Matches FISH_GROUND_MAX_LAYERS in FishGroundColour.hlsl (and the terrain array's own limit).</summary>
		public const int MaxLayers = TerrainArrayLayerParams.MaximumLayers;


		private static readonly int HeightsId = Shader.PropertyToID("_FishGroundHeights");
		private static readonly int IndexId = Shader.PropertyToID("_FishGroundIndex");
		private static readonly int AlbedoId = Shader.PropertyToID("_FishGroundAlbedo");
		private static readonly int NormalsId = Shader.PropertyToID("_FishGroundNormals");
		private static readonly int InfoId = Shader.PropertyToID("_FishGroundInfo");
		private static readonly int ResId = Shader.PropertyToID("_FishGroundArrayRes");
		private static readonly int TileId = Shader.PropertyToID("_FishGroundTile");
		private static readonly int TileDataId = Shader.PropertyToID("_FishGroundTileData");
		private static readonly int LayerTableId = Shader.PropertyToID("_FishGroundLayerTable");
		private static readonly int MasksId = Shader.PropertyToID("_FishGroundMasks");
		private static readonly int BlendId = Shader.PropertyToID("_FishGroundBlend");

		private static readonly Vector4[] tiles = new Vector4[MaxTiles];
		private static readonly Vector4[] tileData = new Vector4[MaxTiles];

		private static Texture2DArray heights;
		private static RenderTexture index;
		private static RenderTexture blankIndex;
		private static Texture2D layerTable;

		/// <summary>Terrains copied (0 when none): what a compute shader reading the heights must bind, as it does not see the render globals.</summary>
		public static int TileCount { get; private set; }

		/// <summary>The heights array (one slice per copied terrain, 0..1 of its height), or null.</summary>
		public static Texture Heights => heights;

		/// <summary>The heightmap slice size (texels).</summary>
		public static int HeightResolution { get; private set; }

		/// <summary>Per copied terrain: xy world xz origin, zw 1 / size xz (<see cref="MaxTiles"/> long).</summary>
		public static Vector4[] Tiles => tiles;

		/// <summary>Per copied terrain: x world y origin, y height (m), z 1 when its index map was baked, w its index slice.</summary>
		public static Vector4[] TileData => tileData;

		/// <summary>Copies every terrain drawn from the most common array set into the GPU arrays and publishes them.</summary>
		public static void Build(Terrain[] terrains, Dictionary<Terrain, TerrainArraySet> sets)
		{
			TerrainArraySet set = MostCommon(sets);
			var chosen = new List<Terrain>();
			int heightRes = 0, controlRes = 0;
			if (set != null)
			{
				foreach (Terrain t in terrains)
				{
					if (chosen.Count >= MaxTiles)
					{
						Debug.LogWarning($"[Ground material] more than {MaxTiles} terrains: the rest keep the average-colour contact blend.");
						break;
					}
					if (t == null || t.terrainData == null || !sets.TryGetValue(t, out TerrainArraySet own) || own != set)
					{
						continue;
					}
					TerrainData data = t.terrainData;
					int indexRes = IndexResolutionOf(t, data);
					if (indexRes <= 0)
					{
						continue;
					}
					if (heightRes != 0 && data.heightmapResolution != heightRes)
					{
						// The heights go up as they are, one slice per terrain: a slice cannot hold another size.
						Debug.LogWarning($"[Ground material] '{t.name}': heightmap {data.heightmapResolution}² differs from {heightRes}²; left out (average-colour blend there).");
						continue;
					}
					if (controlRes != 0 && indexRes != controlRes)
					{
						// An index map cannot be resampled (ids do not blend), and a slice holds one size. Generated scenes cut
						// every tile alike, so this is a hand-made scene's case.
						Debug.LogWarning($"[Ground material] '{t.name}': alphamaps {indexRes}² differ from {controlRes}²; left out (average-colour blend there).");
						continue;
					}
					heightRes = data.heightmapResolution;
					controlRes = indexRes;
					chosen.Add(t);
				}
			}
			if (chosen.Count == 0)
			{
				Release();
				return;
			}

			heights = EnsureHeights(heights, heightRes, chosen.Count);
			var row = new ushort[heightRes * heightRes];
			index = EnsureIndex(index, controlRes, chosen.Count);
			Shader bake = TerrainIndexMapBaker.ShaderOf(set);
			int baked = 0;
			for (int i = 0; i < chosen.Count; i++)
			{
				Terrain t = chosen[i];
				TerrainData data = t.terrainData;
				Vector3 p = t.GetPosition();
				Vector3 s = data.size;
				// Normalised 0..1 of the terrain's height, row z, column x: the slice's texel (x, z).
				float[,] h = data.GetHeights(0, 0, heightRes, heightRes);
				for (int z = 0; z < heightRes; z++)
				{
					for (int x = 0; x < heightRes; x++)
					{
						row[z * heightRes + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(h[z, x]) * 65535f);
					}
				}
				heights.SetPixelData(row, 0, i);
				// The tile's four strongest layers per texel, as its own terrain draws them.
				bool hasIndex = FillIndex(t, data, bake, index, i);
				baked += hasIndex ? 1 : 0;
				tiles[i] = new Vector4(p.x, p.z, 1f / s.x, 1f / s.z);
				tileData[i] = new Vector4(p.y, s.y, hasIndex ? 1f : 0f, i);
			}

			heights.Apply(false, false);
			if (!Application.isEditor)
			{
				TerrainIndexMapBaker.ReleaseScratch();
			}

			int layers = Mathf.Min(MaxLayers, set.LayerCount);
			// Each layer's tiling, tint, surface, mask remap and what it is made of for the planet's looks (the band at a base
			// recolours as the terrain does): the terrain's own layer table, without the paths (the band draws none).
			if (layerTable == null)
			{
				layerTable = TerrainLayerTable.Create("Ground layer table");
			}
			TerrainLayerTable.Fill(layerTable, TerrainLayerTable.Pack(set, null, null, null));

			// What the terrain's own material does with them (FishWeatherTerrainArray.shader): its height blend, how deep
			// snow lifts it, how much weather it takes — the base blend must render it the same way.
			Material terrainMaterial = chosen[0].materialTemplate;
			float heightTransition = terrainMaterial != null && terrainMaterial.HasFloat("_HeightTransition") ? terrainMaterial.GetFloat("_HeightTransition") : 0f;
			bool heightBlend = terrainMaterial != null && terrainMaterial.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT");
			float snowDepth = terrainMaterial != null && terrainMaterial.HasFloat("_FishSnowDepth") ? terrainMaterial.GetFloat("_FishSnowDepth") : 0f;
			float weatherAmount = terrainMaterial != null && terrainMaterial.HasFloat("_FishWeatherAmount") ? terrainMaterial.GetFloat("_FishWeatherAmount") : 1f;

			Shader.SetGlobalTexture(HeightsId, heights);
			Shader.SetGlobalTexture(IndexId, index);
			Shader.SetGlobalTexture(LayerTableId, layerTable);
			Shader.SetGlobalTexture(AlbedoId, set.Albedo);
			// Bound even when the set has no normals or masks (flagged off): an unbound array slot is not legal everywhere.
			Shader.SetGlobalTexture(NormalsId, set.Normal != null ? set.Normal : (Texture)set.Albedo);
			Shader.SetGlobalTexture(MasksId, set.Mask != null ? set.Mask : (Texture)set.Albedo);
			Shader.SetGlobalVector(BlendId, new Vector4(heightTransition, heightBlend ? 1f : 0f, set.Mask != null ? 1f : 0f, snowDepth));
			Shader.SetGlobalVectorArray(TileId, tiles);
			Shader.SetGlobalVectorArray(TileDataId, tileData);
			Shader.SetGlobalVector(ResId, new Vector4(heightRes, controlRes, 0f, 0f));
			Shader.SetGlobalVector(InfoId, new Vector4(chosen.Count, layers, set.Normal != null ? 1f : 0f, weatherAmount));
			TileCount = chosen.Count;
			HeightResolution = heightRes;
			Debug.Log($"[Ground material] {chosen.Count} terrain(s) from '{set.name}': heights {heightRes}² × {chosen.Count}, index maps {controlRes}² × {baked}, {layers} layers; masks {(set.Mask != null ? "yes" : "no")}, height blend {(heightBlend ? $"on ({heightTransition:F2})" : "off")}, snow lift {snowDepth:F2} m, weather {weatherAmount:F2}.");
#if UNITY_EDITOR
			CheckHeights(chosen);
#endif
		}

#if UNITY_EDITOR
		/// <summary>
		/// Editor diagnostic only (the blend itself reads nothing back): reads a few copied heights back and compares
		/// them with the terrain's own, logging the worst difference — the first thing to look at if anything on the
		/// terrain is lifted or sunk by the contact blend.
		/// </summary>
		private static void CheckHeights(List<Terrain> chosen)
		{
			Texture2DArray target = heights;
			for (int i = 0; i < chosen.Count && i < 4; i++)
			{
				Terrain t = chosen[i];
				int slice = i;
				int res = target.width;
				UnityEngine.Rendering.AsyncGPUReadback.Request(target, 0, 0, res, 0, res, slice, 1, request =>
				{
					if (request.hasError || t == null || t.terrainData == null)
					{
						Debug.LogWarning($"[Ground material] height check {slice}: readback failed.");
						return;
					}
					Unity.Collections.NativeArray<ushort> data = request.GetData<ushort>();
					TerrainData td = t.terrainData;
					int sourceRes = td.heightmapResolution;
					float worst = 0f;
					string worstAt = "";
					for (int s = 0; s < 5; s++)
					{
						int x = (res - 1) * (s + 1) / 6;
						int z = (res - 1) * ((s * 3 + 1) % 6) / 6;
						float copied = t.GetPosition().y + data[z * res + x] / 65535f * td.size.y;
						float actual = t.GetPosition().y + td.GetHeight(Mathf.Min(x, sourceRes - 1), Mathf.Min(z, sourceRes - 1));
						if (Mathf.Abs(copied - actual) >= Mathf.Abs(worst))
						{
							worst = copied - actual;
							worstAt = $"({x},{z}) copied {copied:F2} m vs terrain {actual:F2} m";
						}
					}
					string verdict = Mathf.Abs(worst) < 0.05f ? "ok" : "MISMATCH";
					Debug.Log($"[Ground material] height check '{t.name}': {verdict}, worst {worst:F3} m at {worstAt}.");
				});
			}
			// The index maps' texels against the CPU bake from each terrain's own splat weights (TerrainIndexMap.BakeTexel).
			for (int i = 0; i < chosen.Count && i < 4; i++)
			{
				TerrainIndexMapBaker.Check(chosen[i], index, (int)tileData[i].w);
			}
		}
#endif

		/// <summary>
		/// The size of a terrain's index map: the one its binder draws, else its stored one, else its alphamaps'; 0 when it has
		/// none of them (a terrain with no layers).
		/// </summary>
		private static int IndexResolutionOf(Terrain t, TerrainData data)
		{
			Texture own = TerrainArrayBinder.IndexMapOf(t);
			if (own != null)
			{
				return own.width;
			}
			TerrainDetailBake.IndexMap stored = SceneTerrainBake.IndexOf(data);
			if (stored != null)
			{
				return stored.Resolution;
			}
			return data.alphamapTextureCount > 0 ? data.alphamapResolution : 0;
		}

		/// <summary>
		/// Slice <paramref name="slice"/> of the index array: the map the terrain's binder draws (baked or stored), copied;
		/// else the scene's stored map, uploaded and copied; else baked from the terrain's alphamaps. A client build's tile
		/// has no alphamaps, so one of the first two must serve it.
		/// </summary>
		private static bool FillIndex(Terrain t, TerrainData data, Shader bake, RenderTexture target, int slice)
		{
			if (TerrainIndexMapBaker.CopyInto(TerrainArrayBinder.IndexMapOf(t), target, slice))
			{
				return true;
			}
			TerrainDetailBake.IndexMap stored = SceneTerrainBake.IndexOf(data);
			if (stored != null)
			{
				Texture2D uploaded = TerrainIndexMapBaker.Upload(stored, $"{t.name} index map (stored, contact)");
				bool copied = TerrainIndexMapBaker.CopyInto(uploaded, target, slice);
				if (uploaded != null)
				{
					Object.Destroy(uploaded);
				}
				if (copied)
				{
					return true;
				}
			}
			return data.alphamapTextureCount > 0 && TerrainIndexMapBaker.Bake(data, bake, target, slice);
		}

		/// <summary>Switches the terrain-material contact off (the shader falls back to the ground colour map) and frees the arrays.</summary>
		public static void Release()
		{
			Shader.SetGlobalVector(InfoId, Vector4.zero);
			TileCount = 0;
			if (heights != null)
			{
				Object.Destroy(heights);
				heights = null;
			}
			if (index != null)
			{
				index.Release();
				Object.Destroy(index);
				index = null;
			}
			// Never left unbound: the shaders declare it an unsigned-integer array, which Unity's default would not be.
			Shader.SetGlobalTexture(IndexId, BlankIndex());
		}

		/// <summary>A one-slice index array of unpainted ground, bound while nothing is copied.</summary>
		private static RenderTexture BlankIndex()
		{
			if (blankIndex == null || !blankIndex.IsCreated())
			{
				blankIndex = TerrainIndexMapBaker.Create(1, 1, 1, "Ground index (none)", array: true);
				var clear = new UnityEngine.Rendering.CommandBuffer { name = "Ground index (none)" };
				clear.SetRenderTarget(blankIndex, 0, CubemapFace.Unknown, 0);
				clear.ClearRenderTarget(false, true, Color.clear);
				Graphics.ExecuteCommandBuffer(clear);
				clear.Release();
			}
			return blankIndex;
		}

		/// <summary>The index array (R32G32_UInt, a slice per terrain), reused when it already fits.</summary>
		private static RenderTexture EnsureIndex(RenderTexture rt, int size, int depth)
		{
			if (rt != null && rt.IsCreated() && rt.width == size && rt.volumeDepth == depth && rt.graphicsFormat == TerrainIndexMapBaker.Format)
			{
				return rt;
			}
			if (rt != null)
			{
				rt.Release();
				Object.Destroy(rt);
			}
			return TerrainIndexMapBaker.Create(size, size, depth, "Ground index", array: true);
		}

		/// <summary>The set most terrains are drawn from.</summary>
		private static TerrainArraySet MostCommon(Dictionary<Terrain, TerrainArraySet> sets)
		{
			var counts = new Dictionary<TerrainArraySet, int>();
			TerrainArraySet best = null;
			int bestCount = 0;
			foreach (TerrainArraySet set in sets.Values)
			{
				if (set == null || set.Albedo == null)
				{
					continue;
				}
				counts.TryGetValue(set, out int n);
				counts[set] = ++n;
				if (n > bestCount)
				{
					best = set;
					bestCount = n;
				}
			}
			return best;
		}

		/// <summary>The heights array (R16, linear, clamped, bilinear, no mips), reused when it already fits.</summary>
		private static Texture2DArray EnsureHeights(Texture2DArray array, int size, int depth)
		{
			if (array != null && array.width == size && array.depth == depth)
			{
				return array;
			}
			if (array != null)
			{
				Object.Destroy(array);
			}
			return new Texture2DArray(size, size, depth, TextureFormat.R16, false, true)
			{
				name = "Ground heights",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.DontSave,
			};
		}
	}
}
