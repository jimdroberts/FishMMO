using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// What lives on and in the caves' still water near the camera (2026-10-26, phase I2n-c; decision 86 (i) and its leeches, decision
	/// 97): marsh-gas beads rising from the drowned muck and ringing out on the surface, and leeches swimming in it toward whoever wades
	/// in (<see cref="SceneCaveWater"/>; FishCaveWaterLife.shader). The pools are the cut's cave-life Pool spots (every client loads
	/// them); how thickly a pool beads and how many leeches swim in it is the cave biome's (its Cave Floods' Bubbles and Leeches for the
	/// cave's form: the interior's where the cave links one, else the host's). Presentation only: nothing on the wire, no collider, no
	/// damage.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared and per viewer.</b> The beads and the leeches' circuits are pure functions of the shared motion clock; only how far a
	/// pool's leeches have turned toward a wader is this client's own (its pull, eased in over seconds while someone stands in the pool
	/// and out after: <see cref="SceneCaveWater.Attract"/>), as decision 97 allows a viewer-reactive effect.
	/// </para>
	/// <para>
	/// <b>Cost.</b> Nothing at all on a scene where no cave biome names beads or leeches (every scene until a round authors them): one
	/// version check a frame. Else a scan of the candidate pools each time the camera has moved <see cref="Refind"/> metres, and per
	/// camera two RenderMesh calls of a pre-built mesh (<see cref="MaxPools"/> pools' quads), the beads a few pixels, the leeches a few
	/// hundred. Play mode, client only, self-starting like the cave drips; the shader comes from the weather render profile (so a
	/// client build includes it), else by name in the editor.
	/// </para>
	/// </remarks>
	public static class CaveWaterLife
	{
		/// <summary>Pools drawn at once (FishCaveWaterLife.shader's WATER_POOLS).</summary>
		public const int MaxPools = 8;
		/// <summary>Metres from the camera a pool is drawn to (a bead is far under a pixel past it).</summary>
		public const float Reach = 35f;
		public const string ShaderName = "FishMMO/World/Cave Water Life";
		/// <summary>The leeches' render queue: opaque, before the water's surface is drawn over them.</summary>
		public const int LeechQueue = 2450;
		private const float Refind = 4f;
		/// <summary>A leech's segments (a ribbon the shader waves).</summary>
		private const int LeechSegments = 6;
		/// <summary>How far round a pool a character counts as a wader, metres (its rim and a stride).</summary>
		private const float WaderReach = 8f;

		private static readonly int PoolsId = Shader.PropertyToID("_WaterPools");
		private static readonly int ShapeId = Shader.PropertyToID("_WaterShape");
		private static readonly int SeedsId = Shader.PropertyToID("_WaterSeeds");
		private static readonly int WadersId = Shader.PropertyToID("_WaterWaders");
		private static readonly int InfoId = Shader.PropertyToID("_WaterInfo");
		private static readonly int ModeId = Shader.PropertyToID("_Mode");
		private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
		private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
		private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

		/// <summary>One pool that beads or holds leeches.</summary>
		public struct Pool
		{
			public Vector3 Surface;
			public float Radius, Depth, Bubbles;
			public int Vents, Leeches;
			public uint Seed;
			/// <summary>This viewer's pull on its leeches (0..1), and the feet it last pulled them toward.</summary>
			public float Attract;
			public Vector3 Feet;
		}

		private static readonly List<Pool> candidates = new List<Pool>();
		private static readonly List<int> near = new List<int>();
		private static readonly Dictionary<int, int> hostBiomes = new Dictionary<int, int>();
		private static readonly Vector4[] pools = new Vector4[MaxPools];
		private static readonly Vector4[] shapes = new Vector4[MaxPools];
		private static readonly Vector4[] seeds = new Vector4[MaxPools];
		private static readonly Vector4[] waders = new Vector4[MaxPools];
		private static Material beadMaterial, leechMaterial;
		private static Shader materialShader;
		private static Mesh mesh;
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int gatheredLife = -1, gatheredSites = -1, frame = -1;
		private static bool hooked;

		private static readonly Unity.Profiling.ProfilerMarker DrawMarker = new Unity.Profiling.ProfilerMarker("CaveWaterLife.Draw");

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			hooked = false;
			gatheredLife = gatheredSites = frame = -1;
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
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
			SceneManager.sceneUnloaded += _ => gatheredLife = -1;
			Application.quitting += Shutdown;
		}

		private static void Shutdown()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			hooked = false;
			Release();
		}

		private static void Release()
		{
			if (beadMaterial != null)
			{
				Object.Destroy(beadMaterial);
				beadMaterial = null;
			}
			if (leechMaterial != null)
			{
				Object.Destroy(leechMaterial);
				leechMaterial = null;
			}
			materialShader = null;
			if (mesh != null)
			{
				Object.Destroy(mesh);
				mesh = null;
			}
			candidates.Clear();
			near.Clear();
			hostBiomes.Clear();
		}

		/// <summary>
		/// A pool's numbers from its cave-life spot and the cave biome dressing its cave (<paramref name="content"/>: its Cave Floods'
		/// entry for the spot's form): false where that biome names neither beads nor leeches (every cave biome's until its round).
		/// </summary>
		public static bool TryPool(in SceneCaveLifeSpot spot, IReadOnlyList<BiomeCaveFlood> content, out Pool pool)
		{
			pool = default;
			if (spot.Kind != SceneCaveLifeKind.Pool || !BiomeCaveWaters.TryFlood(content, (BiomeCaveForm)spot.Form, out BiomeCaveFlood flood))
			{
				return false;
			}
			float radius = Mathf.Max(0.3f, spot.Extent.x), depth = Mathf.Max(0.1f, spot.Extent.y);
			int vents = SceneCaveWater.VentsOf(flood.Bubbles, radius), leeches = SceneCaveWater.LeechesOf(flood.Leeches, radius);
			if (vents == 0 && leeches == 0)
			{
				return false;
			}
			pool = new Pool
			{
				Surface = spot.Position,
				Radius = radius,
				Depth = depth,
				Bubbles = flood.Bubbles,
				Vents = vents,
				Leeches = leeches,
				Seed = SceneCaveWater.SeedOf(spot.Position),
			};
			return true;
		}

		/// <summary>Files the loaded caves' pools that bead or hold leeches, again whenever the caves (or the sites naming their hosts) change.</summary>
		private static void Gather()
		{
			gatheredLife = SceneCaveLife.Version;
			gatheredSites = SceneSiteEmitters.Version;
			candidates.Clear();
			near.Clear();
			foundAt = new Vector3(float.MaxValue, 0f, 0f);
			hostBiomes.Clear();
			foreach (SceneSiteEmitters set in SceneSiteEmitters.Active)
			{
				if (set == null || set.Points == null || set.Points.Points == null)
				{
					continue;
				}
				foreach (PointOfInterestRecord point in set.Points.Points)
				{
					if (point != null && point.BiomeID != 0)
					{
						hostBiomes[point.Id] = point.BiomeID;
					}
				}
			}
			foreach (SceneCaveLife.Set set in SceneCaveLife.Sets)
			{
				IReadOnlyList<SceneCaveLifeSpot> spots = set.Spots;
				for (int i = 0; spots != null && i < spots.Count; i++)
				{
					SceneCaveLifeSpot spot = spots[i];
					if (spot.Kind != SceneCaveLifeKind.Pool)
					{
						continue;
					}
					// The cave's own biome: its interior's where it links one, else the host land's at its site.
					int id = spot.Biome != 0 ? spot.Biome : hostBiomes.TryGetValue(spot.SiteId, out int host) ? host : 0;
					if (id == 0 || !BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) || biome == null)
					{
						continue;
					}
					if (TryPool(in spot, biome.CaveFloods, out Pool pool))
					{
						candidates.Add(pool);
					}
				}
			}
		}

		private static bool ResolveMaterials()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.CaveWaterLifeShader != null ? profile.CaveWaterLifeShader : materialShader != null ? materialShader : Shader.Find(ShaderName);
			if (shader == null)
			{
				return false;
			}
			if (beadMaterial == null || leechMaterial == null || materialShader != shader)
			{
				if (beadMaterial != null)
				{
					Object.Destroy(beadMaterial);
				}
				if (leechMaterial != null)
				{
					Object.Destroy(leechMaterial);
				}
				beadMaterial = new Material(shader) { name = "Cave Water Beads", hideFlags = HideFlags.HideAndDontSave };
				beadMaterial.SetFloat(ModeId, 0f);
				beadMaterial.SetFloat(SrcBlendId, (float)BlendMode.One);
				beadMaterial.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
				beadMaterial.SetFloat(ZWriteId, 0f);
				leechMaterial = new Material(shader) { name = "Cave Water Leeches", hideFlags = HideFlags.HideAndDontSave };
				leechMaterial.SetFloat(ModeId, 1f);
				leechMaterial.SetFloat(SrcBlendId, (float)BlendMode.One);
				leechMaterial.SetFloat(DstBlendId, (float)BlendMode.Zero);
				leechMaterial.SetFloat(ZWriteId, 1f);
				leechMaterial.renderQueue = LeechQueue;
				materialShader = shader;
			}
			return true;
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (SceneCaveLife.Version != gatheredLife || SceneSiteEmitters.Version != gatheredSites)
			{
				Gather();
			}
			if (candidates.Count == 0)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (camera == Camera.main && (eye - foundAt).sqrMagnitude > Refind * Refind)
			{
				foundAt = eye;
				FindNear(eye);
			}
			if (near.Count == 0)
			{
				return;
			}
			if (frame != Time.frameCount)
			{
				frame = Time.frameCount;
				Pull(Time.deltaTime);
			}
			using (DrawMarker.Auto())
			{
				Draw(camera, eye);
			}
		}

		/// <summary>The nearest <see cref="MaxPools"/> candidates within <see cref="Reach"/> (and a refind) of the eye, nearest first.</summary>
		private static void FindNear(Vector3 eye)
		{
			near.Clear();
			float reach = (Reach + Refind) * (Reach + Refind);
			for (int i = 0; i < candidates.Count; i++)
			{
				if ((candidates[i].Surface - eye).sqrMagnitude <= reach)
				{
					near.Add(i);
				}
			}
			near.Sort((a, b) => (candidates[a].Surface - eye).sqrMagnitude.CompareTo((candidates[b].Surface - eye).sqrMagnitude));
			if (near.Count > MaxPools)
			{
				near.RemoveRange(MaxPools, near.Count - MaxPools);
			}
		}

		/// <summary>
		/// This viewer's pull on each near pool's leeches, a frame on (per viewer: nothing shared moves), and the feet it pulls them
		/// toward: the nearest character standing in the pool.
		/// </summary>
		private static void Pull(float dt)
		{
			for (int k = 0; k < near.Count; k++)
			{
				Pool pool = candidates[near[k]];
				if (pool.Leeches == 0)
				{
					continue;
				}
				bool wading = TryWader(in pool, out Vector3 feet);
				pool.Attract = SceneCaveWater.Attract(pool.Attract, wading, dt);
				if (wading)
				{
					pool.Feet = feet;
				}
				candidates[near[k]] = pool;
			}
		}

		/// <summary>The feet of the nearest character standing in a pool (this client's view of every character it holds).</summary>
		private static bool TryWader(in Pool pool, out Vector3 feet)
		{
			feet = default;
			float best = float.MaxValue;
			foreach (KeyValuePair<long, ICharacter> pair in BaseCharacter.ClientCharacters)
			{
				Transform t = pair.Value?.Transform;
				if (t == null)
				{
					continue;
				}
				Vector3 at = t.position;
				float d = (at - pool.Surface).sqrMagnitude;
				if (d < best && d < WaderReach * WaderReach && SceneCaveWater.Wades(at, pool.Surface, pool.Radius, pool.Depth))
				{
					best = d;
					feet = at;
				}
			}
			return best < float.MaxValue;
		}

		private static void Draw(Camera camera, Vector3 eye)
		{
			if (!ResolveMaterials())
			{
				return;
			}
			if (mesh == null)
			{
				mesh = BuildMesh();
			}
			var bounds = new Bounds(candidates[near[0]].Surface, Vector3.one);
			bool anyLeeches = false;
			for (int i = 0; i < MaxPools; i++)
			{
				if (i >= near.Count)
				{
					pools[i] = shapes[i] = seeds[i] = Vector4.zero;
					waders[i] = Vector4.zero;
					continue;
				}
				Pool pool = candidates[near[i]];
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.75f, Reach, Vector3.Distance(eye, pool.Surface)));
				pools[i] = new Vector4(pool.Surface.x, pool.Surface.y, pool.Surface.z, pool.Radius);
				shapes[i] = new Vector4(pool.Depth, pool.Vents, pool.Leeches, fade);
				// The seed in two 16-bit halves (a 32-bit seed in one float rounds).
				seeds[i] = new Vector4(pool.Seed >> 16, pool.Seed & 0xFFFFu, pool.Bubbles, 0f);
				waders[i] = pool.Attract > 0f ? new Vector4(pool.Feet.x, pool.Feet.y, pool.Feet.z, pool.Attract) : Vector4.zero;
				anyLeeches |= pool.Leeches > 0;
				bounds.Encapsulate(new Bounds(pool.Surface, new Vector3(2f * pool.Radius + 1f, 2f * pool.Depth + 1f, 2f * pool.Radius + 1f)));
			}
			/* No property block, so both draws stay in the SRP Batcher: the arrays are globals (the beads and the leeches read the
			 * same pools, and nothing else reads them; set in the camera's own hook before it renders) and the clock and count are
			 * each material's own (this system's, never shared). */
			Shader.SetGlobalVectorArray(PoolsId, pools);
			Shader.SetGlobalVectorArray(ShapeId, shapes);
			Shader.SetGlobalVectorArray(SeedsId, seeds);
			Shader.SetGlobalVectorArray(WadersId, waders);
			var info = new Vector4(WorldMotion.Wrapped(SceneCaveWater.Wrap), near.Count, 0f, 0f);
			beadMaterial.SetVector(InfoId, info);
			leechMaterial.SetVector(InfoId, info);
			Graphics.RenderMesh(new RenderParams(beadMaterial)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
				worldBounds = bounds,
			}, mesh, 0, Matrix4x4.identity);
			if (anyLeeches)
			{
				Graphics.RenderMesh(new RenderParams(leechMaterial)
				{
					camera = camera,
					shadowCastingMode = ShadowCastingMode.Off,
					receiveShadows = false,
					layer = camera.gameObject.layer,
					worldBounds = bounds,
				}, mesh, 0, Matrix4x4.identity);
			}
		}

		/// <summary>
		/// The quads: per pool every vent's beads and their rings, then its leeches' ribbons, each vertex naming its pool, part and which
		/// (POSITION), and its corner (TEXCOORD0: along, across).
		/// </summary>
		private static Mesh BuildMesh()
		{
			int beadQuads = SceneCaveWater.MaxVents * SceneCaveWater.BeadsPerBurst * 2;
			int leechQuads = SceneCaveWater.MaxLeeches * LeechSegments;
			int quads = MaxPools * (beadQuads + leechQuads);
			var positions = new List<Vector3>(quads * 4);
			var corners = new List<Vector2>(quads * 4);
			var indices = new List<int>(quads * 6);
			void Quad(Vector3 slot, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
			{
				int v = positions.Count;
				positions.Add(slot);
				positions.Add(slot);
				positions.Add(slot);
				positions.Add(slot);
				corners.Add(a);
				corners.Add(b);
				corners.Add(c);
				corners.Add(d);
				indices.Add(v);
				indices.Add(v + 2);
				indices.Add(v + 1);
				indices.Add(v + 1);
				indices.Add(v + 2);
				indices.Add(v + 3);
			}
			for (int p = 0; p < MaxPools; p++)
			{
				for (int w = 0; w < SceneCaveWater.MaxVents * SceneCaveWater.BeadsPerBurst; w++)
				{
					Quad(new Vector3(p, 0, w), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1));
					Quad(new Vector3(p, 1, w), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1));
				}
				for (int l = 0; l < SceneCaveWater.MaxLeeches; l++)
				{
					for (int s = 0; s < LeechSegments; s++)
					{
						float u0 = (float)s / LeechSegments, u1 = (float)(s + 1) / LeechSegments;
						Quad(new Vector3(p, 2, l), new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u0, 1), new Vector2(u1, 1));
					}
				}
			}
			var built = new Mesh { name = "Cave Water Life", hideFlags = HideFlags.HideAndDontSave };
			built.SetVertices(positions);
			built.SetUVs(0, corners);
			built.SetTriangles(indices, 0, false);
			built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return built;
		}
	}
}
