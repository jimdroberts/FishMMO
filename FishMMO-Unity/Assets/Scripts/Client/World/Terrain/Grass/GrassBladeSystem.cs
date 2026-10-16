using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// The procedural blade grass (RDR2 / Ghost of Tsushima style): takes the grass detail prototypes
	/// (<see cref="GrassBladeSettings.PrototypePrefixes"/>, default "Detail_Grass") of every terrain the
	/// instanced detail renderer drives, and draws them as thin, individually shaped blades regenerated on
	/// the GPU every frame around each camera (<see cref="GrassBladeRenderer"/>, FishGrassBlades.compute,
	/// FishMMO/Grass Blades) in place of the clump meshes, which <see cref="TerrainDetailInstancing"/> then
	/// skips on that terrain (<see cref="TerrainDetailInstancing.SetSkipped"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where grass grows</b> is still the scatter's: each grass prototype's detail layer (biome rules,
	/// shore gate, slope) becomes a density map at take-on, once per terrain (CPU, one terrain a frame).
	/// The blades are a deterministic function of their world lattice cell (<see cref="GrassMath"/>), so the
	/// same blades stand in the same places every frame and on every terrain border.
	/// </para>
	/// <para>
	/// <b>When.</b> Play mode, client, compute-capable devices, and only on terrains the detail renderer
	/// drives — so Unity's own detail drawing never draws the clumps under the blades. Everywhere else
	/// (WebGL2, the detail renderer off, edit mode) the mesh grass draws exactly as before. The dashboard's
	/// It is the default wherever compute runs; the dashboard's "Toggle old mesh grass" opts back into the
	/// instanced clump meshes per editor session (SessionState <see cref="MeshGrassKey"/>, off by default).
	/// Reflection cameras draw no blades.
	/// </para>
	/// </remarks>
	public static class GrassBladeSystem
	{
		/// <summary>
		/// SessionState key (editor): true draws the old instanced clump meshes instead of the blades. A new key,
		/// opt-in and false by default, so a switch-off left over from testing the A/B no longer sticks.
		/// </summary>
		public const string MeshGrassKey = "FishMMO.ProceduralGrass.UseMeshGrass";

		private sealed class Taken
		{
			public Terrain Terrain;
			public TerrainData Data;
			public GrassTerrain Grass;
		}

		private static readonly Dictionary<Terrain, Taken> taken = new Dictionary<Terrain, Taken>();

		/// <summary>The blade grass's key in <see cref="TerrainDetailInstancing.SetSkipped"/> (the GPU detail scatter has its own).</summary>
		private static readonly object SkipOwner = new object();

		/// <summary>
		/// Milliseconds a frame of the main thread the terrain being taken may spend reading its layers, heights and maps
		/// (the arithmetic on them runs on a worker: GrassTerrain.Builder). Was 2 with the arithmetic on the main thread.
		/// </summary>
		public const double BuildBudgetMilliseconds = 1.0;

		/// <summary>
		/// The budget while the terrain being built is within the grass distance of the camera: its grass is on screen. Was 8
		/// with the arithmetic on the main thread (6–8 ms spikes as terrains streamed in); the reads alone fit in 1.
		/// </summary>
		public const double VisibleBuildBudgetMilliseconds = 1.0;

		/// <summary>
		/// Milliseconds a frame of the main thread spent taking terrains from the scene's bake (TerrainDetailBake): each is a
		/// few textures made from its maps, so several go in a frame, and the atlas is rebuilt once for them, not once each.
		/// </summary>
		public const double BakedBudgetMilliseconds = 4.0;

		/// <summary>The terrain whose grass is being built, a strip a frame (its mesh grass draws until it is done).</summary>
		private static GrassTerrain.Builder building;
		/// <summary>What has been taken since the last summary line: one line per batch, not one per terrain.</summary>
		private static int summaryTerrains, summaryFrames, summaryChannels, summaryBaked;
		/// <summary>The frame to log one <see cref="Describe"/> line with the GPU's real blade counts, after a batch is taken (−1 none).</summary>
		private static int describeAt = -1;
		private static bool statsWereOn;
		private static double summaryMilliseconds;

		private static readonly List<GrassTerrain> grassTerrains = new List<GrassTerrain>();
		/// <summary>Terrains with no blade grass (or refused), with the data they had: not looked at again until it changes.</summary>
		private static readonly Dictionary<Terrain, TerrainData> none = new Dictionary<Terrain, TerrainData>();
		/// <summary>The type table's keys, parallel to <see cref="types"/>: each entry's prefab and look (<see cref="GrassTypeTable"/>).</summary>
		private static readonly List<(GameObject Prefab, string Look)> typeKeys = new List<(GameObject, string)>();
		private static readonly List<GrassType> types = new List<GrassType>();
		private static readonly List<Terrain> active = new List<Terrain>();
		private static readonly List<int> prototypeScratch = new List<int>();
		private static readonly List<GrassType> typeScratch = new List<GrassType>();
		private static readonly List<Taken> releasing = new List<Taken>();
		/// <summary>Terrains whose build failed on a worker once (taken again once, then left to the mesh grass).</summary>
		private static readonly HashSet<Terrain> failedOnce = new HashSet<Terrain>();
		private static readonly GrassBladeSettings defaults = new GrassBladeSettings();
		private static GrassBladeRenderer renderer;
		private static bool rendererTried;
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;
		private static float grassDistance = ClientGrassSettings.DefaultDistance;
		private static readonly HashSet<string> warned = new HashSet<string>();
		/// <summary>The next frame to look for terrains' array sets while any taken terrain has none (a binder loads its set by address).</summary>
		private static int arraysCheckAt;
		private const int ArraysCheckInterval = 30;
		/// <summary>
		/// True when a prototype took the old look because its biome was not registered yet (the registry loads by address):
		/// the terrains are taken again when it changes, so every blade wears its own biome's look.
		/// </summary>
		private static bool biomesPending;

		/// <summary>The session's switch (A/B); off gives every grass prototype back to the mesh renderer.</summary>
		public static bool Enabled
		{
			get => enabled;
			set
			{
				if (enabled == value)
				{
					return;
				}
				enabled = value;
				if (!value)
				{
					ReleaseAll();
				}
				syncedFrame = -1;
			}
		}

		public static bool Running => renderer != null && taken.Count > 0;
		public static int TerrainCount => taken.Count;
		/// <summary>Whether a terrain is being taken on now (its build in progress).</summary>
		public static bool Building => building != null;
		public static int TypeCount => types.Count;
		public static GrassBladeRenderer Renderer => renderer;

		/// <summary>
		/// The blade grass standing at a world point, metres: the tallest type there weighted by its density (0 where none
		/// grows, off every taken terrain, or before its maps are built). The ambient life reads it so ground animals forage
		/// where they can be seen.
		/// </summary>
		public static float CoverAt(float x, float z)
		{
			for (int i = 0; i < grassTerrains.Count; i++)
			{
				GrassTerrain gt = grassTerrains[i];
				if (gt != null && x >= gt.Origin.x && z >= gt.Origin.z && x <= gt.Origin.x + gt.Size.x && z <= gt.Origin.z + gt.Size.z)
				{
					return gt.CoverAt(x, z);
				}
			}
			return 0f;
		}
		public static int LastDrawCount { get; private set; }

		/// <summary>A line for the dashboard: state, terrains, types, the last camera's work and (with stats on) blades.</summary>
		public static string Describe()
		{
			if (!enabled)
			{
				return "Procedural grass OFF (the grass prototypes draw as meshes).";
			}
			if (renderer == null)
			{
				return rendererTried ? "Procedural grass unavailable on this device or without its assets (the mesh grass draws); see the log." : "Procedural grass not started (enter play mode on a terrain scene).";
			}
			var s = new System.Text.StringBuilder();
			s.Append($"Procedural grass ON: {taken.Count} terrain(s){(building != null ? $" (building '{building.Terrain?.name}': {building.Progress}, {building.Frames} frames, {building.Milliseconds:F0} ms)" : "")}, {types.Count} type(s) [");
			for (int i = 0; i < types.Count; i++)
			{
				s.Append(i > 0 ? ", " : "").Append(types[i].Name).Append($" {types[i].Height:F2} m{(types[i].ColoursFromMesh ? "" : " (fallback colours)")} [{types[i].Source}]");
			}
			s.Append($"]; last game camera {renderer.LastCamera}: {renderer.LastTiles} tiles, {renderer.LastItems} work items, {renderer.LastCandidates} candidate threads; GPU buffers {renderer.GpuBytes / (1024f * 1024f):F1} MiB");
			if (renderer.LastBlades[0] >= 0)
			{
				s.Append($"; blades appended LOD0/1/2 {renderer.LastBlades[0]}/{renderer.LastBlades[1]}/{renderer.LastBlades[2]} (caps {renderer.Capacity(0)}/{renderer.Capacity(1)}/{renderer.Capacity(2)}), shadow {renderer.LastBlades[3]}/{renderer.LastBlades[4]}");
			}
			// "0 tiles" with terrains taken usually means the ground under the camera is not one of them.
			Terrain under = TerrainUnderCamera();
			if (under != null)
			{
				string state = taken.TryGetValue(under, out Taken t) ? $"blades ({t.Grass.Channels} type channel(s) from {t.Grass.Layers} prototype(s))"
					: building != null && building.Terrain == under ? "building"
					: none.ContainsKey(under) ? "mesh grass (no blade grass painted on it)"
					: !TerrainDetailInstancing.IsDriving(under) ? "not driven by the detail renderer"
					: "waiting to build";
				s.Append($"; under the camera '{under.name}': {state}");
			}
			return s.Append('.').ToString();
		}

		private static Terrain TerrainUnderCamera()
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				return null;
			}
			Terrain.GetActiveTerrains(active);
			foreach (Terrain t in active)
			{
				if (t != null && t.terrainData != null && DistanceToCamera(t) <= 0f)
				{
					return t;
				}
			}
			return null;
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
			warned.Clear();
			enabled = true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			syncedFrame = -1;
