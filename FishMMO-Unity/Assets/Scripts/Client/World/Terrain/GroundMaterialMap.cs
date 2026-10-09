using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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
	/// (uploaded once from <see cref="TerrainData.GetHeights"/>), and its control maps (<see cref="TerrainData.alphamapTextures"/>)
	/// are copied GPU-to-GPU into consecutive slices of <c>_FishGroundControl</c>. Everything after that — the trace, the
	/// skirt, the paint — runs in the shaders; nothing is read back. The heights are NOT copied from
	/// <see cref="TerrainData.heightmapTexture"/>: Unity fills that render texture lazily, at the terrain's first draw, so
	/// a copy at scene load (this builds at the start of a frame) took uninitialised memory — traced ground 1–2 km in
	/// the air, the skirt thrown into the sky (2026-10-08). The layers' art is the scene's
	/// <see cref="TerrainArraySet"/> — the arrays and per-layer tiling, tint and surface the terrain itself is drawn
	/// with (TerrainArrayBinder) — published under their own global names.
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
		public const int MaxLayers = 32;

		/// <summary>Control maps one terrain can carry (eight of four channels).</summary>
		public const int MaxControls = 8;


		private static readonly int HeightsId = Shader.PropertyToID("_FishGroundHeights");
		private static readonly int ControlId = Shader.PropertyToID("_FishGroundControl");
		private static readonly int AlbedoId = Shader.PropertyToID("_FishGroundAlbedo");
		private static readonly int NormalsId = Shader.PropertyToID("_FishGroundNormals");
		private static readonly int InfoId = Shader.PropertyToID("_FishGroundInfo");
		private static readonly int ResId = Shader.PropertyToID("_FishGroundArrayRes");
		private static readonly int TileId = Shader.PropertyToID("_FishGroundTile");
		private static readonly int TileDataId = Shader.PropertyToID("_FishGroundTileData");
		private static readonly int LayerSTId = Shader.PropertyToID("_FishGroundLayerST");
		private static readonly int LayerTintId = Shader.PropertyToID("_FishGroundLayerTint");
		private static readonly int LayerSurfaceId = Shader.PropertyToID("_FishGroundLayerSurface");
		private static readonly int LayerMaskOffsetId = Shader.PropertyToID("_FishGroundLayerMaskOffset");
		private static readonly int LayerMaskScaleId = Shader.PropertyToID("_FishGroundLayerMaskScale");
		private static readonly int MasksId = Shader.PropertyToID("_FishGroundMasks");
		private static readonly int BlendId = Shader.PropertyToID("_FishGroundBlend");

		private static readonly Vector4[] tiles = new Vector4[MaxTiles];
		private static readonly Vector4[] tileData = new Vector4[MaxTiles];
		private static readonly Vector4[] layerST = new Vector4[MaxLayers];
		private static readonly Vector4[] layerTint = new Vector4[MaxLayers];
		private static readonly Vector4[] layerSurface = new Vector4[MaxLayers];
		private static readonly Vector4[] layerMaskOffset = new Vector4[MaxLayers];
		private static readonly Vector4[] layerMaskScale = new Vector4[MaxLayers];

		private static Texture2DArray heights;
		private static RenderTexture control;

		/// <summary>Terrains copied (0 when none): what a compute shader reading the heights must bind, as it does not see the render globals.</summary>
		public static int TileCount { get; private set; }

		/// <summary>The heights array (one slice per copied terrain, 0..1 of its height), or null.</summary>
		public static Texture Heights => heights;

		/// <summary>The heightmap slice size (texels).</summary>
		public static int HeightResolution { get; private set; }

		/// <summary>Per copied terrain: xy world xz origin, zw 1 / size xz (<see cref="MaxTiles"/> long).</summary>
		public static Vector4[] Tiles => tiles;

		/// <summary>Per copied terrain: x world y origin, y height (m), z control maps, w first control slice.</summary>
		public static Vector4[] TileData => tileData;

		/// <summary>Copies every terrain drawn from the most common array set into the GPU arrays and publishes them.</summary>
		public static void Build(Terrain[] terrains, Dictionary<Terrain, TerrainArraySet> sets)
		{
			TerrainArraySet set = MostCommon(sets);
			var chosen = new List<Terrain>();
			int heightRes = 0, controlRes = 0, controlSlices = 0;
			bool mixedRes = false;
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
					int controls = Mathf.Min(MaxControls, data.alphamapTextureCount);
					if (controls == 0)
					{
						continue;
					}
					if (heightRes != 0 && data.heightmapResolution != heightRes)
					{
						// The heights go up as they are, one slice per terrain: a slice cannot hold another size.
						Debug.LogWarning($"[Ground material] '{t.name}': heightmap {data.heightmapResolution}² differs from {heightRes}²; left out (average-colour blend there).");
						continue;
					}
					mixedRes |= controlRes != 0 && data.alphamapResolution != controlRes;
					heightRes = data.heightmapResolution;
					controlRes = Mathf.Max(controlRes, data.alphamapResolution);
					controlSlices += controls;
					chosen.Add(t);
				}
			}
			if (chosen.Count == 0)
			{
				Release();
				return;
			}
			if (mixedRes)
			{
				// Resampled to the largest: a smaller tile's texels then sit a fraction of a texel off. Generated scenes
				// cut every tile alike, so this is a hand-made scene's case.
				Debug.LogWarning("[Ground material] terrains differ in control resolution; the smaller ones are resampled.");
			}

			heights = EnsureHeights(heights, heightRes, chosen.Count);
			var row = new ushort[heightRes * heightRes];
			control = Ensure(control, controlRes, controlSlices, GraphicsFormat.R8G8B8A8_UNorm, "Ground control");
			int slice = 0;
			for (int i = 0; i < chosen.Count; i++)
			{
				Terrain t = chosen[i];
				TerrainData data = t.terrainData;
				Vector3 p = t.GetPosition();
				Vector3 s = data.size;
				int controls = Mathf.Min(MaxControls, data.alphamapTextureCount);
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
				for (int k = 0; k < controls; k++)
				{
					Copy(data.GetAlphamapTexture(k), control, slice + k);
				}
				tiles[i] = new Vector4(p.x, p.z, 1f / s.x, 1f / s.z);
				tileData[i] = new Vector4(p.y, s.y, controls, slice);
				slice += controls;
			}

			heights.Apply(false, false);

			int layers = Mathf.Min(MaxLayers, set.LayerCount);
			for (int l = 0; l < MaxLayers; l++)
			{
				layerST[l] = l < layers && l < set.LayerST.Length ? set.LayerST[l] : new Vector4(1f, 1f, 0f, 0f);
				layerTint[l] = l < layers && l < set.LayerTint.Length ? set.LayerTint[l] : Vector4.one;
				layerSurface[l] = l < layers && l < set.LayerSurface.Length ? set.LayerSurface[l] : Vector4.zero;
				layerMaskOffset[l] = l < layers && l < set.LayerMaskOffset.Length ? set.LayerMaskOffset[l] : Vector4.zero;
				layerMaskScale[l] = l < layers && l < set.LayerMaskScale.Length ? set.LayerMaskScale[l] : Vector4.one;
			}

			// What the terrain's own material does with them (FishWeatherTerrainArray.shader): its height blend, how deep
			// snow lifts it, how much weather it takes — the base blend must render it the same way.
			Material terrainMaterial = chosen[0].materialTemplate;
			float heightTransition = terrainMaterial != null && terrainMaterial.HasFloat("_HeightTransition") ? terrainMaterial.GetFloat("_HeightTransition") : 0f;
			bool heightBlend = terrainMaterial != null && terrainMaterial.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT");
			float snowDepth = terrainMaterial != null && terrainMaterial.HasFloat("_FishSnowDepth") ? terrainMaterial.GetFloat("_FishSnowDepth") : 0f;
			float weatherAmount = terrainMaterial != null && terrainMaterial.HasFloat("_FishWeatherAmount") ? terrainMaterial.GetFloat("_FishWeatherAmount") : 1f;

			Shader.SetGlobalTexture(HeightsId, heights);
			Shader.SetGlobalTexture(ControlId, control);
			Shader.SetGlobalTexture(AlbedoId, set.Albedo);
			// Bound even when the set has no normals or masks (flagged off): an unbound array slot is not legal everywhere.
			Shader.SetGlobalTexture(NormalsId, set.Normal != null ? set.Normal : (Texture)set.Albedo);
			Shader.SetGlobalTexture(MasksId, set.Mask != null ? set.Mask : (Texture)set.Albedo);
			Shader.SetGlobalVectorArray(LayerMaskOffsetId, layerMaskOffset);
			Shader.SetGlobalVectorArray(LayerMaskScaleId, layerMaskScale);
			Shader.SetGlobalVector(BlendId, new Vector4(heightTransition, heightBlend ? 1f : 0f, set.Mask != null ? 1f : 0f, snowDepth));
			Shader.SetGlobalVectorArray(TileId, tiles);
			Shader.SetGlobalVectorArray(TileDataId, tileData);
			Shader.SetGlobalVectorArray(LayerSTId, layerST);
			Shader.SetGlobalVectorArray(LayerTintId, layerTint);
			Shader.SetGlobalVectorArray(LayerSurfaceId, layerSurface);
			Shader.SetGlobalVector(ResId, new Vector4(heightRes, controlRes, 0f, 0f));
			Shader.SetGlobalVector(InfoId, new Vector4(chosen.Count, layers, set.Normal != null ? 1f : 0f, weatherAmount));
			TileCount = chosen.Count;
			HeightResolution = heightRes;
			Debug.Log($"[Ground material] {chosen.Count} terrain(s) from '{set.name}': heights {heightRes}² × {chosen.Count}, control {controlRes}² × {controlSlices}, {layers} layers; masks {(set.Mask != null ? "yes" : "no")}, height blend {(heightBlend ? $"on ({heightTransition:F2})" : "off")}, snow lift {snowDepth:F2} m, weather {weatherAmount:F2}.");
