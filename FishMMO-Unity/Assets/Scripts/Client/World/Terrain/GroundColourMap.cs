using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// The colour of the ground under every point of the loaded terrains, for the vegetation shader: grass
	/// and leaves take it at the root (FishVegetationPasses.hlsl, <c>VegGroundColour</c>), so a plant
	/// grows out of the soil it stands on instead of starting at a hard colour edge.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it holds.</b> One texture over the bounds of every active terrain, about
	/// <see cref="TargetTexelMetres"/> per texel: at each texel, the terrain's splat weights there times
	/// each layer's average colour — what the ground looks like from a few metres off, which is all a
	/// blade's root needs. The splat weights are the terrains' own control maps, the same ones the
	/// terrain shader reads.
	/// </para>
	/// <para>
	/// <b>Layer colours from what the terrain DRAWS.</b> The ground is drawn by FishMMO/Weather Terrain
	/// Array from its scene's baked <see cref="TerrainArraySet"/>: slice i of the albedo array for layer i,
	/// times that layer's tint. Those slices hold the art the scene really uses (LOCAL art where it has
	/// it), which need not be the terrain layer's own diffuse texture — the generated placeholder. The
	/// first version averaged the layers' own textures: a grass layer whose placeholder is green, drawn
	/// as red earth, gave the grass a green ground to match, and nothing changed on screen. The layer's
	/// own texture is only the fallback for a terrain with no bound set; while a binder's set is still
	/// loading (by address, in a player), the map is rebuilt once it arrives.
	/// </para>
	/// <para>
	/// <b>On its planet.</b> The terrain draws each layer on its world (FishFlora.hlsl FishFloraGround: living ground shifted
	/// by its biome's ground shift, bare ground toward the world's own on a thin-aired world; never a patch's look), so the
	/// map must too, or a plant's root and the far grass would pull toward a colour the ground does not have.
	/// The map is built in two parts: the terrain's own colour and, per texel, how much of the ground there is living,
	/// lichen and mineral (by the same weights; <see cref="TerrainArraySet.LayerFlora"/>). Neither depends on the world, so
	/// the scene's bake holds both as they are, and the looks are worn over them here, at load and whenever
	/// <see cref="FloraPaletteMap"/> changes (<see cref="WearLooks"/>): from the bake or worked out in play, the map is the
	/// same, and with the looks off (<see cref="FloraPaletteMap.Enabled"/>) it is the terrain's own colour exactly as before.
	/// The shift is one per biome (FloraPaletteMap.GroundShift); the contact blend at a base reads the terrain's layers
	/// itself (GroundMaterialMap) and takes the same shift there.
	/// </para>
	/// <para>
	/// <b>Published as shader globals</b> (<c>_FishGroundColour</c>, <c>_FishGroundColourRect</c>,
	/// <c>_FishGroundColourParams</c>). Params.x is 1 only while a map is set: in edit mode, or with no
	/// terrain, it is 0 and the shader blends nothing. Rebuilt when scenes load or unload. Play mode,
	/// client only.
	/// </para>
	/// </remarks>
	public static class GroundColourMap
	{
		/// <summary>The map's resolution, metres per texel (coarser for scenes too big for <see cref="MaxTexels"/>).</summary>
		public const float TargetTexelMetres = 2f;

		/// <summary>The largest side the map is allowed.</summary>
		public const int MaxTexels = 4096;

		private static readonly int TextureId = Shader.PropertyToID("_FishGroundColour");
		private static readonly int RectId = Shader.PropertyToID("_FishGroundColourRect");
		private static readonly int ParamsId = Shader.PropertyToID("_FishGroundColourParams");
		private static readonly int ShapeId = Shader.PropertyToID("_FishGroundColourShape");
		private static readonly int DistanceBlendId = Shader.PropertyToID("_FishDistanceBlend");
		private static readonly int ContactLargeId = Shader.PropertyToID("_FishContactLarge");

		/// <summary>The blend settings: the Weather Render Profile's "Ground colour under vegetation", read every frame (live in the inspector).</summary>
		private static float rootBlend = 1f, plantBlend = 0.9f, blendHeight = 0.8f, bladeDetail = 0.25f, crownStart = 1.5f, crownEnd = 3f;
		private static Vector4 distanceBlend = new Vector4(80f, 600f, 0.6f, 0.35f);
		/// <summary>Trees' and rocks' contact with the ground (FishGroundColour.hlsl FishGroundContact): band height, strength, unused, on.</summary>
		private static Vector4 contactLarge = new Vector4(0.5f, 0.75f, 0.6f, 1f);

		/// <summary>Takes the profile's settings, republishing when one changed.</summary>
		private static void ReadProfile()
		{
			WeatherRenderProfile p = WeatherRenderProfile.Active;
			if (p == null)
			{
				return;
			}
			float end = Mathf.Max(p.GroundCrownStart + 0.05f, p.GroundCrownEnd);
			var distance = new Vector4(Mathf.Max(0f, p.DistanceBlendStart), Mathf.Max(p.DistanceBlendStart + 1f, p.DistanceBlendEnd),
				Mathf.Clamp01(p.DistanceNormalFlatten), Mathf.Clamp01(p.DistanceGroundPull));
			if (distance != distanceBlend)
			{
				distanceBlend = distance;
				Publish();
			}
			var large = new Vector4(Mathf.Max(0.01f, p.ContactBlendHeightLarge), Mathf.Clamp01(p.ContactColourLarge), 0f, p.ContactBlendLarge ? 1f : 0f);
			if (large != contactLarge)
			{
				contactLarge = large;
				Publish();
			}
			if (p.GroundRootBlend != rootBlend || p.GroundPlantBlend != plantBlend || p.GroundBlendHeight != blendHeight
				|| p.GroundBladeDetail != bladeDetail || p.GroundCrownStart != crownStart || end != crownEnd)
			{
				rootBlend = Mathf.Clamp01(p.GroundRootBlend);
				plantBlend = Mathf.Clamp01(p.GroundPlantBlend);
				blendHeight = Mathf.Max(0.05f, p.GroundBlendHeight);
				bladeDetail = Mathf.Clamp(p.GroundBladeDetail, 0f, 0.6f);
				crownStart = Mathf.Max(0f, p.GroundCrownStart);
				crownEnd = end;
				Publish();
			}
		}

		private static bool published;

		private static readonly Dictionary<Texture, Color> means = new Dictionary<Texture, Color>();

		/// <summary>
		/// Debug view (Params.y): 0 off; 1 plants drawn wholly in the colour their root reads from the map,
		/// magenta where it reads nothing; 2 plants drawn grey by how much ground colour the normal blend gives
		/// them (white all, black none). Set at any time; it survives rebuilds.
		/// </summary>
		public static int DebugMode
		{
			get => debugMode;
			set
			{
				debugMode = Mathf.Clamp(value, 0, 2);
				Publish();
			}
		}

		private static int debugMode;

		/// <summary>The shader globals as they are now, for the log.</summary>
		public static string Describe() =>
			$"params {Shader.GetGlobalVector(ParamsId)} (on, debug, root, plant), shape {Shader.GetGlobalVector(ShapeId)} (blend height, crown start, crown end, detail), " +
			$"rect {Shader.GetGlobalVector(RectId)}, map {(map != null ? $"{map.width}×{map.height}" : "none")}";
		private static readonly Dictionary<(Texture, int), Color> sliceMeans = new Dictionary<(Texture, int), Color>();
		private static Texture2D map;
		/// <summary>The terrain's surface normal over the same rect (linear; rgb = n·0.5 + 0.5, a coverage): distant objects shade like the slope they stand on.</summary>
		private static Texture2D normalMap;
		private static readonly int NormalTextureId = Shader.PropertyToID("_FishGroundNormal");
		private static bool hooked, dirty = true, waitingForArrays;
		private static int builtFrame = -1, waitedFrames;

		/// <summary>
		/// The map as built, before the looks: the terrain's own colour (RGBA8 sRGB, a coverage) and per texel the ground's
		/// shares of living, lichen and mineral (RGBA8, a coverage), the bake's own arrays when it came from the bake (never
		/// written), with the rect they cover. The looks are worn over them into <see cref="wornColour"/>.
		/// </summary>
		private static byte[] rawColour, rawShares, wornColour;
		private static Rect mapBounds;
		private static float mapTexel;
		/// <summary>The <see cref="FloraPaletteMap.Version"/> the map last wore.</summary>
		private static int wornVersion = -1;

		/// <summary>Frames to keep rebuilding while a terrain's array set is still loading (a player loads it by address).</summary>
		private const int ArrayWaitFrames = 600;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			Unhook();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			dirty = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
			Application.quitting += Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
		}

		private static void Unhook()
		{
			if (!hooked)
			{
				return;
			}
			hooked = false;
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			Application.quitting -= Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
		}