#if UNITY_EDITOR
			enabled = !UnityEditor.SessionState.GetBool(MeshGrassKey, false);
#endif
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			Application.quitting += Shutdown;
			grassDistance = ClientGrassSettings.Distance;
			ClientGrassSettings.OnChanged += OnGrassSettingsChanged;
			BiomeRegistry.Changed += OnBiomesChanged;
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
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			Application.quitting -= Shutdown;
			ClientGrassSettings.OnChanged -= OnGrassSettingsChanged;
			BiomeRegistry.Changed -= OnBiomesChanged;
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
			ReleaseAll();
			Unhook();
		}

		private static void OnGrassSettingsChanged() => grassDistance = ClientGrassSettings.Distance;

		/// <summary>A biome registered (or left): terrains taken while a prototype's biome was missing are taken again.</summary>
		private static void OnBiomesChanged()
		{
			if (!biomesPending)
			{
				return;
			}
			biomesPending = false;
			RetakeAll();
		}

		/// <summary>Gives every terrain back and forgets the type table, keeping the renderer: they are all taken again, from the bake where there is one.</summary>
		private static void RetakeAll()
		{
			building?.Abandon();
			building = null;
			foreach (Taken t in new List<Taken>(taken.Values))
			{
				Release(t);
			}
			none.Clear();
			failedOnce.Clear();
			types.Clear();
			typeKeys.Clear();
			syncedFrame = -1;
		}

		/// <summary>This system's once-a-frame work before the cameras render, named in the profiler (StartupTimeline reads it).</summary>
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("GrassBladeSystem.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using (ContextMarker.Auto())
			{
				Sync();
			}
		}

		private static GrassBladeSettings Settings => WeatherRenderProfile.Active?.Grass;

		// ── Following the terrains ────────────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			double start = TerrainInstancingProbe.Now;
			GrassBladeSettings settings = Settings ?? defaults;
			if (!enabled || !settings.Enabled)
			{
				if (taken.Count > 0 || renderer != null)
				{
					ReleaseAll();
				}
				GrassSwardMap.Clear();
				return;
			}
			if (renderer == null && !rendererTried)
			{
				rendererTried = true;
				renderer = GrassBladeRenderer.TryCreate(out string reason);
				if (renderer == null)
				{
					Debug.Log($"[Grass] Procedural blade grass off: {reason}.");
				}
			}
			if (renderer == null)
			{
				return;
			}

			Terrain.GetActiveTerrains(active);
			releasing.Clear();
			foreach (Taken t in taken.Values)
			{
				if (t.Terrain == null || !active.Contains(t.Terrain) || t.Terrain.terrainData != t.Data || !TerrainDetailInstancing.IsDriving(t.Terrain))
				{
					releasing.Add(t);
				}
			}
			foreach (Taken t in releasing)
			{
				Release(t);
			}
			// The type table is the loaded scene's: once no terrain holds an entry, it starts again, so a session that walks
			// through many scenes never fills the GPU's 32 with looks of scenes long gone.
			if (taken.Count == 0 && building == null && types.Count > 0)
			{
				types.Clear();
				typeKeys.Clear();
			}

			// One terrain at a time, a few milliseconds a frame: its density maps read every grass layer at full
			// resolution, which in one go cost 25–240 ms a terrain.
			if (building != null)
			{
				Terrain bt = building.Terrain;
				if (bt == null || !active.Contains(bt) || !TerrainDetailInstancing.IsDriving(bt))
				{
					building.Abandon();
					building = null;
				}
				else if (building.Step(DistanceToCamera(bt) <= grassDistance ? VisibleBuildBudgetMilliseconds : BuildBudgetMilliseconds))
				{
					Finish(building);
					building = null;
				}
			}
			if (building == null)
			{
				// Nearest the camera first: the terrain underfoot used to wait behind every other one (30
				// terrains at 2 ms a frame is hundreds of frames), drawing the old mesh grass meanwhile.
				bool started = false, spent = false;
				var bakedWatch = System.Diagnostics.Stopwatch.StartNew();
				while (!started && !spent)
				{
					Terrain next = null;
					float best = float.MaxValue;
					for (int i = 0; i < active.Count; i++)
					{
						Terrain t = active[i];
						if (t == null || taken.ContainsKey(t) || !TerrainDetailInstancing.IsDriving(t))
						{
							continue;
						}
						if (none.TryGetValue(t, out TerrainData seen) && seen == t.terrainData)
						{
							continue;
						}
						float d = DistanceToCamera(t);
						if (d < best)
						{
							best = d;
							next = t;
						}
					}
					if (next == null)
					{
						break;
					}
					started = TryTake(next, settings);
					// Taken from the scene's bake, it is done at once: handed over now, and the next looked for while the
					// frame's budget for it lasts.
					if (started && building != null && building.Done)
					{
						// Counted with the terrains it adds to (one baked as having nothing is not one of them).
						summaryBaked += building.Result != null ? 1 : 0;
						Finish(building);
						building = null;
						started = false;
						spent = bakedWatch.Elapsed.TotalMilliseconds >= BakedBudgetMilliseconds;
					}
				}
				if (!started && !spent && summaryTerrains > 0)
				{
					Debug.Log($"[Grass] Blade grass on {summaryTerrains} terrain(s) ({types.Count} grass type(s), {summaryChannels} prototype layer(s)); {summaryBaked} taken from the scene's bake, the rest built in {summaryMilliseconds:F0} ms of the main thread over {summaryFrames} frames (up to {BuildBudgetMilliseconds:F0} ms a frame; the arithmetic on workers). The detail renderer skips those prototypes there.");
					summaryTerrains = summaryFrames = summaryChannels = summaryBaked = 0;
					summaryMilliseconds = 0;
					// A couple of seconds later, what the GPU really drew (types, heights, blades per slot vs caps):
					// the readback is only on while it is wanted.
					statsWereOn = GrassBladeRenderer.StatsEnabled;
					GrassBladeRenderer.StatsEnabled = true;
					describeAt = Time.frameCount + 3 * GrassBladeRenderer.StatsInterval + 30;
				}
			}
			if (describeAt >= 0 && Time.frameCount >= describeAt)
			{
				describeAt = -1;
				Debug.Log($"[Grass] {Describe()}");
				GrassBladeRenderer.StatsEnabled = statsWereOn;
			}
			ResolveArrays();
			renderer.Publish(settings, types, grassDistance);
			// The ground under the blades takes their roots' shade (it glowed between them, lit as open ground).
			Camera eye = Camera.main;
			if (eye != null)
			{
				GrassSwardMap.Update(settings, grassDistance, grassTerrains, eye.transform.position);
			}
			TerrainInstancingProbe.SyncMs += TerrainInstancingProbe.Now - start;
		}

		/// <summary>
		/// Gives each taken terrain the array set its ground is drawn with (the blades take their colour from
		/// it). Looked for every <see cref="ArraysCheckInterval"/> frames only while one is still missing.
		/// </summary>
		private static void ResolveArrays()
		{
			if (Time.frameCount < arraysCheckAt)
			{
				return;
			}
			bool missing = false;
			foreach (GrassTerrain gt in grassTerrains)
			{
				if (gt.Arrays == null || !gt.Arrays.IsUsable)
				{
					missing = true;
					break;
				}
			}
			if (!missing)
			{
				return;
			}
			arraysCheckAt = Time.frameCount + ArraysCheckInterval;
			foreach (TerrainArrayBinder binder in Object.FindObjectsByType<TerrainArrayBinder>())
			{
				TerrainArraySet set = binder.BoundSet;
				if (set == null || !set.IsUsable)
				{
					continue;
				}
				foreach (Terrain t in binder.EffectiveTerrains())
				{
					if (t != null && taken.TryGetValue(t, out Taken held))
					{
						held.Grass.Arrays = set;
					}
				}
			}
		}

		/// <summary>Metres from the main camera to a terrain's footprint (0 standing on it; far when there is no camera).</summary>
		private static float DistanceToCamera(Terrain terrain)
		{
			Camera camera = Camera.main;
			if (camera == null || terrain == null || terrain.terrainData == null)
			{
				return float.MaxValue;
			}
			Vector3 p = camera.transform.position;
			Vector3 o = terrain.GetPosition();
			Vector3 s = terrain.terrainData.size;
			float dx = Mathf.Max(0f, Mathf.Max(o.x - p.x, p.x - (o.x + s.x)));
			float dz = Mathf.Max(0f, Mathf.Max(o.z - p.z, p.z - (o.z + s.z)));
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		/// <summary>Starts building a terrain's grass; false (and remembered) when it has none to draw as blades.</summary>
		private static bool TryTake(Terrain terrain, GrassBladeSettings settings)
		{
			TerrainData data = terrain.terrainData;
			SelectBlades(terrain, settings, types, typeKeys, warned, prototypeScratch, typeScratch);
			building = prototypeScratch.Count > 0 ? GrassTerrain.Begin(terrain, prototypeScratch, typeScratch) : null;
			if (building == null)
			{
				none[terrain] = data;
				return false;
			}
			// The scene's bake, when it was made from just this: the terrain is taken from it at once (done), not read.
			FishMMO.Shared.TerrainDetailBake.Tile tile = FishMMO.Shared.SceneTerrainBake.TileOf(data, out _);
			if (tile != null)
			{
				building.TakeBaked(tile);
			}
			return true;
		}

		/// <summary>
		/// A terrain's blade prototypes and each one's type, new types made and added to <paramref name="types"/> (the scene's
		/// table, <paramref name="keys"/> parallel to it; up to the renderer's <see cref="GrassBladeRenderer.MaxTypes"/>). The
		/// play's choice and the editor's bake (TerrainDetailBaker) are this one, so a bake made from the same templates is taken.
		/// </summary>
		/// <remarks>
		/// A prototype's type is its prefab with the look of the biome whose rule made it (<see cref="TerrainDetailBiomes"/>,
		/// <see cref="BiomeTemplate.GrassTypeFor(GameObject)"/>) and that rule's healthy and dry tints. Several prototypes share a
		/// type wherever their prefab and look are equal (a prototype per biome rule, summed by the build into a channel per
		/// type). A prototype with no recorded biome (a scene cut before 2026-10-15) or whose biome has no look for its prefab
		/// draws the old look (<see cref="BiomeGrassDefaults"/>) with its material's tints, as every blade did before, and says
		/// so once. A point of interest's own prototype (recorded since 2026-10-16 for the biome the site stands in,
		/// <see cref="TerrainDetailBiomes.Entry.Site"/>) takes that biome's look of the prefab where it has one, else the old
		/// look silently, and always its material's tints.
		/// </remarks>
		public static void SelectBlades(Terrain terrain, GrassBladeSettings settings, List<GrassType> types, List<(GameObject Prefab, string Look)> keys,
			HashSet<string> warned, List<int> prototypes, List<GrassType> chosen)
		{
			prototypes.Clear();
			chosen.Clear();
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null)
			{
				return;
			}
			DetailPrototype[] all = data.detailPrototypes;
			for (int p = 0; p < all.Length; p++)
			{
				DetailPrototype proto = all[p];
				GameObject prefab = proto?.prototype;
				if (prefab == null || !proto.usePrototypeMesh || !settings.IsBladePrototype(prefab.name))
				{
					continue;
				}
				BiomeGrassType look = LookOf(terrain, proto, warned, out bool fromRule, out string source);
				GrassType.TintsOf(proto, fromRule, out Color healthy, out Color dry);
				string signature = look.Signature() + "|t" + Tint(healthy) + Tint(dry);
				int index = GrassTypeTable.Place(keys, prefab, signature, GrassBladeRenderer.MaxTypes, out GrassTypeTable.Placement how);
				if (how == GrassTypeTable.Placement.Refused)
				{
					if (warned.Add("types"))
					{
						Debug.LogWarning($"[Grass] More than {GrassBladeRenderer.MaxTypes} blade looks in the scene: '{prefab.name}' and later prefabs stay meshes.");
					}
					continue;
				}
				if (how == GrassTypeTable.Placement.Merged)
				{
					if (warned.Add("merged " + prefab.name + " " + source))
					{
						Debug.LogWarning($"[Grass] The scene's {GrassBladeRenderer.MaxTypes} blade looks are taken: '{prefab.name}' in {source} is drawn with the look of {types[index].Source}. Fewer distinct looks of one prefab across the scene's biomes would give each its own.");
					}
				}
				else if (how == GrassTypeTable.Placement.Added)
				{
					GrassType type = GrassType.Build(proto, look, healthy, dry);
					type.Index = types.Count;
					type.Source = source;
					types.Add(type);
				}
				else if (types[index].Source != null && !types[index].Source.Contains(source))
				{
					types[index].Source += ", " + source;
				}
				prototypes.Add(p);
				chosen.Add(types[index]);
			}
		}

		private static string Tint(Color c) => $"({Mathf.RoundToInt(c.r * 10000f)},{Mathf.RoundToInt(c.g * 10000f)},{Mathf.RoundToInt(c.b * 10000f)})";

		/// <summary>
		/// The look a prototype's blades take: the template's entry of the biome whose rule made it, else the old look (with one
		/// warning for each reason). <paramref name="fromRule"/> when the prototype is a rule's (its tints are the rule's);
		/// <paramref name="source"/> names where the look came from, for the log.
		/// </summary>
		private static BiomeGrassType LookOf(Terrain terrain, DetailPrototype proto, HashSet<string> warned, out bool fromRule, out string source)
		{
			GameObject prefab = proto.prototype;
			TerrainDetailBiomes.TryGet(terrain, proto, out TerrainDetailBiomes.Entry entry);
			int id = entry.BiomeID;
			// A point of interest's own prototype (a grove's flowers, a field's wheat) takes its site's biome's look but keeps
			// its material's tints: no rule of the biome chose them.
			fromRule = id != 0 && !entry.Site;
			BiomeTemplate biome = id != 0 ? BiomeById(id) : null;
			BiomeGrassType look = biome != null ? biome.GrassTypeFor(prefab) : null;
			if (look != null)
			{
				source = biome.name;
				return look;
			}
			if (id == 0)
			{
				source = "no biome";
				if (terrain.GetComponent<TerrainDetailBiomes>() == null && warned.Add("record"))
				{
					Debug.LogWarning($"[Grass] Terrain '{terrain.name}' (and perhaps the scene's others) has no record of which biome made its detail prototypes (cut before 2026-10-15): its blades draw the old look of each prefab. Repaint or re-cut the scene to give each biome's its own.");
				}
			}
			else if (biome == null)
			{
				source = $"biome #{id}";
				biomesPending = true;
			}
			else if (entry.Site)
			{
				// The site's biome grows no such blades of its own: the prefab's old look, as before (no gap to author).
				source = biome.name + " (site, old look)";
			}
			else
			{
				source = biome.name + " (old look)";
				if (warned.Add("look " + biome.name))
				{
					Debug.LogWarning($"[Grass] Biome '{biome.name}' has no Grass Types entry for '{prefab.name}' (said once for the biome): drawn with the old look. Run Biomes → Authoring → Author biome art to write its blade looks onto the template.");
				}
			}
			return DefaultLook(prefab.name);
		}

		private static readonly Dictionary<string, BiomeGrassType> defaultLooks = new Dictionary<string, BiomeGrassType>();

		/// <summary>The old look of a prefab (<see cref="BiomeGrassDefaults"/>), one shared copy per prefab name.</summary>
		private static BiomeGrassType DefaultLook(string prefabName)
		{
			if (!defaultLooks.TryGetValue(prefabName, out BiomeGrassType look))
			{
				look = BiomeGrassDefaults.For(prefabName) ?? new BiomeGrassType { GroundTint = BiomeGrassType.LegacyGroundTint };
				defaultLooks[prefabName] = look;
			}
			return look;
		}

		/// <summary>
		/// A biome by its id: the registry's (play). In the editor, where the registry may not hold every template (the bake runs
		/// at the end of a cut), the project's templates by the same id, so the bake chooses as play will.
		/// </summary>
		private static BiomeTemplate BiomeById(int id)
		{
			if (BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) && biome != null)
			{
				return biome;
			}
