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
	/// <b>GPU only.</b> Every terrain's heightmap (<see cref="TerrainData.heightmapTexture"/>, already on the GPU)
	/// is blitted into one slice of <c>_FishGroundHeights</c>, and its control maps (<see cref="TerrainData.alphamapTextures"/>)
	/// into consecutive slices of <c>_FishGroundControl</c>. Nothing is read back. The layers' art is the scene's
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

		/// <summary>The value of a full-height heightmap texel (Unity stores heights in 0..0.5 of an R16).</summary>
		private const float HeightmapMax = 32766f / 65535f;

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

		private static readonly Vector4[] tiles = new Vector4[MaxTiles];
		private static readonly Vector4[] tileData = new Vector4[MaxTiles];
		private static readonly Vector4[] layerST = new Vector4[MaxLayers];
		private static readonly Vector4[] layerTint = new Vector4[MaxLayers];
		private static readonly Vector4[] layerSurface = new Vector4[MaxLayers];

		private static RenderTexture heights;
		private static RenderTexture control;

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
					RenderTexture hm = data.heightmapTexture;
					int controls = Mathf.Min(MaxControls, data.alphamapTextureCount);
					if (hm == null || controls == 0)
					{
						continue;
					}
					mixedRes |= (heightRes != 0 && hm.width != heightRes) || (controlRes != 0 && data.alphamapResolution != controlRes);
					heightRes = Mathf.Max(heightRes, hm.width);
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
				Debug.LogWarning("[Ground material] terrains differ in heightmap or control resolution; the smaller ones are resampled.");
			}

			heights = Ensure(heights, heightRes, chosen.Count, GraphicsFormat.R16_UNorm, "Ground heights");
			control = Ensure(control, controlRes, controlSlices, GraphicsFormat.R8G8B8A8_UNorm, "Ground control");
			int slice = 0;
			for (int i = 0; i < chosen.Count; i++)
			{
				Terrain t = chosen[i];
				TerrainData data = t.terrainData;
				Vector3 p = t.GetPosition();
				Vector3 s = data.size;
				int controls = Mathf.Min(MaxControls, data.alphamapTextureCount);
				Graphics.Blit(data.heightmapTexture, heights, 0, i);
				for (int k = 0; k < controls; k++)
				{
					Graphics.Blit(data.GetAlphamapTexture(k), control, 0, slice + k);
				}
				tiles[i] = new Vector4(p.x, p.z, 1f / s.x, 1f / s.z);
				tileData[i] = new Vector4(p.y, s.y / HeightmapMax, controls, slice);
				slice += controls;
			}

			int layers = Mathf.Min(MaxLayers, set.LayerCount);
			for (int l = 0; l < MaxLayers; l++)
			{
				layerST[l] = l < layers && l < set.LayerST.Length ? set.LayerST[l] : new Vector4(1f, 1f, 0f, 0f);
				layerTint[l] = l < layers && l < set.LayerTint.Length ? set.LayerTint[l] : Vector4.one;
				layerSurface[l] = l < layers && l < set.LayerSurface.Length ? set.LayerSurface[l] : Vector4.zero;
			}

			Shader.SetGlobalTexture(HeightsId, heights);
			Shader.SetGlobalTexture(ControlId, control);
			Shader.SetGlobalTexture(AlbedoId, set.Albedo);
			// Bound even when the set has no normals (flagged off in Info.z): an unbound array slot is not legal everywhere.
			Shader.SetGlobalTexture(NormalsId, set.Normal != null ? set.Normal : (Texture)set.Albedo);
			Shader.SetGlobalVectorArray(TileId, tiles);
			Shader.SetGlobalVectorArray(TileDataId, tileData);
			Shader.SetGlobalVectorArray(LayerSTId, layerST);
			Shader.SetGlobalVectorArray(LayerTintId, layerTint);
			Shader.SetGlobalVectorArray(LayerSurfaceId, layerSurface);
			Shader.SetGlobalVector(ResId, new Vector4(heightRes, controlRes, 0f, 0f));
			Shader.SetGlobalVector(InfoId, new Vector4(chosen.Count, layers, set.Normal != null ? 1f : 0f, 0f));
			Debug.Log($"[Ground material] {chosen.Count} terrain(s) from '{set.name}': heights {heightRes}² × {chosen.Count}, control {controlRes}² × {controlSlices}, {layers} layers.");
		}

		/// <summary>Switches the terrain-material contact off (the shader falls back to the ground colour map) and frees the arrays.</summary>
		public static void Release()
		{
			Shader.SetGlobalVector(InfoId, Vector4.zero);
			if (heights != null)
			{
				heights.Release();
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