#if UNITY_EDITOR
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
			{
				Shutdown();
			}
		}
#endif

		private static void Shutdown()
		{
			Release();
			Unhook();
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => dirty = true;

		private static void OnSceneUnloaded(Scene scene) => dirty = true;

		/// <summary>This system's once-a-frame work before the cameras render, named in the profiler (StartupTimeline reads it).</summary>
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("GroundColourMap.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using var contextScope = ContextMarker.Auto();
			ReadProfile();
			// Terrains enable themselves during the frame a scene loads: build on the next frame's render.
			if (!dirty && waitingForArrays && waitedFrames < ArrayWaitFrames && ++waitedFrames % 15 == 0 && ArraysReady())
			{
				dirty = true;   // the sets arrived: rebuild with the colours the terrain draws
			}
			if (!dirty || builtFrame == Time.frameCount)
			{
				// The looks were built, switched or retuned since the map last wore them.
				if (!dirty && map != null && wornVersion != FloraPaletteMap.Version)
				{
					WearLooks();
				}
				return;
			}
			dirty = false;
			builtFrame = Time.frameCount;
			Build();
		}

		/// <summary>Writes the switches and strengths to the shader globals (on only while a map is published).</summary>
		private static void Publish()
		{
			bool on = published && map != null;
			Shader.SetGlobalVector(ParamsId, new Vector4(on ? 1f : 0f, debugMode, rootBlend, plantBlend));
			Shader.SetGlobalVector(ShapeId, new Vector4(blendHeight, crownStart, crownEnd, bladeDetail));
			Shader.SetGlobalVector(DistanceBlendId, distanceBlend);
			Shader.SetGlobalVector(ContactLargeId, contactLarge);
		}

		/// <summary>Takes the map down and switches the shader's blend off.</summary>
		public static void Release()
		{
			published = false;
			Publish();
			if (map != null)
			{
				Object.Destroy(map);
				map = null;
			}
			if (normalMap != null)
			{
				Object.Destroy(normalMap);
				normalMap = null;
			}
			means.Clear();
			sliceMeans.Clear();
			rawColour = rawShares = wornColour = null;
			wornVersion = -1;
			waitingForArrays = false;
			GroundMaterialMap.Release();
		}

		private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("GroundColourMap.Build");
		private static readonly ProfilerMarker MaterialMarker = new ProfilerMarker("GroundMaterialMap.Build");

		/// <summary>
		/// Builds the map from every active terrain and publishes it (or switches the blend off when there is none): from the
		/// scene's bake (TerrainDetailBake.GroundColour) when it was made over these very terrains, else worked out here.
		/// </summary>
		public static void Build()
		{
			using var buildScope = BuildMarker.Auto();
			Terrain[] terrains = Terrain.activeTerrains;
			Dictionary<Terrain, TerrainArraySet> sets = ArraySets(out bool missing);
			waitingForArrays = missing;
			waitedFrames = 0;
			Rect bounds;
			float texel;
			int w, h;
			Color32[] pixels = null, normals = null, shares = null;
			TerrainDetailBake.GroundColour baked = SceneTerrainBake.GroundOf(terrains);
			if (baked != null && !SameSets(baked, terrains, sets))
			{
				baked = null;
			}
			// A bake from before the shares were baked has none: the map is worked out here, as for no bake.
			if (baked != null && baked.Width > 0 && baked.Height > 0 && baked.Colour.Length == baked.Width * baked.Height * 4 && baked.Normals.Length == baked.Colour.Length
				&& baked.Shares != null && baked.Shares.Length == baked.Colour.Length)
			{
				bounds = baked.Bounds;
				texel = baked.Texel;
				w = baked.Width;
				h = baked.Height;
				// A set still loading (by address, in a player) still brings a rebuild when it arrives: the ground material
				// map below needs it, and this map is taken from the bake again then.
			}
			else
			{
				baked = null;
				if (!Compute(terrains, sets, out bounds, out texel, out w, out h, out pixels, out normals, out shares))
				{
					Release();
					return;
				}
			}
			if (map == null || map.width != w || map.height != h)
			{
				if (map != null)
				{
					Object.Destroy(map);
				}
				// sRGB, like the albedo it is mixed into; no mips (the shader reads level 0 at the root).
				map = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
				{
					name = "Ground colour map",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					hideFlags = HideFlags.DontSave,
				};
			}
			// The terrain's own colour and its shares; the looks are worn over them (WearLooks), into the map.
			rawColour = baked != null ? baked.Colour : Bytes(pixels);
			rawShares = baked != null ? baked.Shares : Bytes(shares);
			mapBounds = bounds;
			mapTexel = texel;
			WearLooks();
			if (normalMap == null || normalMap.width != w || normalMap.height != h)
			{
				if (normalMap != null)
				{
					Object.Destroy(normalMap);
				}
				normalMap = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
				{
					name = "Ground normal map",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					hideFlags = HideFlags.DontSave,
				};
			}
			if (baked != null)
			{
				normalMap.SetPixelData(baked.Normals, 0);
			}
			else
			{
				normalMap.SetPixels32(normals);
			}
			normalMap.Apply(false, false);
			Shader.SetGlobalTexture(NormalTextureId, normalMap);
			if (baked != null)
			{
				UnityEngine.Debug.Log($"[Ground colour] {w}×{h} map at {texel:0.#} m/texel over {terrains.Length} terrain(s), from the scene's bake.");
			}
			else
			{
				Report(terrains, sets, w, h, texel, pixels);
			}
			// The terrains' own heights and layers, for the contact blend at trunk, rock and detail bases (GPU only).
			using (MaterialMarker.Auto())
			{
				GroundMaterialMap.Build(terrains, sets);
			}
			Shader.SetGlobalTexture(TextureId, map);
			Shader.SetGlobalVector(RectId, new Vector4(bounds.xMin, bounds.yMin, 1f / (w * texel), 1f / (h * texel)));
			published = true;
			Publish();
		}

		/// <summary>
		/// Writes the map: the terrain's own colour in the planet's looks while <see cref="FloraPaletteMap"/> draws them, else
		/// as it is. Called when the map is built and whenever the looks change (<see cref="FloraPaletteMap.Version"/>), over
		/// the colour and shares kept from the build, so neither a rebuild nor the bake is needed for either.
		/// </summary>
		private static void WearLooks()
		{
			wornVersion = FloraPaletteMap.Version;
			if (map == null || rawColour == null || rawColour.Length != map.width * map.height * 4)
			{
				return;
			}
			byte[] colour = rawColour;
			if (rawShares != null && rawShares.Length == rawColour.Length && FloraPaletteMap.GroundLooks(out FloraPaletteMap.GroundLookTable looks))
			{
				if (wornColour == null || wornColour.Length != rawColour.Length)
				{
					wornColour = new byte[rawColour.Length];
				}
				Wear(rawColour, rawShares, map.width, map.height, mapBounds, mapTexel, looks, wornColour);
				colour = wornColour;
			}
			map.SetPixelData(colour, 0);
			map.Apply(false, false);
		}

		/// <summary>sRGB byte to linear light.</summary>
		private static readonly float[] SrgbToLinear = BuildSrgbToLinear();

		/// <summary>Linear light (0..1 in <see cref="LinearSteps"/> steps) to the nearest sRGB byte.</summary>
		private static readonly byte[] LinearToSrgb = BuildLinearToSrgb();
		private const int LinearSteps = 4096;

		private static float[] BuildSrgbToLinear()
		{
			var table = new float[256];
			for (int i = 0; i < 256; i++)
			{
				double c = i / 255.0;
				table[i] = (float)(c <= 0.04045 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4));
			}
			return table;
		}

		private static byte[] BuildLinearToSrgb()
		{
			var table = new byte[LinearSteps + 1];
			for (int i = 0; i <= LinearSteps; i++)
			{
				double l = (double)i / LinearSteps;
				double c = l <= 0.0031308 ? l * 12.92 : 1.055 * System.Math.Pow(l, 1.0 / 2.4) - 0.055;
				table[i] = (byte)System.Math.Max(0, System.Math.Min(255, (int)System.Math.Round(c * 255.0)));
			}
			return table;
		}

		private static byte ToSrgb(float linear) => LinearToSrgb[(int)(Mathf.Clamp01(linear) * LinearSteps + 0.5f)];

		/// <summary>
		/// The looks worn over a map: each covered texel's colour (sRGB bytes, taken to linear light) through
		/// <see cref="GrassMath.FloraGround"/> by its shares (living, lichen, mineral: lichen keeps its own colour), shifted by
		/// its biome's ground shift (the texel centre's biome on the looks' grid) and toward the world's ground, at the
		/// strengths the shaders use. A texel of bare ground under an ordinary air (no living share, no world colour) is copied
		/// as it is.
		/// </summary>
		private static void Wear(byte[] raw, byte[] shares, int w, int h, Rect bounds, float texel, FloraPaletteMap.GroundLookTable looks, byte[] result)
		{
			var ground = new System.Numerics.Vector3(looks.Ground.x, looks.Ground.y, looks.Ground.z);
			float groundStrength = looks.Ground.w, foliageStrength = looks.Strength.x;
			for (int z = 0; z < h; z++)
			{
				float wz = bounds.yMin + (z + 0.5f) * texel;
				for (int x = 0; x < w; x++)
				{
					int i = (z * w + x) * 4;
					byte living = shares[i], lichen = shares[i + 1], mineral = shares[i + 2];
					if (raw[i + 3] == 0 || (living == 0 && (mineral == 0 || groundStrength <= 0f)))
					{
						result[i] = raw[i];
						result[i + 1] = raw[i + 1];
						result[i + 2] = raw[i + 2];
						result[i + 3] = raw[i + 3];
						continue;
					}
					int index = looks.IndexAt(bounds.xMin + (x + 0.5f) * texel, wz);
					var albedo = new System.Numerics.Vector3(SrgbToLinear[raw[i]], SrgbToLinear[raw[i + 1]], SrgbToLinear[raw[i + 2]]);
					var share = new System.Numerics.Vector3(living, lichen, mineral) * (1f / 255f);
					System.Numerics.Vector3 c = GrassMath.FloraGround(albedo, share, looks.Shift[index], ground, foliageStrength, groundStrength);
					result[i] = ToSrgb(c.X);
					result[i + 1] = ToSrgb(c.Y);
					result[i + 2] = ToSrgb(c.Z);
					result[i + 3] = raw[i + 3];
				}
			}
		}

		/// <summary>RGBA8 texels as bytes, in order (the layout of the bake's arrays and of SetPixelData).</summary>
		private static byte[] Bytes(Color32[] texels)
		{
			var bytes = new byte[texels.Length * 4];
			for (int i = 0, at = 0; i < texels.Length; i++)
			{
				Color32 c = texels[i];
				bytes[at++] = c.r;
				bytes[at++] = c.g;
				bytes[at++] = c.b;
				bytes[at++] = c.a;
			}
			return bytes;
		}

		/// <summary>
		/// The map over <paramref name="terrains"/>: its rect, texel size and size, and each texel's colour (the splat-weighted
		/// mean of the layers as drawn with <paramref name="sets"/>), surface normal, and the ground's shares of living, lichen
		/// and mineral by the same weights (r, g, b; a coverage), which the looks are worn by (<see cref="WearLooks"/>). The
		/// colour is the terrain's own, before any look: nothing here depends on the world. False when there is no terrain.
		/// The play's build and the editor's bake (TerrainDetailBaker) are this one.
		/// </summary>
		public static bool Compute(IReadOnlyList<Terrain> terrains, Dictionary<Terrain, TerrainArraySet> sets, out Rect bounds, out float texel,
			out int w, out int h, out Color32[] pixels, out Color32[] normals, out Color32[] shares)
		{
			bounds = new Rect();
			texel = TargetTexelMetres;
			w = h = 0;
			pixels = normals = shares = null;
			bool any = false;
			foreach (Terrain t in terrains)
			{
				if (t == null || t.terrainData == null)
				{
					continue;
				}
				Vector3 p = t.GetPosition();
				Vector3 s = t.terrainData.size;
				var r = new Rect(p.x, p.z, s.x, s.z);
				bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin), Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax)) : r;
				any = true;
			}
			if (!any || bounds.width <= 0f || bounds.height <= 0f)
			{
				return false;
			}
			texel = Mathf.Max(TargetTexelMetres, Mathf.Max(bounds.width, bounds.height) / MaxTexels);
			w = Mathf.Clamp(Mathf.CeilToInt(bounds.width / texel), 1, MaxTexels);
			h = Mathf.Clamp(Mathf.CeilToInt(bounds.height / texel), 1, MaxTexels);
			pixels = new Color32[w * h];
			// Alpha is coverage: 0 where no terrain was read, which the shader treats as "no blend".
			var mid = new Color32(128, 128, 128, 0);
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = mid;
			}
			normals = new Color32[w * h];
			var flat = new Color32(128, 255, 128, 0);
			for (int i = 0; i < normals.Length; i++)
			{
				normals[i] = flat;
			}
			shares = new Color32[w * h];
			foreach (Terrain t in terrains)
			{
				if (t != null && t.terrainData != null)
				{
					sets.TryGetValue(t, out TerrainArraySet set);
					Paint(t, set, bounds, w, h, pixels, shares);
					PaintNormals(t, bounds, w, h, normals);
				}
			}
			return true;
		}

		/// <summary>
		/// A texture set's key for the bake: what of it the map depends on besides its albedo (the layers, their tints, and
		/// what each is made of for the looks: <see cref="TerrainArraySet.LayerFlora"/>, from the layers' names). A bake made
		/// before the shares were in the key never matches it, and play works its map out instead.
		/// </summary>
		public static int SetKey(TerrainArraySet set)
		{
			if (set == null)
			{
				return 0;
			}
			unchecked
			{
				int key = 17 * 31 + set.LayerCount;
				if (set.LayerTint != null)
				{
					foreach (Vector4 tint in set.LayerTint)
					{
						key = key * 31 + tint.GetHashCode();
					}
				}
				key = key * 31 + 0x464C4F52;
				foreach (Vector4 flora in set.LayerFlora())
				{
					key = key * 31 + flora.GetHashCode();
				}
				return key;
			}
		}

		/// <summary>
		/// Whether a bake was made with the texture sets bound now: each terrain's own (its albedo and tints), where it has one
		/// (a set still loading, by address in a player, is taken to be the one baked).
		/// </summary>
		private static bool SameSets(TerrainDetailBake.GroundColour baked, Terrain[] terrains, Dictionary<Terrain, TerrainArraySet> sets)
		{
			if (baked.Albedos == null || baked.SetKeys == null || baked.Albedos.Length != baked.Terrains.Length || baked.SetKeys.Length != baked.Terrains.Length)
			{
				return false;
			}
			foreach (Terrain t in terrains)
			{
				if (t == null || t.terrainData == null || !sets.TryGetValue(t, out TerrainArraySet set))
				{
					continue;
				}
				int at = System.Array.IndexOf(baked.Terrains, t.terrainData);
				if (at < 0 || baked.Albedos[at] != set.Albedo || baked.SetKeys[at] != SetKey(set))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// One line per build: the map's size, how much of it a terrain covered, and every layer's average
		/// colour; in the editor also the map itself as Library/FishMMO/GroundColourMap.png, to look at.
		/// </summary>
		private static void Report(Terrain[] terrains, Dictionary<Terrain, TerrainArraySet> sets, int w, int h, float texel, Color32[] pixels)
		{
			int covered = 0;
			foreach (Color32 p in pixels)
			{
				covered += p.a > 0 ? 1 : 0;
			}
			var layers = new System.Text.StringBuilder();
			var seen = new HashSet<TerrainLayer>();
			foreach (Terrain t in terrains)
			{
				TerrainLayer[] ls = t != null && t.terrainData != null ? t.terrainData.terrainLayers : null;
				if (ls == null)
				{
					continue;
				}
				sets.TryGetValue(t, out TerrainArraySet set);
				int[] sceneLayers = TerrainTileLayers.Of(t);
				for (int i = 0; i < ls.Length; i++)
				{
					TerrainLayer l = ls[i];
					if (l != null && seen.Add(l))
					{
						Color c = LayerColour(set, ls, i, TerrainTileLayerMap.SceneLayerOf(sceneLayers, i), out bool fromArray);
						layers.Append(layers.Length > 0 ? ", " : "").Append(l.name).Append(" #").Append(ColorUtility.ToHtmlStringRGB(c))
							.Append(fromArray ? "" : " (layer texture: no array set)");
					}
				}
			}
			string png = null;
#if UNITY_EDITOR
			try
			{
				string folder = System.IO.Path.Combine(Application.dataPath, "..", "Library", "FishMMO");
				System.IO.Directory.CreateDirectory(folder);
				png = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "GroundColourMap.png"));
				System.IO.File.WriteAllBytes(png, map.EncodeToPNG());
			}
			catch (System.Exception e)
			{
				png = "not written: " + e.Message;
			}
#endif
			UnityEngine.Debug.Log($"[Ground colour] {w}×{h} map at {texel:0.#} m/texel over {terrains.Length} terrain(s), {100f * covered / Mathf.Max(1, pixels.Length):0}% covered; layers: {layers}{(png != null ? $"; map {png}" : "")}.");
		}

		/// <summary>Writes one terrain's ground colour into the texels it covers.</summary>
		/// <summary>Writes one terrain's surface normal (central differences of its heights) into the texels it covers.</summary>
		private static void PaintNormals(Terrain terrain, Rect bounds, int w, int h, Color32[] normals)
		{
			TerrainData data = terrain.terrainData;
			int res = data.heightmapResolution;
			if (res < 2)
			{
				return;
			}
			float[,] heights = data.GetHeights(0, 0, res, res);
			Vector3 origin = terrain.GetPosition();
			Vector3 size = data.size;
			float stepX = size.x / (res - 1), stepZ = size.z / (res - 1);
			float texelW = bounds.width / w, texelH = bounds.height / h;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((origin.x - bounds.xMin) / texelW), 0, w - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((origin.x + size.x - bounds.xMin) / texelW), 0, w);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((origin.z - bounds.yMin) / texelH), 0, h - 1);
			int z1 = Mathf.Clamp(Mathf.CeilToInt((origin.z + size.z - bounds.yMin) / texelH), 0, h);
			for (int z = z0; z < z1; z++)
			{
				float v = (bounds.yMin + (z + 0.5f) * texelH - origin.z) / size.z;
				if (v < 0f || v > 1f)
				{
					continue;
				}
				int hz = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
				int hz0 = Mathf.Max(0, hz - 1), hz1 = Mathf.Min(res - 1, hz + 1);
				for (int x = x0; x < x1; x++)
				{
					float u = (bounds.xMin + (x + 0.5f) * texelW - origin.x) / size.x;
					if (u < 0f || u > 1f)
					{
						continue;
					}
					int hx = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1);
					int hx0 = Mathf.Max(0, hx - 1), hx1 = Mathf.Min(res - 1, hx + 1);
					float dhdx = (heights[hz, hx1] - heights[hz, hx0]) * size.y / Mathf.Max(1e-4f, (hx1 - hx0) * stepX);
					float dhdz = (heights[hz1, hx] - heights[hz0, hx]) * size.y / Mathf.Max(1e-4f, (hz1 - hz0) * stepZ);
					Vector3 n = new Vector3(-dhdx, 1f, -dhdz).normalized;
					normals[z * w + x] = new Color32(
						(byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 0, 255),
						255);
				}
			}
		}

		private static void Paint(Terrain terrain, TerrainArraySet set, Rect bounds, int w, int h, Color32[] pixels, Color32[] shares)
		{
			TerrainData data = terrain.terrainData;
			TerrainLayer[] layers = data.terrainLayers ?? new TerrainLayer[0];
			Texture2D[] controls = data.alphamapTextures;
			bool hasControls = layers.Length > 0 && controls != null && controls.Length > 0;
			/* A client build's tile keeps no alphamaps (nor layers) once its stored index map stands in for them
			 * (ClientTerrainLayerStripper): the map says which of the set's layers lie where, four a texel. Where a texel has
			 * four layers or fewer that is its whole paint, so the colour is the same; past four, the fifth and later are left
			 * out, as the terrain itself draws them. */
			TerrainDetailBake.IndexMap stored = hasControls ? null : SceneTerrainBake.IndexOf(data);
			int count = hasControls ? layers.Length : stored != null && set != null ? Mathf.Min(stored.Layers, set.LayerCount) : 0;
			if (count == 0)
			{
				return;
			}
			/* A tile carrying only the layers it paints (TerrainTileLayers) numbers its channels its own way: channel l is the
			 * set's slice sceneLayers[l]. The stored map's ids are the set's own already. */
			int[] sceneLayers = hasControls ? TerrainTileLayers.Of(terrain) : null;
			if (!hasControls && set != null)
			{
				// The stored map names the set's layers, which a compacted tile's own count does not bound.
				count = set.LayerCount;
			}
			var colours = new Color[count];
			// What each layer is made of for the looks: the set's own (by its baked names, as the terrain shader is given
			// them), or the layer's name where no set draws the terrain.
			Vector4[] setFlora = set != null ? set.LayerFlora() : null;
			var flora = new Vector4[count];
			for (int l = 0; l < count; l++)
			{
				int slice = TerrainTileLayerMap.SceneLayerOf(sceneLayers, l);
				bool drawable = l < layers.Length || (set != null && set.Albedo != null && slice < set.LayerCount && slice < set.Albedo.depth);
				colours[l] = drawable ? LayerColour(set, layers, l, slice, out _) : new Color(0.5f, 0.5f, 0.5f, 1f);
				if (setFlora != null && slice < set.LayerCount && slice < setFlora.Length)
				{
					flora[l] = setFlora[slice];
				}
				else
				{
					Shared.Biomes.BiomeLooks.GroundShare share = Shared.Biomes.BiomeLooks.GroundShareOf(l < layers.Length && layers[l] != null ? layers[l].name : null);
					flora[l] = new Vector4(share.Foliage, share.Accent, share.Mineral, 0f);
				}
			}
			// The control maps (RGBA = four layers' weights each), read once as bytes; GetAlphamaps (floats,
			// far more memory) only if a control map cannot be read on the CPU.
			var weights = new Color32[hasControls ? controls.Length : 0][];
			int res = stored != null ? stored.Resolution : 0;
			bool readable = true;
			for (int c = 0; c < weights.Length && readable; c++)
			{
				Texture2D control = controls[c];
				readable = control != null && control.isReadable;
				if (readable)
				{
					weights[c] = control.GetPixels32();
					res = control.width;
				}
			}
			if (hasControls && !readable)
			{
				res = data.alphamapResolution;
				float[,,] alphas = data.GetAlphamaps(0, 0, res, res);
				for (int c = 0; c < controls.Length; c++)
				{
					weights[c] = new Color32[res * res];
				}
				for (int zz = 0; zz < res; zz++)
				{
					for (int xx = 0; xx < res; xx++)
					{
						for (int l = 0; l < Mathf.Min(layers.Length, alphas.GetLength(2)); l++)
						{
							ref Color32 cw = ref weights[l >> 2][zz * res + xx];
							byte wb = (byte)Mathf.Clamp(Mathf.RoundToInt(alphas[zz, xx, l] * 255f), 0, 255);
							switch (l & 3)
							{
								case 0: cw.r = wb; break;
								case 1: cw.g = wb; break;
								case 2: cw.b = wb; break;
								default: cw.a = wb; break;
							}
						}
					}
				}
			}

			Vector3 origin = terrain.GetPosition();
			Vector3 size = data.size;
			float texelW = bounds.width / w, texelH = bounds.height / h;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((origin.x - bounds.xMin) / texelW), 0, w - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((origin.x + size.x - bounds.xMin) / texelW), 0, w);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((origin.z - bounds.yMin) / texelH), 0, h - 1);
			int z1 = Mathf.Clamp(Mathf.CeilToInt((origin.z + size.z - bounds.yMin) / texelH), 0, h);
			for (int z = z0; z < z1; z++)
			{
				float v = (bounds.yMin + (z + 0.5f) * texelH - origin.z) / size.z;
				if (v < 0f || v > 1f)
				{
					continue;
				}
				int cz = Mathf.Min(res - 1, (int)(v * res));
				for (int x = x0; x < x1; x++)
				{
					float u = (bounds.xMin + (x + 0.5f) * texelW - origin.x) / size.x;
					if (u < 0f || u > 1f)
					{
						continue;
					}
					int cx = Mathf.Min(res - 1, (int)(u * res));
					int k = cz * res + cx;
					float r = 0f, g = 0f, b = 0f, total = 0f;
					Vector4 share = Vector4.zero;
					if (stored != null)
					{
						WeighIndexTexel(stored.Map[2 * k], stored.Map[2 * k + 1], count, colours, flora, ref r, ref g, ref b, ref share, ref total);
					}
					for (int l = 0; l < weights.Length * 4 && l < count; l++)
					{
						Color32 cw = weights[l >> 2][k];
						byte wb = (l & 3) == 0 ? cw.r : (l & 3) == 1 ? cw.g : (l & 3) == 2 ? cw.b : cw.a;
						if (wb == 0)
						{
							continue;
						}
						float weight = wb / 255f;
						r += colours[l].r * weight;
						g += colours[l].g * weight;
						b += colours[l].b * weight;
						share += flora[l] * weight;
						total += weight;
					}
					if (total > 1e-3f)
					{
						pixels[z * w + x] = new Color(r / total, g / total, b / total, 1f);
						share /= total;
						shares[z * w + x] = new Color(share.x, share.y, share.z, 1f);
					}
				}
			}
		}

		/// <summary>
		/// One texel of a stored index map (<see cref="TerrainIndexMap"/>) into a colour sum: each of its four layers' colour and
		/// make-up by its weight byte over 255, as the alphamap path weighs each layer's byte. Ids past
		/// <paramref name="count"/> (zero-weight phantoms of a palette under four layers) add nothing.
		/// </summary>
		public static void WeighIndexTexel(uint ids, uint packedWeights, int count, Color[] colours, Vector4[] flora,
			ref float r, ref float g, ref float b, ref Vector4 share, ref float total)
		{
			for (int s = 0; s < TerrainIndexMap.Slots; s++)
			{
				int wb = TerrainIndexMap.WeightOf(packedWeights, s);
				int l = TerrainIndexMap.IdOf(ids, s);
				if (wb == 0 || l >= count)
				{
					continue;
				}
				float weight = wb / 255f;
				r += colours[l].r * weight;
				g += colours[l].g * weight;
				b += colours[l].b * weight;
				share += flora[l] * weight;
				total += weight;
			}
		}

		/// <summary>
		/// The terrain array set bound to each active terrain (TerrainArrayBinder), and whether any binder is
		/// still without one (its set loading).
		/// </summary>
		private static Dictionary<Terrain, TerrainArraySet> ArraySets(out bool missing)
		{
			var result = new Dictionary<Terrain, TerrainArraySet>();
			missing = false;
			foreach (TerrainArrayBinder binder in Object.FindObjectsByType<TerrainArrayBinder>())
			{
				TerrainArraySet set = binder.BoundSet;
				if (set == null || set.Albedo == null)
				{
					missing = true;
					continue;
				}
				foreach (Terrain t in binder.EffectiveTerrains())
				{
					if (t != null)
					{
						result[t] = set;
					}
				}
			}
			return result;
		}

		private static bool ArraysReady()
		{
			ArraySets(out bool missing);
			return !missing;
		}

		/// <summary>
		/// Layer <paramref name="index"/>'s colour as the terrain draws it: its slice of the bound albedo array
		/// times its tint (the terrain shader's own arithmetic), or — no set — the layer's own diffuse texture.
		/// </summary>
		/// <param name="index">The tile's channel (its layer in <paramref name="layers"/>).</param>
		/// <param name="slice">The scene layer that channel holds: the set's slice (TerrainTileLayers; the channel itself on a tile carrying the whole palette).</param>
		private static Color LayerColour(TerrainArraySet set, TerrainLayer[] layers, int index, int slice, out bool fromArray)
		{
			fromArray = set != null && set.Albedo != null && slice < set.LayerCount && slice < set.Albedo.depth;
			if (!fromArray)
			{
				return MeanOf(index < layers.Length ? layers[index] : null);
			}
			index = slice;
			var key = ((Texture)set.Albedo, index);
			if (!sliceMeans.TryGetValue(key, out Color mean))
			{
				mean = Average(set.Albedo, index);
				sliceMeans[key] = mean;
			}
			Vector4 tint = set.LayerTint != null && index < set.LayerTint.Length ? set.LayerTint[index] : Vector4.one;
			return new Color(mean.r * tint.x, mean.g * tint.y, mean.b * tint.z, 1f);
		}

		/// <summary>
		/// A layer's average colour (sRGB): its diffuse map blitted to 32×32 and averaged, times the layer's
		/// remap maximum (its tint). Cached by texture; grey for a layer with no map.
		/// </summary>
		private static Color MeanOf(TerrainLayer layer)
		{
			Texture texture = layer != null ? layer.diffuseTexture : null;
			if (texture == null)
			{
				return new Color(0.5f, 0.5f, 0.5f, 1f);
			}
			if (!means.TryGetValue(texture, out Color mean))
			{
				mean = Average(texture, -1);
				means[texture] = mean;
			}
			Vector4 remap = layer.diffuseRemapMax;
			return remap == Vector4.zero ? mean : new Color(mean.r * remap.x, mean.g * remap.y, mean.b * remap.z, 1f);
		}

		/// <summary>The average colour of a texture, or of one slice of an array (<paramref name="slice"/> ≥ 0).</summary>
		private static Color Average(Texture texture, int slice)
		{
			const int Side = 32;
			RenderTexture rt = RenderTexture.GetTemporary(Side, Side, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			RenderTexture previous = RenderTexture.active;
			var read = new Texture2D(Side, Side, TextureFormat.RGBA32, false, false);
			try
			{
				if (slice >= 0)
				{
					Graphics.Blit(texture, rt, slice, 0);
				}
				else
				{
					Graphics.Blit(texture, rt);
				}
				RenderTexture.active = rt;
				read.ReadPixels(new Rect(0, 0, Side, Side), 0, 0, false);
				read.Apply(false, false);
				Color32[] px = read.GetPixels32();
				long r = 0, g = 0, b = 0;
				foreach (Color32 c in px)
				{
					r += c.r;
					g += c.g;
					b += c.b;
				}
				float n = px.Length * 255f;
				return new Color(r / n, g / n, b / n, 1f);
			}
			finally
			{
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(rt);
				// In edit mode too: the editor's bake works the map out outside play.
				if (Application.isPlaying)
				{
					Object.Destroy(read);
				}
				else
				{
					Object.DestroyImmediate(read);
				}
			}
		}
	}
}