#if UNITY_EDITOR
			if (editorBiomes == null)
			{
				editorBiomes = new Dictionary<int, BiomeTemplate>();
				foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
				{
					var template = UnityEditor.AssetDatabase.LoadAssetAtPath<BiomeTemplate>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
					if (template != null)
					{
						editorBiomes[BiomeRegistry.IDOf(template)] = template;
					}
				}
			}
			if (editorBiomes.TryGetValue(id, out biome))
			{
				return biome;
			}
#endif
			return null;
		}

#if UNITY_EDITOR
		/// <summary>The project's templates by id, for the editor's lookups (<see cref="BiomeById"/>); rebuilt each session.</summary>
		private static Dictionary<int, BiomeTemplate> editorBiomes;

		/// <summary>Forgets the editor's template lookup (a bake after templates were added or renamed).</summary>
		public static void ForgetEditorBiomes() => editorBiomes = null;
#endif

		/// <summary>Hands a finished terrain from the mesh grass to the blades.</summary>
		private static void Finish(GrassTerrain.Builder done)
		{
			Terrain terrain = done.Terrain;
			GrassTerrain grass = done.Result;
			summaryFrames += done.Frames;
			summaryMilliseconds += done.Milliseconds;
			if (grass == null)
			{
				// A build that failed on a worker is taken again from the start, once; one with no grass is remembered.
				if (!done.Failed || !failedOnce.Add(terrain))
				{
					none[terrain] = terrain.terrainData;
				}
				return;
			}
			taken[terrain] = new Taken { Terrain = terrain, Data = terrain.terrainData, Grass = grass };
			grassTerrains.Add(grass);
			none.Remove(terrain);
			TerrainDetailInstancing.SetSkipped(SkipOwner, terrain, grass.Skip);
			summaryTerrains++;
			summaryChannels += grass.Layers;
		}

		private static void Release(Taken t)
		{
			taken.Remove(t.Terrain);
			grassTerrains.Remove(t.Grass);
			t.Grass.Dispose();
			if (t.Terrain != null)
			{
				TerrainDetailInstancing.SetSkipped(SkipOwner, t.Terrain, null);
			}
		}

		private static void ReleaseAll()
		{
			building?.Abandon();
			building = null;
			summaryTerrains = summaryFrames = summaryChannels = summaryBaked = 0;
			summaryMilliseconds = 0;
			foreach (Taken t in new List<Taken>(taken.Values))
			{
				Release(t);
			}
			taken.Clear();
			grassTerrains.Clear();
			GrassSwardMap.Clear();
			none.Clear();
			failedOnce.Clear();
			types.Clear();
			typeKeys.Clear();
			defaultLooks.Clear();
			biomesPending = false;
			renderer?.Dispose();
			renderer = null;
			rendererTried = false;
			arraysCheckAt = 0;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!Application.isPlaying || !TerrainInstancingShared.DrawsDetails(camera) || camera.cameraType == CameraType.Reflection)
			{
				return;
			}
			Sync();
			if (renderer == null || grassTerrains.Count == 0)
			{
				return;
			}
			GrassBladeSettings settings = Settings ?? defaults;
			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			LastDrawCount = renderer.Execute(camera, context, in view, grassTerrains, settings, grassDistance);
		}
	}
}
