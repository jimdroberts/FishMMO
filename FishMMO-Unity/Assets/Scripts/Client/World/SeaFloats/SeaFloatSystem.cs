using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>The sea floats' draw settings (<see cref="SeaFloatSystem"/>), on the Weather Render Profile ("Sea floats").</summary>
	[Serializable]
	public class SeaFloatSettings
	{
		[Tooltip("Off: nothing is drawn floating on the sea, whatever the biomes list.")]
		public bool Enabled = true;
		[Tooltip("The farthest any float is drawn, metres (each kind has its own reach under this; never past 800 m, inside the sea's own wave fade).")]
		[Range(50f, BiomeSeaFloats.MaxDrawMetres)] public float DrawDistance = 650f;
		[Tooltip("Instances each kind may draw a frame (the rest are dropped): GPU memory is 32 bytes each, per kind.")]
		[Range(1024, 65536)] public int Capacity = 16384;
		[Tooltip("The camera this far above the sea, or more, sees no floats (nothing is expanded or drawn).")]
		[Min(10f)] public float MaxCameraHeight = 400f;
	}

	/// <summary>
	/// What drifts on the open sea (2026-10-25, phase I2p, decision 42): sargassum windrows, by-the-wind sailors, barnacled drift
	/// logs, propagule rafts, corklines, growlers, pumice rafts, pancake and brash ice — client only and purely visual, on the GPU.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What floats where.</b> The sea round the camera is cut into 128 m cells; each cell takes the biome under its middle
	/// (the scene's biome map, as the sea life reads it) and that template's <see cref="BiomeTemplate.SeaFloats"/>, the ice kinds
	/// only where the painted climate is cold enough (<see cref="BiomeSeaFloats.LivesAt"/>). A cell and a kind is a job; the
	/// compute (FishSeaFloats.compute) expands each job's lattice into instances, and one indirect draw a kind draws them through
	/// FishMMO/Sea Floats, which rides each on the sea's own wave sum (FishWaterSwell.hlsl) and keeps it out of the surf.
	/// </para>
	/// <para>
	/// <b>The same sea for everyone.</b> Every float is a point of its kind's lattice in the frame of the scene's climatological
	/// wind (the prevailing wind of its latitude, fixed for the scene), carried downwind at its kind's constant rate on the shared
	/// motion clock (<see cref="WorldMotion.Seconds"/>), its look hashed from its identity and the scene's name
	/// (<see cref="BiomeSeaFloats"/>). Nothing is integrated, nothing is on the wire.
	/// </para>
	/// <para>
	/// <b>Nothing by default.</b> No template lists a float until a biome round does: no job, no dispatch, no draw, no buffer.
	/// Where compute shaders are missing (WebGL2) nothing is drawn.
	/// </para>
	/// </remarks>
	public static class SeaFloatSystem
	{
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishSeaFloats.compute";
		public const string ShaderAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishSeaFloats.shader";

		/// <summary>The cells the sea is cut into, metres.</summary>
		public const float CellMetres = 128f;

		/// <summary>The clock the floats' slow turning is folded to, seconds (a whole number of turns fits it: FishSeaFloats.compute).</summary>
		public const double SpinFold = 600.0;

		[StructLayout(LayoutKind.Sequential)]
		private struct FloatJob
		{
			public Vector4 Cell;
			public int FirstU, FirstV, CountU, CountV;
			public int Kind, Slot, Steps, First;
			public Vector4 Drift;
			public const int Stride = 64;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct FloatKind
		{
			public Vector4 A, B, C, D;
			public const int Stride = 64;
		}

		private static readonly int JobsId = Shader.PropertyToID("_FloatJobs");
		private static readonly int KindsId = Shader.PropertyToID("_FloatKinds");
		private static readonly int CountsId = Shader.PropertyToID("_FloatCounts");
		private static readonly int InstancesId = Shader.PropertyToID("_FloatInstances");
		private static readonly int ArgsId = Shader.PropertyToID("_FloatArgs");
		private static readonly int ArgsFenceId = Shader.PropertyToID("_FloatArgsFence");
		private static readonly int CommandBasesId = Shader.PropertyToID("_FloatCommandBases");
		private static readonly int DriftId = Shader.PropertyToID("_FloatDrift");
		private static readonly int EyeId = Shader.PropertyToID("_FloatEye");
		private static readonly int SeedId = Shader.PropertyToID("_FloatSeed");
		private static readonly int ThreadsId = Shader.PropertyToID("_FloatThreads");
		private static readonly int JobCountId = Shader.PropertyToID("_FloatJobCount");
		private static readonly int SlotsId = Shader.PropertyToID("_FloatSlots");
		private static readonly int CapacityId = Shader.PropertyToID("_FloatCapacity");

		private const int Slots = BiomeSeaFloats.KindCount;

		private static bool hooked;
		private static ComputeShader compute;
		private static Shader shader;
		private static Material material;
		private static Mesh mesh;
		private static int clearKernel, expandKernel, finalizeKernel, fenceKernel;
		private static GraphicsBuffer jobBuffer, kindBuffer, countBuffer, instanceBuffer, argsBuffer, baseBuffer;
		private static int capacity;
		private static CommandBuffer cmd;
		private static readonly List<FloatJob> jobs = new List<FloatJob>();
		private static FloatJob[] jobRows = new FloatJob[0];
		private static readonly bool[] drawn = new bool[Slots];

		/// <summary>Why nothing is drawn, or that it is (logged when it changes).</summary>
		public static string Status { get; private set; } = "not started";

		// The scene's biome under each cell, kept while the scene and its map stay the same.
		private struct CellBiome
		{
			public BiomeTemplate Template;
			public float Temperature;
		}
		private static readonly Dictionary<long, CellBiome> cellBiomes = new Dictionary<long, CellBiome>();
		private static SceneBiomeMap cellsOf;
		private static WorldSceneSettings sceneSettings;
		private static UnityEngine.SceneManagement.Scene settingsScene;
		private static readonly List<Terrain> terrains = new List<Terrain>();

		private static bool NeedsArgsFence => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;

		private static SeaFloatSettings Settings => WeatherRenderProfile.Active?.SeaFloats;

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			Unhook();
			Status = "not started";
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
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
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
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

		private static void Release()
		{
			jobBuffer?.Release();
			kindBuffer?.Release();
			countBuffer?.Release();
			instanceBuffer?.Release();
			argsBuffer?.Release();
			baseBuffer?.Release();
			jobBuffer = kindBuffer = countBuffer = instanceBuffer = argsBuffer = baseBuffer = null;
			cmd?.Release();
			cmd = null;
			Destroy(material);
			Destroy(mesh);
			material = null;
			mesh = null;
			compute = null;
			shader = null;
			capacity = 0;
			cellBiomes.Clear();
			cellsOf = null;
			sceneSettings = null;
		}

		private static void Destroy(UnityEngine.Object o)
		{
			if (o == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(o);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(o);
			}
		}

		private static void Report(bool main, string reason)
		{
			if (!main || reason == Status)
			{
				return;
			}
			Status = reason;
			Debug.Log($"[Sea floats] {reason}.");
		}

		// ── The frame ─────────────────────────────────────────────────

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("SeaFloatSystem.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (camera.cameraType == CameraType.Game && camera != Camera.main)
			{
				return;
			}
			bool main = camera.cameraType == CameraType.Game;
			SeaFloatSettings settings = Settings;
			if (settings == null || !settings.Enabled)
			{
				Report(main, settings == null ? "idle: no Weather Render Profile is loaded" : "idle: switched off on the Weather Render Profile (Sea floats)");
				return;
			}
			if (!SurfaceWater.TryGetLevel(out float level) || float.IsInfinity(level))
			{
				Report(main, "idle: this scene has no sea");
				return;
			}
			Vector3 eye = camera.transform.position;
			if (eye.y - level > settings.MaxCameraHeight)
			{
				Report(main, "idle: the camera is too high above the sea");
				return;
			}
			if (!SceneMap(out SceneBiomeMap map, out uint seed))
			{
				Report(main, "idle: no scene biome map");
				return;
			}
			Vector2 drift = DriftWay();
			double seconds = WorldMotion.Seconds;
			float reach = Mathf.Min(settings.DrawDistance, BiomeSeaFloats.MaxDrawMetres);
			if (!Gather(map, eye, reach, drift, seconds))
			{
				// The fast path: nothing in reach floats anything (every scene until a biome round names its floats).
				Report(main, "idle: no biome round the camera floats anything on its sea");
				return;
			}
			if (!EnsureGpu(settings, out string why))
			{
				Report(main, "idle: " + why);
				return;
			}
			Dispatch(context, eye, drift, seconds, seed);
			Draw(camera, eye, reach);
			Report(main, "drawing");
		}

		/// <summary>The scene's climatological drift: the prevailing wind's way at its latitude, fixed for the scene (unit, world xz).</summary>
		public static Vector2 DriftWay()
		{
			AirDrift.Air air = SkySystem.SceneAir(0.0);
			Vector2 way = WeatherDriver.PrevailingWind(air.Latitude, air.Belts);
			return way.sqrMagnitude > 1e-6f ? way.normalized : Vector2.right;
		}

		/// <summary>The biome map and placement seed of the scene the terrains belong to.</summary>
		private static bool SceneMap(out SceneBiomeMap map, out uint seed)
		{
			map = null;
			seed = 0u;
			Terrain.GetActiveTerrains(terrains);
			Terrain any = terrains.Count > 0 ? terrains[0] : null;
			if (any == null)
			{
				return false;
			}
			UnityEngine.SceneManagement.Scene scene = any.gameObject.scene;
			if (sceneSettings == null || settingsScene != scene)
			{
				settingsScene = scene;
				WorldSceneSettings.TryGetForScene(scene, out sceneSettings);
			}
			map = sceneSettings != null ? sceneSettings.BiomeMap : null;
			seed = SeaLifePlacement.SceneSeed(FishMMO.Shared.SceneNames.Of(scene));
			return map != null;
		}

		private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

		/// <summary>The biome (and the painted climate's temperature) under a cell's middle at sea level, cached.</summary>
		private static CellBiome BiomeOf(SceneBiomeMap map, int cx, int cz)
		{
			if (map != cellsOf)
			{
				cellBiomes.Clear();
				cellsOf = map;
			}
			long key = Key(cx, cz);
			if (!cellBiomes.TryGetValue(key, out CellBiome cell))
			{
				var middle = new Vector3((cx + 0.5f) * CellMetres, 0f, (cz + 0.5f) * CellMetres);
				cell.Template = map.Sample(middle);
				ScenePlacementClimate climate = sceneSettings != null ? sceneSettings.PlacementClimate : null;
				cell.Temperature = climate != null && climate.Generated ? climate.SampleAt(middle, out _).Temperature : 0f;
				cellBiomes[key] = cell;
				if (cellBiomes.Count > 4096)
				{
					cellBiomes.Clear();
				}
			}
			return cell;
		}

		/// <summary>The jobs round the camera: every cell in reach and every kind its biome floats there; false when there are none.</summary>
		private static bool Gather(SceneBiomeMap map, Vector3 eye, float reach, Vector2 drift, double seconds)
		{
			jobs.Clear();
			Array.Clear(drawn, 0, drawn.Length);
			int range = Mathf.CeilToInt(reach / CellMetres);
			int cx = Mathf.FloorToInt(eye.x / CellMetres), cz = Mathf.FloorToInt(eye.z / CellMetres);
			int threads = 0;
			for (int z = cz - range; z <= cz + range; z++)
			{
				for (int x = cx - range; x <= cx + range; x++)
				{
					float x0 = x * CellMetres, z0 = z * CellMetres;
					// From the camera to the cell's nearest point.
					float dx = Mathf.Max(0f, Mathf.Max(x0 - eye.x, eye.x - (x0 + CellMetres)));
					float dz = Mathf.Max(0f, Mathf.Max(z0 - eye.z, eye.z - (z0 + CellMetres)));
					float near = Mathf.Sqrt(dx * dx + dz * dz);
					if (near > reach)
					{
						continue;
					}
					CellBiome cell = BiomeOf(map, x, z);
					List<BiomeSeaFloat> floats = cell.Template != null ? cell.Template.SeaFloats : null;
					if (floats == null || floats.Count == 0)
					{
						continue;
					}
					foreach (BiomeSeaFloat f in floats)
					{
						int kind = (int)f.Kind;
						if (f.Weight <= 0f || kind >= Slots || !BiomeSeaFloats.LivesAt(f.Kind, cell.Temperature))
						{
							continue;
						}
						BiomeSeaFloatTraits traits = BiomeSeaFloats.Traits(f.Kind);
						if (near > Mathf.Min(reach, traits.DrawMetres))
						{
							continue;
						}
						BiomeSeaFloats.SplitDrift(BiomeSeaFloats.Drifted(f.Kind, seconds), traits.Spacing.x, out int steps, out float remainder);
						BiomeSeaFloats.SlotRange(drift, traits.Spacing, remainder, x0, z0, CellMetres, out int firstU, out int firstV, out int countU, out int countV);
						int count = countU * countV;
						if (count <= 0)
						{
							continue;
						}
						jobs.Add(new FloatJob
						{
							Cell = new Vector4(x0, z0, CellMetres, f.Weight),
							FirstU = firstU, FirstV = firstV, CountU = countU, CountV = countV,
							Kind = kind, Slot = kind, Steps = steps, First = threads,
							Drift = new Vector4(remainder, 0f, 0f, 0f),
						});
						threads += count;
						drawn[kind] = true;
					}
				}
			}
			return jobs.Count > 0;
		}

		/// <summary>The compute, the shader, the mesh and the buffers; made once the first float is to be drawn.</summary>
		private static bool EnsureGpu(SeaFloatSettings settings, out string why)
		{
			why = null;
			if (compute == null)
			{
				compute = WeatherRenderProfile.Active != null ? WeatherRenderProfile.Active.SeaFloatsCompute : null;
#if UNITY_EDITOR
				if (compute == null)
				{
					compute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
				}
#endif
				if (compute != null)
				{
					clearKernel = compute.FindKernel("FishFloatClear");
					expandKernel = compute.FindKernel("FishFloatExpand");
					finalizeKernel = compute.FindKernel("FishFloatFinalize");
					fenceKernel = compute.FindKernel("FishFloatArgsFence");
				}
			}
			if (shader == null)
			{
				shader = WeatherRenderProfile.Active != null ? WeatherRenderProfile.Active.SeaFloatsShader : null;
#if UNITY_EDITOR
				if (shader == null)
				{
					shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
				}
#endif
			}
			if (!TerrainGpuMath.ChooseGpu(SystemInfo.supportsComputeShaders, SystemInfo.maxComputeBufferInputsVertex, compute != null))
			{
				why = compute == null ? $"the compute ({ComputeAssetPath}) is not loaded: Weather Render Profile → Sea floats" : "no compute shaders or too few vertex-stage buffers on this device";
				return false;
			}
			if (shader == null || !shader.isSupported)
			{
				why = $"FishMMO/Sea Floats ({ShaderAssetPath}) is not loaded or not supported here: Weather Render Profile → Sea floats";
				return false;
			}
			if (mesh == null)
			{
				mesh = SeaFloatMeshes.Build();
			}
			if (material == null)
			{
				material = new Material(shader) { name = "Sea floats", hideFlags = HideFlags.DontSave };
			}
			int wanted = Mathf.Clamp(settings.Capacity, 1024, 65536);
			if (instanceBuffer == null || capacity != wanted)
			{
				capacity = wanted;
				instanceBuffer?.Release();
				argsBuffer?.Release();
				baseBuffer?.Release();
				instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots * capacity, 32) { name = "Sea float instances" };
				argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, Slots, GraphicsBuffer.IndirectDrawIndexedArgs.size) { name = "Sea float args" };
				baseBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots, 4) { name = "Sea float command bases" };
				var args = new GraphicsBuffer.IndirectDrawIndexedArgs[Slots];
				var bases = new uint[Slots];
				for (int s = 0; s < Slots; s++)
				{
					SubMeshDescriptor sub = mesh.GetSubMesh(s);
					bases[s] = (uint)(s * capacity);
					args[s] = new GraphicsBuffer.IndirectDrawIndexedArgs
					{
						indexCountPerInstance = (uint)sub.indexCount,
						instanceCount = 0u,
						startIndex = (uint)sub.indexStart,
						baseVertexIndex = (uint)sub.baseVertex,
						startInstance = TerrainGpuRenderer.InstanceIdCarriesBase ? bases[s] : 0u,
					};
				}
				argsBuffer.SetData(args);
				baseBuffer.SetData(bases);
				material.SetBuffer(InstancesId, instanceBuffer);
				material.SetBuffer(CommandBasesId, baseBuffer);
			}
			if (countBuffer == null)
			{
				countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, Slots, 4) { name = "Sea float counts" };
			}
			if (kindBuffer == null)
			{
				var rows = new FloatKind[Slots];
				for (int k = 0; k < Slots; k++)
				{
					BiomeSeaFloatTraits t = BiomeSeaFloats.Traits((BiomeSeaFloatKind)k);
					int band = t.RowPitch > 0f ? Math.Max(1, Mathf.RoundToInt(t.RowPitch / t.Spacing.y)) : 1;
					rows[k] = new FloatKind
					{
						A = new Vector4(t.Spacing.x, t.Spacing.y, t.Chance, t.PatchShare),
						B = new Vector4(t.Size.x, t.Size.y, t.RowPitch, t.RowHalfWidth),
						C = new Vector4(Math.Max(1, t.PatchCells), t.DrawMetres, t.Rigid ? 1f : 0f, t.MinDepth),
						D = new Vector4(band, 0f, 0f, 0f),
					};
				}
				kindBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots, FloatKind.Stride) { name = "Sea float kinds" };
				kindBuffer.SetData(rows);
			}
			if (jobBuffer == null || jobBuffer.count < jobs.Count)
			{
				jobBuffer?.Release();
				jobBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(64, Mathf.NextPowerOfTwo(jobs.Count)), FloatJob.Stride) { name = "Sea float jobs" };
			}
			cmd ??= new CommandBuffer { name = "Sea floats" };
			return true;
		}

		private static void Dispatch(ScriptableRenderContext context, Vector3 eye, Vector2 drift, double seconds, uint seed)
		{
			if (jobRows.Length < jobs.Count)
			{
				jobRows = new FloatJob[Mathf.NextPowerOfTwo(jobs.Count)];
			}
			jobs.CopyTo(jobRows);
			FloatJob last = jobs[jobs.Count - 1];
			int threads = last.First + last.CountU * last.CountV;
			cmd.Clear();
			cmd.SetBufferData(jobBuffer, jobRows, 0, 0, jobs.Count);
			cmd.SetComputeVectorParam(compute, DriftId, new Vector4(drift.x, drift.y, (float)WorldMotion.Repeat(seconds, SpinFold), 0f));
			cmd.SetComputeVectorParam(compute, EyeId, new Vector4(eye.x, eye.y, eye.z, 0f));
			cmd.SetComputeIntParam(compute, SeedId, unchecked((int)seed));
			cmd.SetComputeIntParam(compute, ThreadsId, threads);
			cmd.SetComputeIntParam(compute, JobCountId, jobs.Count);
			cmd.SetComputeIntParam(compute, SlotsId, Slots);
			cmd.SetComputeIntParam(compute, CapacityId, capacity);

			cmd.SetComputeBufferParam(compute, clearKernel, CountsId, countBuffer);
			cmd.DispatchCompute(compute, clearKernel, 1, 1, 1);

			cmd.SetComputeBufferParam(compute, expandKernel, JobsId, jobBuffer);
			cmd.SetComputeBufferParam(compute, expandKernel, KindsId, kindBuffer);
			cmd.SetComputeBufferParam(compute, expandKernel, CountsId, countBuffer);
			cmd.SetComputeBufferParam(compute, expandKernel, InstancesId, instanceBuffer);
			int groups = (threads + 63) / 64;
			cmd.DispatchCompute(compute, expandKernel, Mathf.Min(groups, 65535), (groups + 65534) / 65535, 1);

			cmd.SetComputeBufferParam(compute, finalizeKernel, CountsId, countBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, argsBuffer);
			cmd.DispatchCompute(compute, finalizeKernel, 1, 1, 1);
			if (NeedsArgsFence)
			{
				cmd.SetComputeBufferParam(compute, fenceKernel, ArgsFenceId, argsBuffer);
				cmd.SetComputeBufferParam(compute, fenceKernel, CountsId, countBuffer);
				cmd.DispatchCompute(compute, fenceKernel, 1, 1, 1);
			}
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
		}

		/// <summary>One indirect draw per kind any job expanded this frame.</summary>
		private static void Draw(Camera camera, Vector3 eye, float reach)
		{
			var rp = new RenderParams(material)
			{
				camera = camera,
				layer = 0,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = true,
				worldBounds = new Bounds(eye, Vector3.one * (2f * reach + 40f)),
				lightProbeUsage = LightProbeUsage.Off,
				reflectionProbeUsage = ReflectionProbeUsage.Off,
			};
			for (int s = 0; s < Slots; s++)
			{
				if (drawn[s])
				{
					Graphics.RenderMeshIndirect(rp, mesh, argsBuffer, 1, s);
				}
			}
		}
	}
}
