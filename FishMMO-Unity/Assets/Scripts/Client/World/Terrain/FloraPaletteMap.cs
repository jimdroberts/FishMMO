using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The looks of the scene's biomes on its world (<see cref="BiomeLooks"/>), handed to the shaders: which biome lies where,
	/// and each biome's twelve looks. Grass, flowers, small plants, fungi and stones pick a look per patch of ground
	/// (FishFlora.hlsl); the ambient animals ask <see cref="LookAt"/> once per group.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two small textures and five globals,</b> rebuilt when a scene loads or unloads:
	/// </para>
	/// <list type="bullet">
	/// <item><c>_FishFloraBiomes</c>: R8, the scene's biome map grid, each texel the biome's index in the palette (0 = the
	/// scene's own look, for ground the map leaves blank).</item>
	/// <item><c>_FishFloraPalette</c>: RGBAHalf, <see cref="BiomeLooks.Variants"/> wide and <see cref="Rows"/> rows per biome,
	/// linear colours: foliage (a = running weight), root (a = height), bloom (a = bloom size), accent (a = width), mineral
	/// (a = bend).</item>
	/// <item><c>_FishFloraRect</c> (origin xz, 1/size xz), <c>_FishFloraInfo</c> (on, grid w, grid h, biomes),
	/// <c>_FishFloraStrength</c> (foliage, bloom, accent, mineral: how far each takes its look), <c>_FishFloraGround</c> (the
	/// world's bare-ground colour, linear, a = how far mineral ground layers take it: <see cref="BiomeLooks.WorldTraits.Ground"/>).</item>
	/// </list>
	/// <para>
	/// Off (<c>_FishFloraInfo.x</c> = 0) until built, and while <see cref="Enabled"/> is false: every shader then draws as
	/// it did before. Grid only, no smoothing: the shaders dither biome borders by a jittered read.
	/// </para>
	/// </remarks>
	public static class FloraPaletteMap
	{
		/// <summary>
		/// Palette rows per biome: foliage, root, bloom, accent, mineral, the living green's shift, and the ground's shift
		/// (<see cref="GroundShift"/>, the same in every column: what the terrain and the ground colour map carry, so a plant
		/// pulled to it can take only its patch's difference from it).
		/// </summary>
		public const int Rows = 7;

		/// <summary>The most biomes a scene's palette carries; the rest share the scene's own look.</summary>
		public const int MaxBiomes = 63;

		private static readonly int BiomesId = Shader.PropertyToID("_FishFloraBiomes");
		private static readonly int PaletteId = Shader.PropertyToID("_FishFloraPalette");
		private static readonly int RectId = Shader.PropertyToID("_FishFloraRect");
		private static readonly int InfoId = Shader.PropertyToID("_FishFloraInfo");
		private static readonly int StrengthId = Shader.PropertyToID("_FishFloraStrength");
		private static readonly int GroundId = Shader.PropertyToID("_FishFloraGround");
		private static readonly int WorldId = Shader.PropertyToID("_FishFloraWorld");

		/// <summary>How far each kind of thing takes its look: grass and leaves, flowers, fungi and lichen, stones.</summary>
		public static Vector4 Strength = new Vector4(0.7f, 1f, 0.75f, 0.6f);

		/// <summary>Play-mode A/B: false draws every biome as before.</summary>
		public static bool Enabled
		{
			get => enabled;
			set
			{
				if (enabled != value)
				{
					enabled = value;
					Publish();
				}
			}
		}
		private static bool enabled = true;

		private static Texture2D biomeTexture, paletteTexture;
		private static Vector4 rect, info, ground, growth = new Vector4(1f, 1f, 0f, 0f);
		private static bool hooked, dirty = true, published;
		private static int builtFrame = -1, retryFrame = -1;

		/// <summary>How long to wait before building again while some of the map's biomes are not registered.</summary>
		private const int RetryFrames = 60;

		/// <summary>How many such retries before the missing biomes are taken as gone (about half a minute).</summary>
		private const int MaxRetries = 30;
		private static int retries;

		/// <summary>The scene's looks: index 0 its own, then one per biome on the map, by <see cref="biomeIndex"/>.</summary>
		private static readonly List<BiomeLooks.Look> looks = new List<BiomeLooks.Look>();
		private static readonly Dictionary<int, int> biomeIndex = new Dictionary<int, int>();
		private static SceneBiomeMap activeMap;
		private static string described = "not built";

		/// <summary>The grid as published (each texel its biome's palette index), for <see cref="GroundLooks"/>.</summary>
		private static byte[] gridIndices;
		private static int gridWidth, gridHeight;
		private static GroundLookTable groundLooks;

		/// <summary>
		/// Bumped whenever what the shaders are given changes: built, released, switched on or off (<see cref="Enabled"/>), or
		/// retuned (<see cref="Republish"/>). The CPU copies of the ground's colour (GroundColourMap) compare it to know
		/// when to wear the looks again.
		/// </summary>
		public static int Version { get; private set; }

		/// <summary>
		/// What the CPU's ground colour (GroundColourMap) takes of the looks: per biome on the map the shift its living ground
		/// takes (<see cref="GroundShift"/>, as FishFlora.hlsl FishFloraGroundShiftAt draws the terrain), and the grid that
		/// says which biome lies where. Linear light throughout.
		/// </summary>
		public sealed class GroundLookTable
		{
			/// <summary>Per palette index: the living ground's shift (<see cref="GroundShift"/>), linear.</summary>
			public System.Numerics.Vector3[] Shift;
			/// <summary>How far each takes its look (<see cref="Strength"/>), and the world's bare ground (rgb linear, a strength).</summary>
			public Vector4 Strength, Ground;
			public byte[] Indices;
			public int Width, Height;
			/// <summary>The grid's world xz origin and one over its size (zw 0 when the scene has no map: every point is index 0).</summary>
			public Vector4 Rect;

			/// <summary>The palette index under a world xz: the grid's texel, unjittered (the shader dithers its borders).</summary>
			public int IndexAt(float x, float z)
			{
				if (Rect.z <= 0f || Indices == null)
				{
					return 0;
				}
				float u = (x - Rect.x) * Rect.z, v = (z - Rect.y) * Rect.w;
				if (u < 0f || v < 0f || u > 1f || v > 1f)
				{
					return 0;
				}
				int tx = Mathf.Clamp((int)(u * Width), 0, Width - 1), tz = Mathf.Clamp((int)(v * Height), 0, Height - 1);
				int index = Indices[tz * Width + tx];
				return index < Shift.Length ? index : 0;
			}
		}

		/// <summary>
		/// The looks as the CPU's ground colour wears them, or false while they are not drawn (nothing built, or
		/// <see cref="Enabled"/> off): the ground's colour is then the terrain's own, as before.
		/// </summary>
		public static bool GroundLooks(out GroundLookTable table)
		{
			table = null;
			if (!published || !enabled || looks.Count == 0 || biomeTexture == null || paletteTexture == null)
			{
				return false;
			}
			if (groundLooks == null)
			{
				var t = new GroundLookTable
				{
					Shift = new System.Numerics.Vector3[looks.Count],
					Indices = gridIndices,
					Width = gridWidth,
					Height = gridHeight,
					Rect = rect,
				};
				for (int b = 0; b < looks.Count; b++)
				{
					Color shift = GroundShift(looks[b]);
					t.Shift[b] = new System.Numerics.Vector3(shift.r, shift.g, shift.b);
				}
				groundLooks = t;
			}
			groundLooks.Strength = Strength;
			groundLooks.Ground = ground;
			table = groundLooks;
			return true;
		}

		private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("FloraPaletteMap.Build");

		/// <summary>A line for diagnostics: what was built.</summary>
		public static string Describe() => described;

		/// <summary>
		/// The looks of the biome under <paramref name="position"/> in the loaded scene, or false when nothing is built. The
		/// same looks the shaders draw there.
		/// </summary>
		public static bool LookAt(Vector3 position, out BiomeLooks.Look look)
		{
			look = null;
			if (!published || looks.Count == 0)
			{
				return false;
			}
			int id = activeMap != null ? activeMap.IDAt(position) : 0;
			look = id != 0 && biomeIndex.TryGetValue(id, out int index) ? looks[index] : looks[0];
			return true;
		}

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
			BiomeRegistry.Changed += OnBiomesChanged;
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
			BiomeRegistry.Changed -= OnBiomesChanged;
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

		/// <summary>
		/// Biomes register as their assets load, which can be after the scene's first frame: a palette built before then
		/// resolved none of the map's biomes and drew every patch in a stand-in look (2026-10-15, "[Flora] 0 biome(s) … grid
		/// 200×266"). A change in the registry rebuilds it.
		/// </summary>
		private static void OnBiomesChanged() => dirty = true;

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			if (!dirty && retryFrame >= 0 && Time.frameCount >= retryFrame)
			{
				dirty = true;
			}
			if (!dirty || builtFrame == Time.frameCount)
			{
				return;
			}
			dirty = false;
			builtFrame = Time.frameCount;
			using (BuildMarker.Auto())
			{
				Build();
			}
		}

		/// <summary>The world scene whose looks to draw: the first loaded one with settings, its biome map preferred.</summary>
		private static WorldSceneSettings FindSettings()
		{
			WorldSceneSettings first = null;
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded || !WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings))
				{
					continue;
				}
				if (settings.BiomeMap != null)
				{
					return settings;
				}
				first ??= settings;
			}
			return first;
		}

		private static void Build()
		{
			WorldSceneSettings settings = FindSettings();
			looks.Clear();
			biomeIndex.Clear();
			activeMap = null;
			groundLooks = null;
			if (settings == null)
			{
				Release();
				described = "no world scene";
				return;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = ScenePlacementClimate.ResolveBody(system, settings.AtlasEntry);
			BiomeLooks.WorldTraits world = BiomeLooks.WorldOf(system, body);
			SceneBiomeMap map = settings.BiomeMap;

			// The biomes on the map, commonest first, so the scene's own look (index 0) is its commonest biome's.
			var counts = new Dictionary<int, int>();
			if (map != null && map.BiomeIDs != null)
			{
				foreach (int id in map.BiomeIDs)
				{
					if (id != 0)
					{
						counts.TryGetValue(id, out int n);
						counts[id] = n + 1;
					}
				}
			}
			var ordered = new List<KeyValuePair<int, int>>(counts);
			ordered.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
			BiomeTemplate commonest = ordered.Count > 0 && BiomeRegistry.TryGetByID(ordered[0].Key, out BiomeTemplate top) ? top : null;
			looks.Add(commonest != null ? BiomeLooks.Of(commonest, world) : BiomeLooks.For(BiomeLookClass.Temperate, 0u, world));
			int unresolved = 0;
			foreach (KeyValuePair<int, int> entry in ordered)
			{
				if (!BiomeRegistry.TryGetByID(entry.Key, out BiomeTemplate biome) || biome == null)
				{
					unresolved++;
					continue;
				}
				if (looks.Count > MaxBiomes)
				{
					continue;
				}
				biomeIndex[entry.Key] = looks.Count;
				looks.Add(BiomeLooks.Of(biome, world));
			}
			if (unresolved > 0 && retries < MaxRetries)
			{
				// Not loaded yet (or a map naming a biome that is gone): try again shortly, as well as on the registry's word.
				if (retries == 0)
				{
					Debug.Log($"[Flora] {unresolved} of the map's {ordered.Count} biome(s) not registered yet; their ground takes the scene's look until they are.");
				}
				retries++;
				retryFrame = Time.frameCount + RetryFrames;
			}
			else
			{
				if (unresolved > 0)
				{
					Debug.LogWarning($"[Flora] {unresolved} of the map's {ordered.Count} biome(s) never registered; their ground keeps the scene's look.");
				}
				retries = 0;
				retryFrame = -1;
			}

			// The grid: each texel its biome's index.
			int w = map != null ? Mathf.Max(1, map.Width) : 1, h = map != null ? Mathf.Max(1, map.Height) : 1;
			var indices = new byte[w * h];
			if (map != null && map.BiomeIDs != null && map.BiomeIDs.Length == w * h)
			{
				for (int i = 0; i < indices.Length; i++)
				{
					indices[i] = (byte)(biomeIndex.TryGetValue(map.BiomeIDs[i], out int index) ? index : 0);
				}
				rect = new Vector4(map.WorldOrigin.x, map.WorldOrigin.y, 1f / Mathf.Max(1e-3f, map.WorldSize.x), 1f / Mathf.Max(1e-3f, map.WorldSize.y));
			}
			else
			{
				rect = new Vector4(0f, 0f, 0f, 0f);
			}
			EnsureTexture(ref biomeTexture, w, h, TextureFormat.R8, "Flora Biomes");
			biomeTexture.SetPixelData(indices, 0);
			biomeTexture.Apply(false, false);

			// The palette: twelve looks a column, five rows a biome, linear light.
			int rows = looks.Count * Rows;
			EnsureTexture(ref paletteTexture, BiomeLooks.Variants, rows, TextureFormat.RGBAHalf, "Flora Palette");
			var pixels = new Color[BiomeLooks.Variants * rows];
			for (int b = 0; b < looks.Count; b++)
			{
				BiomeLooks.Look look = looks[b];
				Color groundShift = GroundShift(look);
				for (int v = 0; v < BiomeLooks.Variants; v++)
				{
					BiomeLooks.Variant variant = look.Variants[v];
					int row = b * Rows;
					pixels[(row + 6) * BiomeLooks.Variants + v] = groundShift;
					pixels[(row + 0) * BiomeLooks.Variants + v] = Linear(variant.Foliage, look.Cumulative[v]);
					pixels[(row + 1) * BiomeLooks.Variants + v] = Linear(variant.Root, variant.Height);
					pixels[(row + 2) * BiomeLooks.Variants + v] = Linear(variant.Bloom, variant.BloomSize);
					pixels[(row + 3) * BiomeLooks.Variants + v] = Linear(variant.Accent, variant.Width);
					pixels[(row + 4) * BiomeLooks.Variants + v] = Linear(variant.Mineral, variant.Bend);
					// Already linear: a multiplier, not a colour.
					pixels[(row + 5) * BiomeLooks.Variants + v] = new Color(variant.Shift.r, variant.Shift.g, variant.Shift.b, 1f);
				}
			}
			paletteTexture.SetPixels(pixels);
			paletteTexture.Apply(false, false);

			gridIndices = indices;
			gridWidth = w;
			gridHeight = h;
			groundLooks = null;
			info = new Vector4(1f, w, h, looks.Count);
			Color groundLinear = world.Ground.linear;
			ground = new Vector4(groundLinear.r, groundLinear.g, groundLinear.b, world.GroundStrength);
			Vector2 grows = BiomeLooks.GrowthOf(world);
			growth = new Vector4(grows.x, grows.y, 0f, 0f);
			activeMap = map;
			published = true;
			Publish();
			described = $"{looks.Count - 1} biome(s) on '{(body != null ? body.ResolvedName : "the home world")}' (star {world.StarKelvin:0} K, " +
				$"{world.Atmosphere} air{(world.GroundStrength > 0f ? $", ground #{ColorUtility.ToHtmlStringRGB(world.Ground)}" : "")}), grid {w}×{h}; scene look {looks[0].Class}";
			Debug.Log($"[Flora] {described}.");
		}

		/// <summary>
		/// The shift a biome's living ground takes (linear): its common look's, which is what the planet changes (the star's
		/// light, the air) and nothing else, white on an Earth-like world, so the ground's own textures are drawn as made. The
		/// other eleven looks are the patches the grass and the plants wear; the ground wears none of them, nor their mean.
		/// </summary>
		public static Color GroundShift(BiomeLooks.Look look)
		{
			Color shift = look.Variants != null && look.Variants.Length > 0 ? look.Variants[0].Shift : Color.white;
			shift.a = 1f;
			return shift;
		}

		/// <summary>An sRGB colour in linear light, with <paramref name="alpha"/> carrying a number.</summary>
		private static Color Linear(Color srgb, float alpha)
		{
			Color c = srgb.linear;
			c.a = alpha;
			return c;
		}

		private static void EnsureTexture(ref Texture2D texture, int width, int height, TextureFormat format, string name)
		{
			if (texture != null && texture.width == width && texture.height == height && texture.format == format)
			{
				return;
			}
			if (texture != null)
			{
				Object.Destroy(texture);
			}
			texture = new Texture2D(width, height, format, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
		}

		private static void Publish()
		{
			bool on = published && enabled && biomeTexture != null && paletteTexture != null;
			if (biomeTexture != null)
			{
				Shader.SetGlobalTexture(BiomesId, biomeTexture);
			}
			if (paletteTexture != null)
			{
				Shader.SetGlobalTexture(PaletteId, paletteTexture);
			}
			Shader.SetGlobalVector(RectId, rect);
			Shader.SetGlobalVector(InfoId, on ? info : Vector4.zero);
			Shader.SetGlobalVector(StrengthId, Strength);
			Shader.SetGlobalVector(GroundId, on ? ground : Vector4.zero);
			Shader.SetGlobalVector(WorldId, on ? growth : new Vector4(1f, 1f, 0f, 0f));
			Version++;
		}

		/// <summary>Takes the palette down and switches the looks off.</summary>
		public static void Release()
		{
			published = false;
			Publish();
			looks.Clear();
			biomeIndex.Clear();
			activeMap = null;
			gridIndices = null;
			groundLooks = null;
			if (biomeTexture != null)
			{
				Object.Destroy(biomeTexture);
				biomeTexture = null;
			}
			if (paletteTexture != null)
			{
				Object.Destroy(paletteTexture);
				paletteTexture = null;
			}
		}

		/// <summary>Re-reads <see cref="Strength"/> into the globals (after a tuning change in play).</summary>
		public static void Republish() => Publish();
	}
}
