using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's own draw of the generated scenes' ground (2026-10-28): every terrain tile drawn with FishMMO/Weather Terrain
	/// Array is drawn instead by one GPU-driven indirect draw a scene (<see cref="TerrainHeightmapField"/>, FishTerrainHeightmap
	/// .compute, FishMMO/Weather Terrain Array GPU), with the same pixel shader on geomorphed patches.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> Unity's terrain drew Moron 2's sixteen tiles as 86 opaque draws (about five patch batches a tile, instanced
	/// with a property block per tile, so outside the SRP Batcher: ~56 shader setups a frame) and nine shadow draws, and culled
	/// and picked their patches on the main thread per camera and per cascade (TerrainManager.CullAllTerrains, up to 0.4 ms,
	/// and Terrain.UpdateSplatProperties). Here the patches are chosen on the GPU, a thread a quadtree node, and drawn in one
	/// call a list: the ground, the patches a hole touches (_ALPHATEST_ON, only where a tile with holes is in view), and the
	/// same two as shadow casters — about five draws whatever the tile count.
	/// </para>
	/// <para>
	/// <b>Levels.</b> A continuous level field (<see cref="TerrainHeightmapMath"/>) built from the heightmaps' own roughness
	/// keeps the surface within <see cref="ClientTerrainLod.HeightmapPixelError"/> pixels, and the patches geomorph between
	/// levels, so nothing pops; the shadows are cast by the same patches.
	/// </para>
	/// <para>
	/// <b>What stays Unity's.</b> The terrain components remain for collision, the editor, the cut and the server; this sets
	/// <see cref="Terrain.drawHeightmap"/> off on the tiles it draws, in play only, and gives it back when it stops (play ends, the
	/// scene goes, or the switch is turned off). Trees and details are untouched.
	/// </para>
	/// <para>
	/// <b>The switch.</b> <see cref="TerrainHeightmapSettings.Enabled"/> on the weather render profile (live), or
	/// <see cref="Override"/> (the probe's `unityterrain` variant): off, Unity's terrain draws the ground again.
	/// </para>
	/// </remarks>
	public static class TerrainHeightmapSystem
	{
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishTerrainHeightmap.compute";
		public const string ShaderAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishWeatherTerrainArrayGpu.shader";
		public const string ShaderName = "Hidden/FishMMO/Weather Terrain Array GPU";

		/// <summary>Frames between looks for tiles that appeared without a scene load (a tile enabled later).</summary>
		private const int ScanInterval = 30;

		/// <summary>When set, wins over the profile's switch (the probe's A/B): false draws with Unity's terrain.</summary>
		public static bool? Override;

		/// <summary>True while any scene's ground is drawn here.</summary>
		public static bool Active => fields.Count > 0;

		/// <summary>The draws the last game camera issued (for probes).</summary>
		public static int LastDraws { get; private set; }

		/// <summary>The scenes drawn here, one line each (formatted when read).</summary>
		public static string Report
		{
			get
			{
				if (fields.Count == 0)
				{
					return Enabled ? "terrain heightmaps: Unity's (no tiles taken)" : "terrain heightmaps: Unity's (switched off)";
				}
				var lines = new System.Text.StringBuilder("terrain heightmaps: GPU draw, ");
				lines.Append(LastDraws).Append(" draw(s) for the game camera");
				foreach (TerrainHeightmapField f in fields)
				{
					lines.Append("; ").Append(f.Describe());
				}
				return lines.ToString();
			}
		}

		internal static bool NeedsArgsFence => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;

		private static readonly List<TerrainHeightmapField> fields = new List<TerrainHeightmapField>();
		private static readonly List<Terrain> active = new List<Terrain>();
		// Scenes (by handle) whose tiles cannot be drawn here: said once, then left to Unity's terrain.
		private static readonly HashSet<int> refused = new HashSet<int>();
		// FishMMO/Weather Terrain Array, found once (comparing names would allocate one per tile per look).
		private static Shader arrayShader;
		private static ComputeShader compute;
		private static Shader shader;
		private static Mesh patchMesh;
		private static CommandBuffer cmd;
		private static int selectKernel = -1, clearKernel = -1, fenceKernel = -1;
		private static bool hooked;
		private static bool scan = true;
		private static int syncedFrame = -1;
		private static int lastScanFrame = -1000;

		private static bool Enabled
		{
			get
			{
				if (Override.HasValue)
				{
					return Override.Value;
				}
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				return profile == null || profile.TerrainHeightmap == null || profile.TerrainHeightmap.Enabled;
			}
		}

		// ── Hook ────────────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
			refused.Clear();
			Override = null;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			scan = true;
			syncedFrame = -1;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
			TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
			TerrainCallbacks.textureChanged += OnTextureChanged;
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
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
			TerrainCallbacks.textureChanged -= OnTextureChanged;
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
			ReleaseAll();
			Unhook();
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => scan = true;

		private static void OnSceneUnloaded(Scene scene) => scan = true;

		private static void OnHeightmapChanged(Terrain terrain, RectInt region, bool synched) => MarkStale(terrain);

		private static void OnTextureChanged(Terrain terrain, string textureName, RectInt region, bool synched)
		{
			if (textureName == TerrainData.HolesTextureName)
			{
				MarkStale(terrain);
			}
		}

		private static void MarkStale(Terrain terrain)
		{
			foreach (TerrainHeightmapField f in fields)
			{
				if (f.Holds(terrain))
				{
					f.Stale = true;
				}
			}
		}

		// ── Per frame ───────────────────────────────────────────────────────

		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("TerrainHeightmapSystem.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using (ContextMarker.Auto())
			{
				Sync();
			}
		}

		/// <summary>Once a frame: the switch, scenes come and gone, tiles' bindings and heights changed, tiles switched off.</summary>
		private static void Sync()
		{
			if (syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			if (!Enabled || !Supported())
			{
				ReleaseAll();
				return;
			}
			if (!EnsureResources())
			{
				ReleaseAll();
				return;
			}
			for (int i = fields.Count - 1; i >= 0; i--)
			{
				TerrainHeightmapField f = fields[i];
				if (!f.Alive())
				{
					f.Release();
					fields.RemoveAt(i);
					scan = true;
					continue;
				}
				if (f.Stale)
				{
					if (!f.Build(compute, shader, patchMesh))
					{
						Refuse(f);
						f.Release();
						fields.RemoveAt(i);
						continue;
					}
				}
				else if (f.BoundPushes != TerrainArrayBinder.TilePushes && !f.Rebind())
				{
					Refuse(f);
					f.Release();
					fields.RemoveAt(i);
					continue;
				}
#if UNITY_EDITOR
				// The tiles' material may be edited in play: its numbers are copied again now and then.
				if (Time.frameCount % ScanInterval == 0)
				{
					f.CopyLook();
				}
#endif
				f.Refresh();
			}
			if (scan || Time.frameCount - lastScanFrame >= ScanInterval)
			{
				scan = false;
				lastScanFrame = Time.frameCount;
				Scan();
			}
		}

		/// <summary>Takes the eligible tiles no scene's field holds yet, a field per scene.</summary>
		private static void Scan()
		{
			Terrain.GetActiveTerrains(active);
			for (int i = 0; i < active.Count; i++)
			{
				Terrain t = active[i];
				if (Held(t) || !Eligible(t))
				{
					continue;
				}
				Scene scene = t.gameObject.scene;
				if (refused.Contains(scene.handle))
				{
					continue;
				}
				var tiles = new List<Terrain>();
				for (int j = i; j < active.Count; j++)
				{
					if (active[j].gameObject.scene == scene && !Held(active[j]) && Eligible(active[j]))
					{
						tiles.Add(active[j]);
					}
				}
				var field = new TerrainHeightmapField(scene, tiles);
				if (field.Build(compute, shader, patchMesh))
				{
					fields.Add(field);
					Debug.Log($"[Terrain heightmap] {field.Describe()}: drawn by the GPU (Unity's heightmap drawing off on them).");
				}
				else
				{
					Refuse(field);
				}
			}
			active.Clear();
		}

		private static void Refuse(TerrainHeightmapField field)
		{
			if (refused.Add(field.Scene.handle))
			{
				Debug.LogWarning($"[Terrain heightmap] '{field.Scene.name}' is drawn by Unity's terrain: {field.Refusal ?? "it could not be built"}.");
			}
		}

		private static bool Held(Terrain t)
		{
			foreach (TerrainHeightmapField f in fields)
			{
				if (f.Holds(t))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A tile drawn with the array shader, drawing its own heightmap now, with data.</summary>
		private static bool Eligible(Terrain t)
		{
			if (t == null || t.terrainData == null || !t.drawHeightmap)
			{
				return false;
			}
			Material m = t.materialTemplate;
			if (arrayShader == null)
			{
				arrayShader = Shader.Find(TerrainArrayBinder.ShaderName);
			}
			return m != null && arrayShader != null && m.shader == arrayShader;
		}

		/// <summary>Compute and a graphics device for GPU patch selection and indirect draws.</summary>
		private static bool Supported()
		{
			return SystemInfo.supportsComputeShaders
				&& SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
		}

		private static bool EnsureResources()
		{
			if (compute == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				ComputeShader cs = profile != null ? profile.TerrainHeightmapCompute : null;
#if UNITY_EDITOR
				if (cs == null)
				{
					cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
				}
#endif
				if (cs == null)
				{
					return false;
				}
				compute = Object.Instantiate(cs);
				compute.hideFlags = HideFlags.DontSave;
				selectKernel = compute.FindKernel("FishTerrainSelect");
				clearKernel = compute.FindKernel("FishTerrainClear");
				fenceKernel = compute.FindKernel("FishTerrainArgsFence");
			}
			if (shader == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				Shader s = profile != null ? profile.TerrainHeightmapShader : null;
#if UNITY_EDITOR
				if (s == null)
				{
					s = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
				}
#endif
				if (s == null)
				{
					s = Shader.Find(ShaderName);
				}
				if (s == null || !s.isSupported)
				{
					return false;
				}
				shader = s;
			}
			if (patchMesh == null)
			{
				patchMesh = PatchMesh();
			}
			if (cmd == null)
			{
				cmd = new CommandBuffer { name = "Terrain heightmap select" };
			}
			return true;
		}

		/// <summary>One patch: <see cref="TerrainHeightmapMath.PatchVertices"/> squared vertices whose position xy is the grid vertex.</summary>
		private static Mesh PatchMesh()
		{
			int n = TerrainHeightmapMath.PatchVertices;
			var vertices = new Vector3[n * n];
			var normals = new Vector3[n * n];
			var uvs = new Vector2[n * n];
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					vertices[z * n + x] = new Vector3(x, z, 0f);
					normals[z * n + x] = Vector3.up;
					uvs[z * n + x] = new Vector2(x, z) / TerrainHeightmapMath.PatchQuads;
				}
			}
			var mesh = new Mesh { name = "Terrain heightmap patch", hideFlags = HideFlags.HideAndDontSave };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetIndices(TerrainHeightmapMath.PatchIndices(), MeshTopology.Triangles, 0, false);
			// The vertex stage places every vertex; the mesh's own bounds are never used (the draw carries the scene's).
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			mesh.UploadMeshData(true);
			return mesh;
		}

		private static class HookProfiling
		{
			public static readonly ProfilerMarker Marker = new ProfilerMarker("TerrainHeightmapSystem.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (!Application.isPlaying || !TerrainInstancingShared.Draws(camera))
			{
				return;
			}
			Sync();
			if (fields.Count == 0)
			{
				return;
			}
			int draws = 0;
			foreach (TerrainHeightmapField f in fields)
			{
				draws += f.Draw(context, camera, compute, cmd, selectKernel, clearKernel, fenceKernel);
			}
			if (camera.cameraType == CameraType.Game)
			{
				LastDraws = draws;
			}
		}

		/// <summary>Gives every tile back to Unity's drawing and frees everything.</summary>
		private static void ReleaseAll()
		{
			foreach (TerrainHeightmapField f in fields)
			{
				f.Release();
			}
			fields.Clear();
			scan = true;
		}
	}
}