#if UNITY_EDITOR
			CheckHeights(chosen);
#endif
		}

		/// <summary>
		/// Into one slice of an array, on the GPU: an exact <see cref="Graphics.CopyTexture(Texture, int, int, Texture, int, int)"/>
		/// when size and format agree and the device copies textures, else a <see cref="Graphics.Blit(Texture, RenderTexture, int, int)"/>
		/// (resampling).
		/// </summary>
		private static void Copy(Texture source, RenderTexture target, int slice)
		{
			bool same = source.width == target.width && source.height == target.height && source.graphicsFormat == target.graphicsFormat;
			UnityEngine.Rendering.CopyTextureSupport support = SystemInfo.copyTextureSupport;
			bool canCopy = (support & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0
				&& (source is RenderTexture || (support & UnityEngine.Rendering.CopyTextureSupport.TextureToRT) != 0);
			if (same && canCopy)
			{
				Graphics.CopyTexture(source, 0, 0, target, slice, 0);
			}
			else
			{
				Graphics.Blit(source, target, 0, slice);
			}
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
			// The control maps' first slice per terrain against the terrain's own splat weights.
			RenderTexture controls = control;
			for (int i = 0; i < chosen.Count && i < 4; i++)
			{
				Terrain t = chosen[i];
				int slice = (int)tileData[i].w;
				int res = controls.width;
				UnityEngine.Rendering.AsyncGPUReadback.Request(controls, 0, 0, res, 0, res, slice, 1, request =>
				{
					if (request.hasError || t == null || t.terrainData == null)
					{
						Debug.LogWarning($"[Ground material] control check {slice}: readback failed.");
						return;
					}
					Unity.Collections.NativeArray<Color32> data = request.GetData<Color32>();
					TerrainData td = t.terrainData;
					int ares = td.alphamapResolution;
					float worst = 0f;
					for (int s = 0; s < 5; s++)
					{
						int x = (ares - 1) * (s + 1) / 6;
						int z = (ares - 1) * ((s * 3 + 1) % 6) / 6;
						float[,,] w = td.GetAlphamaps(x, z, 1, 1);
						Color32 c = data[z * res + x];
						float[] copied = { c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f };
						for (int ch = 0; ch < 4 && ch < w.GetLength(2); ch++)
						{
							worst = Mathf.Max(worst, Mathf.Abs(copied[ch] - w[0, 0, ch]));
						}
					}
					Debug.Log($"[Ground material] control check '{t.name}': {(worst < 0.02f ? "ok" : "MISMATCH")}, worst weight difference {worst:F3}.");
				});
			}
		}
#endif

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
			if (control != null)
			{
				control.Release();
				Object.Destroy(control);
				control = null;
			}
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

		/// <summary>A clamped, bilinear, mip-less texture array of the given size, reused when it already fits.</summary>
		private static RenderTexture Ensure(RenderTexture rt, int size, int depth, GraphicsFormat format, string name)
		{
			if (rt != null && rt.width == size && rt.volumeDepth == depth && rt.graphicsFormat == format)
			{
				return rt;
			}
			if (rt != null)
			{
				rt.Release();
				Object.Destroy(rt);
			}
			var desc = new RenderTextureDescriptor(size, size, format, 0)
			{
				dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
				volumeDepth = depth,
				useMipMap = false,
				autoGenerateMips = false,
				msaaSamples = 1,
			};
			rt = new RenderTexture(desc)
			{
				name = name,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.DontSave,
			};
			rt.Create();
			return rt;
		}
	}
}
