using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FishMMO.Shared
{
	/// <summary>
	/// Bakes a terrain tile's index map (<see cref="TerrainIndexMap"/>) on the GPU, from the tile's own alphamaps, into an
	/// R32G32_UInt render texture the array terrain shader loads.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>From the GPU's alphamaps</b>, because a terrain paint stroke writes them there first and synchronises the CPU copy
	/// only when the stroke ends: baking from them keeps painting live (the binder rebakes a tile on every alphamap change),
	/// as reading the alphamaps per pixel did. Each map is copied into one slice of a scratch array, and one full-screen
	/// triangle (FishTerrainIndexBake.shader) writes every texel's four strongest layers. A 512² tile of eight maps is eight
	/// copies and one small draw; nothing is read back.
	/// </para>
	/// <para>
	/// <b>Baked live while the ground is edited, stored for play</b> (2026-10-16: "store index maps with the terrain at
	/// cut/repaint, still re-baked live while painting in the editor"). In edit mode every tile is baked here from its
	/// alphamaps, on every change. The cut also stores each tile's map, baked on the CPU by the same rule
	/// (<see cref="TerrainIndexMap.Bake(float[,,])"/>), in the scene's <see cref="TerrainDetailBake"/>; in play (the
	/// editor's and a player's) the binder uploads that (<see cref="Upload"/>) when it is still the alphamaps' own, and bakes
	/// here only when it is not. A client build then leaves the alphamaps out of every tile whose stored map it has checked
	/// against them (ClientTerrainLayerStripper), and the CPU readers that used them (GroundColourMap, the grass's surface
	/// layers) read the stored map on such a tile.
	/// </para>
	/// <para>
	/// Integer arithmetic on bytes, so the GPU bake is <see cref="TerrainIndexMap.BakeTexel"/> exactly; the editor checks a
	/// sample of texels against it once per tile and session (<see cref="Check"/>).
	/// </para>
	/// </remarks>
	public static class TerrainIndexMapBaker
	{
		/// <summary>The bake shader (FishTerrainIndexBake.shader).</summary>
		public const string ShaderName = "Hidden/FishMMO/Terrain Index Bake";

		/// <summary>The index map's format: x four ids, y four weights, a byte each.</summary>
		public const GraphicsFormat Format = GraphicsFormat.R32G32_UInt;

#if !UNITY_SERVER || UNITY_EDITOR
		private static readonly int SourceId = Shader.PropertyToID("_FishIndexSource");
		private static readonly int ParamsId = Shader.PropertyToID("_FishIndexBakeParams");

		private static Material material;
		private static RenderTexture scratch;
		private static CommandBuffer commands;
		private static MaterialPropertyBlock properties;
		private static RenderTexture blank;
		private static bool warnedUnsupported;

		/// <summary>The bake shader a set carries (so a player build has it), else the project's by name.</summary>
		public static Shader ShaderOf(TerrainArraySet set)
		{
			return set != null && set.IndexBakeShader != null ? set.IndexBakeShader : Shader.Find(ShaderName);
		}

		/// <summary>True when this device can bake and read index maps with this shader.</summary>
		public static bool CanBake(Shader shader)
		{
			return shader != null && shader.isSupported && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
				&& SystemInfo.IsFormatSupported(Format, GraphicsFormatUsage.Render);
		}

		/// <summary>
		/// An index map target: <paramref name="width"/> × <paramref name="height"/> texels (one per alphamap texel), an array of
		/// <paramref name="slices"/> when <paramref name="array"/> or more than one.
		/// </summary>
		public static RenderTexture Create(int width, int height, int slices, string name, bool array = false)
		{
			var desc = new RenderTextureDescriptor(Mathf.Max(1, width), Mathf.Max(1, height), Format, GraphicsFormat.None)
			{
				dimension = array || slices > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D,
				volumeDepth = Mathf.Max(1, slices),
				useMipMap = false,
				autoGenerateMips = false,
				msaaSamples = 1,
			};
			var rt = new RenderTexture(desc)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave,
			};
			rt.Create();
			return rt;
		}

		/// <summary>
		/// A 1 × 1 index map of unpainted ground (layer 0 alone): what a tile is given while it has none, so the shader's
		/// unsigned-integer texture is never left to Unity's float default.
		/// </summary>
		public static RenderTexture Blank
		{
			get
			{
				if (blank == null || !blank.IsCreated())
				{
					if (blank != null)
					{
						Destroy(blank);
					}
					blank = Create(1, 1, 1, "Terrain Index Blank");
					var clear = new CommandBuffer { name = "Terrain Index Blank" };
					clear.SetRenderTarget(blank);
					clear.ClearRenderTarget(false, true, Color.clear);
					Graphics.ExecuteCommandBuffer(clear);
					clear.Release();
				}
				return blank;
			}
		}

		/// <summary>True when <paramref name="target"/> is the size an index map of <paramref name="data"/> must be.</summary>
		public static bool Fits(RenderTexture target, TerrainData data)
		{
			int res = data != null ? data.alphamapResolution : 0;
			return target != null && target.IsCreated() && target.width == res && target.height == res && target.graphicsFormat == Format;
		}

		/// <summary>
		/// Bakes <paramref name="data"/>'s index map into <paramref name="target"/> (slice <paramref name="slice"/> of an
		/// array target), now. False, with one warning a session, when the device or shader cannot.
		/// </summary>
		public static bool Bake(TerrainData data, Shader shader, RenderTexture target, int slice = 0)
		{
			if (data == null || target == null)
			{
				return false;
			}
			if (!CanBake(shader))
			{
				if (!warnedUnsupported)
				{
					warnedUnsupported = true;
					Debug.LogWarning($"[Terrain index] Cannot bake index maps here ({(shader == null ? $"'{ShaderName}' not found" : shader.isSupported ? $"{Format} not renderable" : "shader not supported")}): array terrains draw their first layer only.");
				}
				return false;
			}
			int maps = data.alphamapTextureCount;
			int layers = data.alphamapLayers;
			int res = data.alphamapResolution;
			if (maps <= 0 || res <= 0)
			{
				return false;
			}
			if (material == null || material.shader != shader)
			{
				if (material != null)
				{
					Destroy(material);
				}
				material = new Material(shader) { name = "Terrain Index Bake", hideFlags = HideFlags.HideAndDontSave };
			}
			Texture2D first = data.GetAlphamapTexture(0);
			EnsureScratch(res, maps, first.graphicsFormat);
			for (int k = 0; k < maps; k++)
			{
				Copy(data.GetAlphamapTexture(k), scratch, k);
			}

			commands ??= new CommandBuffer { name = "Terrain Index Bake" };
			properties ??= new MaterialPropertyBlock();
			properties.Clear();
			properties.SetTexture(SourceId, scratch);
			properties.SetVector(ParamsId, new Vector4(layers, maps, res, res));
			commands.Clear();
			commands.SetRenderTarget(target, 0, CubemapFace.Unknown, slice);
			commands.SetViewport(new Rect(0f, 0f, res, res));
			commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, properties);
			Graphics.ExecuteCommandBuffer(commands);
			return true;
		}

		/// <summary>Whether <see cref="Upload"/> has said once that this device cannot hold a stored map.</summary>
		private static bool warnedUpload;

		/// <summary>True when this device can sample (load) an index map held as a texture of <see cref="Format"/>.</summary>
		public static bool CanUpload()
		{
			return SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.IsFormatSupported(Format, GraphicsFormatUsage.Sample);
		}

		/// <summary>
		/// A stored index map (<see cref="TerrainDetailBake.IndexMap"/>) as a GPU texture of <see cref="Format"/>, point
		/// filtered and clamped like a baked one, its CPU copy dropped once uploaded (the stored words stay in the bake);
		/// null, with one warning a session, where the device cannot hold it. The caller owns and destroys it.
		/// </summary>
		public static Texture2D Upload(TerrainDetailBake.IndexMap stored, string name)
		{
			if (stored == null || stored.Resolution <= 0 || stored.Map == null || stored.Map.Length != stored.Resolution * stored.Resolution * 2)
			{
				return null;
			}
			if (!CanUpload())
			{
				WarnUpload($"{Format} cannot be sampled here");
				return null;
			}
			Texture2D texture = null;
			try
			{
				texture = new Texture2D(stored.Resolution, stored.Resolution, Format, TextureCreationFlags.DontInitializePixels)
				{
					name = name,
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave,
				};
				texture.SetPixelData(stored.Map, 0);
				texture.Apply(false, true);
				return texture;
			}
			catch (System.Exception e)
			{
				Destroy(texture);
				WarnUpload(e.Message);
				return null;
			}
		}

		private static void WarnUpload(string why)
		{
			if (!warnedUpload)
			{
				warnedUpload = true;
				Debug.LogWarning($"[Terrain index] Cannot upload stored index maps ({why}): tiles are baked from their alphamaps where they have them.");
			}
		}

		/// <summary>
		/// Copies a tile's index map (a baked one or an uploaded stored one, of <see cref="Format"/>) into slice
		/// <paramref name="slice"/> of an index array of its size: an exact texture copy, nothing converted. False where the
		/// sizes or formats differ or the device cannot copy into a render texture.
		/// </summary>
		public static bool CopyInto(Texture source, RenderTexture target, int slice)
		{
			if (source == null || target == null || source.width != target.width || source.height != target.height
				|| source.graphicsFormat != Format || target.graphicsFormat != Format)
			{
				return false;
			}
			// A 2D map into an element of a 2D array is a basic copy; render texture to render texture too.
			CopyTextureSupport support = SystemInfo.copyTextureSupport;
			CopyTextureSupport needed = CopyTextureSupport.Basic | (source is RenderTexture ? 0 : CopyTextureSupport.TextureToRT);
			bool planar = (source.dimension == TextureDimension.Tex2D || source.dimension == TextureDimension.Tex2DArray)
				&& (target.dimension == TextureDimension.Tex2D || target.dimension == TextureDimension.Tex2DArray);
			if ((support & needed) != needed || (!planar && (support & CopyTextureSupport.DifferentTypes) == 0))
			{
				return false;
			}
			Graphics.CopyTexture(source, 0, 0, target, slice, 0);
			return true;
		}

		/// <summary>The scratch array the alphamaps are copied into, reused while it is big enough and of their format.</summary>
		private static void EnsureScratch(int res, int maps, GraphicsFormat format)
		{
			if (scratch != null && scratch.IsCreated() && scratch.width == res && scratch.height == res && scratch.volumeDepth >= maps && scratch.graphicsFormat == format)
			{
				return;
			}
			int depth = Mathf.Max(maps, scratch != null && scratch.width == res && scratch.graphicsFormat == format ? scratch.volumeDepth : 0);
			if (scratch != null)
			{
				scratch.Release();
				Destroy(scratch);
			}
			var desc = new RenderTextureDescriptor(res, res, format, GraphicsFormat.None)
			{
				dimension = TextureDimension.Tex2DArray,
				volumeDepth = depth,
				useMipMap = false,
				autoGenerateMips = false,
				msaaSamples = 1,
			};
			scratch = new RenderTexture(desc)
			{
				name = "Terrain Index Bake Source",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave,
			};
			scratch.Create();
		}

		/// <summary>
		/// One alphamap into one slice: an exact <see cref="Graphics.CopyTexture(Texture, int, int, Texture, int, int)"/>
		/// where the device copies textures into render textures, else a blit (same size, read at texel centres: the same
		/// bytes).
		/// </summary>
		private static void Copy(Texture source, RenderTexture target, int slice)
		{
			CopyTextureSupport support = SystemInfo.copyTextureSupport;
			bool canCopy = (support & CopyTextureSupport.Basic) != 0 && (support & CopyTextureSupport.TextureToRT) != 0
				&& source.width == target.width && source.height == target.height && source.graphicsFormat == target.graphicsFormat;
			if (canCopy)
			{
				Graphics.CopyTexture(source, 0, 0, target, slice, 0);
			}
			else
			{
				Graphics.Blit(source, target, 0, slice);
			}
		}

		/// <summary>Frees the bake's own scratch texture and material (not the index maps, which their owners hold).</summary>
		public static void ReleaseScratch()
		{
			if (scratch != null)
			{
				scratch.Release();
				Destroy(scratch);
				scratch = null;
			}
			if (material != null)
			{
				Destroy(material);
				material = null;
			}
		}

		private static void Destroy(Object o)
		{
			if (o == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Object.Destroy(o);
			}
			else
			{
				Object.DestroyImmediate(o);
			}
		}

		/// <summary>Whether the check has said once that it cannot run here.</summary>
		private static bool checkSkipLogged;

		/// <summary>
		/// One slice of an index map as an RG32 float texture holding the same bits (Graphics.CopyTexture between formats of
		/// equal texel size copies raw bits), which AsyncGPUReadback accepts where it refuses the integer format. Null when
		/// the copy is not possible on this device; the caller destroys the copy.
		/// </summary>
		private static RenderTexture ReadableCopy(RenderTexture target, int slice)
		{
			if (target.graphicsFormat != GraphicsFormat.R32G32_UInt)
			{
				return null;
			}
			if (target.dimension != TextureDimension.Tex2D && (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
			{
				return null;
			}
			var copy = new RenderTexture(target.width, target.height, 0, GraphicsFormat.R32G32_SFloat)
			{
				name = "Terrain Index Check",
				hideFlags = HideFlags.HideAndDontSave,
			};
			try
			{
				copy.Create();
				Graphics.CopyTexture(target, slice, 0, copy, 0, 0);
				return copy;
			}
			catch (System.Exception)
			{
				Destroy(copy);
				return null;
			}
		}

		/// <summary>
		/// Whether a stored map still says what the tile's CPU alphamaps say, at <paramref name="samples"/> texels spread over
		/// the tile, edges included (the CPU bake of each, <see cref="TerrainIndexMap.BakeTexel"/>, against the stored words):
		/// the editor's guard against a map whose staleness was missed (an alphamap written with no terrain callback). True,
		/// with nothing compared, for a tile without alphamaps. Reads a texel at a time, so it is cheap.
		/// </summary>
		public static bool MatchesAlphamaps(TerrainData data, TerrainDetailBake.IndexMap stored, int samples, out string firstBad)
		{
			firstBad = null;
			if (data == null || stored == null)
			{
				firstBad = "nothing to compare";
				return false;
			}
			int layers = data.alphamapLayers;
			int res = data.alphamapResolution;
			if (layers <= 0)
			{
				return true;
			}
			if (res != stored.Resolution || stored.Map == null || stored.Map.Length != res * res * 2)
			{
				firstBad = $"stored {stored.Resolution}² against alphamaps {res}²";
				return false;
			}
			var weights = new byte[layers];
			for (int s = 0; s < samples; s++)
			{
				int x = (int)((long)(res - 1) * ((s * 7 + 3) % samples) / Mathf.Max(1, samples - 1));
				int y = (int)((long)(res - 1) * s / Mathf.Max(1, samples - 1));
				float[,,] alphas = data.GetAlphamaps(x, y, 1, 1);
				for (int l = 0; l < layers; l++)
				{
					weights[l] = TerrainIndexMap.ByteOf(alphas[0, 0, l]);
				}
				TerrainIndexMap.BakeTexel(weights, 0, layers, out uint ids, out uint packed);
				int at = 2 * (y * res + x);
				if (stored.Map[at] != ids || stored.Map[at + 1] != packed)
				{
					firstBad = $"texel ({x},{y}) stored ids {stored.Map[at]:X8} weights {stored.Map[at + 1]:X8}, alphamaps ids {ids:X8} weights {packed:X8}";
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Editor diagnostic: reads a few texels of a baked index map back and compares them with the CPU bake of the same
		/// texels from the terrain's CPU alphamaps (<see cref="TerrainIndexMap.BakeTexel"/>), logging
		/// "[Terrain index] check '&lt;tile&gt;': ok" or a MISMATCH with the first texel that differs. Call it only when the
		/// CPU copy is current (not mid-stroke). Nothing reads the map back otherwise.
		/// </summary>
		public static void Check(Terrain terrain, RenderTexture target, int slice = 0, int samples = 24)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null || target == null || !SystemInfo.supportsAsyncGPUReadback)
			{
				return;
			}
			int res = data.alphamapResolution;
			int layers = data.alphamapLayers;
			string name = terrain.name;
			// Read now: the callback runs frames later, when the index map may have been released and rebuilt.
			int width = target.width;
			/* AsyncGPUReadback refuses integer formats ("RG32 UInt is not supported as source for async read back"), so
			 * the slice is first copied bit for bit into a float texture of the same texel size, which it does read. The
			 * copy moves raw bits (no conversion), so reading it back as uint returns the ids and weights exactly. */
			RenderTexture readable = ReadableCopy(target, slice);
			if (readable == null)
			{
				if (!checkSkipLogged)
				{
					checkSkipLogged = true;
					Debug.Log("[Terrain index] check skipped: this device cannot copy the index map into a readable format.");
				}
				return;
			}
			AsyncGPUReadback.Request(readable, 0, request =>
			{
				Destroy(readable);
				if (request.hasError || data == null)
				{
					Debug.LogWarning($"[Terrain index] check '{name}': readback failed.");
					return;
				}
				Unity.Collections.NativeArray<uint> gpu = request.GetData<uint>();
				var weights = new byte[Mathf.Max(1, layers)];
				int bad = 0;
				string firstBad = null;
				for (int s = 0; s < samples; s++)
				{
					// Spread over the tile, edges included.
					int x = (int)((long)(res - 1) * ((s * 7 + 3) % samples) / Mathf.Max(1, samples - 1));
					int y = (int)((long)(res - 1) * s / Mathf.Max(1, samples - 1));
					float[,,] alphas = data.GetAlphamaps(x, y, 1, 1);
					for (int l = 0; l < layers; l++)
					{
						weights[l] = (byte)Mathf.Clamp(Mathf.RoundToInt(alphas[0, 0, l] * 255f), 0, 255);
					}
					TerrainIndexMap.BakeTexel(weights, 0, layers, out uint ids, out uint packed);
					int at = 2 * (y * width + x);
					if (at + 1 >= gpu.Length || gpu[at] != ids || gpu[at + 1] != packed)
					{
						bad++;
						firstBad ??= at + 1 < gpu.Length
							? $"texel ({x},{y}) GPU ids {gpu[at]:X8} weights {gpu[at + 1]:X8}, CPU ids {ids:X8} weights {packed:X8}"
							: $"texel ({x},{y}) outside the readback";
					}
				}
				if (bad == 0)
				{
					Debug.Log($"[Terrain index] check '{name}': ok ({samples} texels, {layers} layers, {res}²).");
				}
				else
				{
					Debug.LogWarning($"[Terrain index] check '{name}': MISMATCH at {bad} of {samples} texels; first {firstBad}.");
				}
			});
		}
#endif
	}
}
