using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The lighthouses' lanterns at night (<see cref="SceneEmitterKind.Beacon"/>, baked by the cut): a fire behind polished
	/// bronze mirrors that the keeper's clockwork turns, throwing beams that sweep the sea and the shore round it, and a
	/// flash that dazzles when a beam passes over the eye (2026-10-20, "Light houses don't have a light that circles around
	/// in the dark?"). One draw of a pre-built mesh of cones and glare quads that FishLighthouseBeams.shader hands out among
	/// the nearest <see cref="MaxBeacons"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared by every player.</b> Each lantern's heading is a pure function of the shared motion clock
	/// (<see cref="WorldMotion.Seconds"/>, in double) and its seed: two players on the same quay see the same beam pass over
	/// them at the same instant. Its pace (<see cref="TurnSeconds"/>) and number of beams (<see cref="BeamsOf"/>) come from
	/// the seed, so a lighthouse is known by its light, as a real one is by its character.
	/// </para>
	/// <para>
	/// <b>When.</b> Lit from dusk (<c>_FishNightLights</c>, SkySystem). Its beams show faintly in a clear night and strongly
	/// in fog, rain and snow, which scatter them, and reach less far there; its flash dims over distance in thick air.
	/// </para>
	/// <para>Play mode, client only, self-starting like the site lights. The shader comes from the weather render profile
	/// (so a client build includes it), else by name in the editor.</para>
	/// </remarks>
	public static class LighthouseBeams
	{
		/// <summary>Lanterns drawn at once (FishLighthouseBeams.shader's BEACONS).</summary>
		public const int MaxBeacons = 8;
		/// <summary>Beams a lantern throws at most (FishLighthouseBeams.shader's BEAMS).</summary>
		public const int MaxBeams = 3;
		/// <summary>Metres from the camera a lantern is drawn to: a lighthouse is seen from far out at sea.</summary>
		public const float Reach = 4000f;
		/// <summary>How far a beam is drawn, metres (the air takes it before that, <see cref="ClearReach"/>).</summary>
		public const float BeamLength = 480f;
		/// <summary>The metres over which a beam fades to a third in clear air, and in thick fog.</summary>
		public const float ClearReach = 300f, FogReach = 60f;
		/// <summary>How far the flash carries before the air halves it, metres, in clear air and in thick fog.</summary>
		public const float ClearSight = 5000f, FogSight = 350f;
		public const string ShaderName = "FishMMO/World/Lighthouse Beams";

		private const float Refind = 40f;
		/// <summary>The mesh's rings along a beam and its sides round it.</summary>
		private const int Rings = 14, Sides = 10;

		private static readonly int SourcesId = Shader.PropertyToID("_BeaconSources");
		private static readonly int ParamsId = Shader.PropertyToID("_BeaconParams");
		private static readonly int AirId = Shader.PropertyToID("_BeaconAir");
		private static readonly int NightLightsId = Shader.PropertyToID("_FishNightLights");
		private static readonly int WeatherFogId = Shader.PropertyToID("_FishWeatherFog");
		private static readonly int WeatherPrecipId = Shader.PropertyToID("_FishWeatherPrecip");

		private static readonly List<SceneEmitter> all = new List<SceneEmitter>();
		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static readonly List<(float d2, int index)> order = new List<(float, int)>();
		private static readonly Vector4[] sources = new Vector4[MaxBeacons];
		private static readonly Vector4[] parameters = new Vector4[MaxBeacons];
		private static Material material;
		private static Shader materialShader;
		private static Mesh mesh;
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int foundVersion = -1;
		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			hooked = false;
			foundVersion = -1;
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
			SceneManager.sceneUnloaded += _ => foundVersion = -1;
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
			if (material != null)
			{
				Object.Destroy(material);
				material = null;
				materialShader = null;
			}
			if (mesh != null)
			{
				Object.Destroy(mesh);
				mesh = null;
			}
			all.Clear();
			near.Clear();
		}

		/// <summary>Seconds a lantern takes to turn once: 20, 24, 28 or 32 by its seed.</summary>
		public static double TurnSeconds(uint seed) => 20.0 + 4.0 * ((seed >> 4) & 3u);

		/// <summary>Beams a lantern throws, evenly round it: one, two or three by its seed.</summary>
		public static int BeamsOf(uint seed) => 1 + (int)((seed >> 8) % MaxBeams);

		/// <summary>
		/// A lantern's heading now, radians about +y from +z: a whole turn every <see cref="TurnSeconds"/> of the shared motion
		/// clock, from its own starting point, one way or the other by its seed. Read in double, so it never drifts or jumps.
		/// </summary>
		public static float Heading(uint seed, double seconds)
		{
			double turns = seconds / TurnSeconds(seed) + (seed & 0xFFFFu) / 65536.0;
			double frac = turns - System.Math.Floor(turns);
			float heading = (float)(frac * 2.0 * System.Math.PI);
			return (seed & 0x1000u) != 0 ? -heading : heading;
		}

		private static Material ResolveMaterial()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.LighthouseBeamsShader != null ? profile.LighthouseBeamsShader : materialShader != null ? materialShader : Shader.Find(ShaderName);
			if (shader == null)
			{
				return null;
			}
			if (material == null || materialShader != shader)
			{
				if (material != null)
				{
					Object.Destroy(material);
				}
				material = new Material(shader) { name = "Lighthouse Beams", hideFlags = HideFlags.HideAndDontSave };
				materialShader = shader;
			}
			return material;
		}

		/// <summary>Every enabled scene's lanterns, gathered again whenever a scene comes or goes.</summary>
		private static void Gather()
		{
			all.Clear();
			IReadOnlyList<SceneSiteEmitters> sets = SceneSiteEmitters.Active;
			for (int s = 0; s < sets.Count; s++)
			{
				if (sets[s] == null)
				{
					continue;
				}
				IReadOnlyList<SceneEmitter> emitters = sets[s].Emitters;
				for (int i = 0; i < emitters.Count; i++)
				{
					if (emitters[i].Kind == SceneEmitterKind.Beacon)
					{
						all.Add(emitters[i]);
					}
				}
			}
		}

		/// <summary>The nearest <see cref="MaxBeacons"/> lanterns within reach, nearest first; equal distances in gathered order.</summary>
		private static void FindNear(Vector3 eye)
		{
			near.Clear();
			order.Clear();
			float r2 = Reach * Reach;
			for (int i = 0; i < all.Count; i++)
			{
				float d2 = (all[i].Position - eye).sqrMagnitude;
				if (d2 <= r2)
				{
					order.Add((d2, i));
				}
			}
			order.Sort((a, b) => a.d2 != b.d2 ? a.d2.CompareTo(b.d2) : a.index.CompareTo(b.index));
			for (int i = 0; i < order.Count && i < MaxBeacons; i++)
			{
				near.Add(all[order[i].index]);
			}
		}

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("LighthouseBeams.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (camera == Camera.main)
			{
				if (SceneSiteEmitters.Version != foundVersion)
				{
					foundVersion = SceneSiteEmitters.Version;
					Gather();
					foundAt = new Vector3(float.MaxValue, 0f, 0f);
				}
				if ((eye - foundAt).sqrMagnitude > Refind * Refind)
				{
					foundAt = eye;
					FindNear(eye);
				}
			}
			if (near.Count == 0)
			{
				return;
			}
			float night = Mathf.Clamp01(Shader.GetGlobalFloat(NightLightsId));
			if (night <= 0.001f)
			{
				return;
			}
			Material mat = ResolveMaterial();
			if (mat == null)
			{
				return;
			}
			if (mesh == null)
			{
				mesh = BuildMesh();
			}
			// The air: fog, rain and snow scatter a beam (it shows more) and take it sooner (it reaches less far).
			float fog = Mathf.Clamp01(Shader.GetGlobalVector(WeatherFogId).x);
			float precip = Mathf.Clamp01(Shader.GetGlobalVector(WeatherPrecipId).x);
			float thick = Mathf.Clamp01(fog + 0.5f * precip);
			float haze = Mathf.Lerp(0.35f, 1.25f, thick);
			float reach = Mathf.Lerp(ClearReach, FogReach, thick);
			float sight = Mathf.Lerp(ClearSight, FogSight, thick);

			double seconds = WorldMotion.Seconds;
			var bounds = new Bounds(near[0].Position, Vector3.one * (BeamLength * 2f));
			for (int i = 0; i < MaxBeacons; i++)
			{
				if (i >= near.Count)
				{
					sources[i] = Vector4.zero;
					parameters[i] = Vector4.zero;
					continue;
				}
				SceneEmitter e = near[i];
				float distance = Vector3.Distance(eye, e.Position);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.75f, Reach, distance));
				float carried = Mathf.Exp(-distance / sight);
				sources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, Heading(e.Seed, seconds));
				parameters[i] = new Vector4(night * fade, BeamsOf(e.Seed), night * fade * carried, 0f);
				bounds.Encapsulate(new Bounds(e.Position, Vector3.one * (BeamLength * 2f)));
			}
			/* No property block, so the draw stays in the SRP Batcher: the arrays are globals (only this draw reads them, set in the
			 * camera's own hook before it renders) and the air is the material's own (this system's, never shared). */
			Shader.SetGlobalVectorArray(SourcesId, sources);
			Shader.SetGlobalVectorArray(ParamsId, parameters);
			mat.SetVector(AirId, new Vector4(BeamLength, reach, haze, 0f));
			var rp = new RenderParams(mat)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
				worldBounds = bounds,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		/// <summary>
		/// The field: for each lantern, <see cref="MaxBeams"/> open cones and a glare quad. A cone's vertex holds its unit
		/// circle (xy) and its share of the beam's length (z, closer together near the lantern, where it is brightest); a
		/// quad's its corner (xy, −1..1). TEXCOORD0 is (lantern, beam, 0) for a cone and (lantern, 0, 1) for a quad; the
		/// shader places, turns and sizes them all.
		/// </summary>
		private static Mesh BuildMesh()
		{
			var positions = new List<Vector3>();
			var tags = new List<Vector3>();
			var indices = new List<int>();
			for (int b = 0; b < MaxBeacons; b++)
			{
				for (int k = 0; k < MaxBeams; k++)
				{
					int first = positions.Count;
					for (int r = 0; r < Rings; r++)
					{
						float t = Mathf.Pow(r / (float)(Rings - 1), 1.7f);
						for (int s = 0; s < Sides; s++)
						{
							float a = s * 2f * Mathf.PI / Sides;
							positions.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), t));
							tags.Add(new Vector3(b, k, 0f));
						}
					}
					for (int r = 0; r < Rings - 1; r++)
					{
						for (int s = 0; s < Sides; s++)
						{
							int a0 = first + r * Sides + s, a1 = first + r * Sides + (s + 1) % Sides;
							int b0 = a0 + Sides, b1 = a1 + Sides;
							indices.Add(a0); indices.Add(b0); indices.Add(a1);
							indices.Add(a1); indices.Add(b0); indices.Add(b1);
						}
					}
				}
				int quad = positions.Count;
				positions.Add(new Vector3(-1f, -1f, 0f));
				positions.Add(new Vector3(1f, -1f, 0f));
				positions.Add(new Vector3(1f, 1f, 0f));
				positions.Add(new Vector3(-1f, 1f, 0f));
				for (int c = 0; c < 4; c++)
				{
					tags.Add(new Vector3(b, 0f, 1f));
				}
				indices.Add(quad); indices.Add(quad + 1); indices.Add(quad + 2);
				indices.Add(quad); indices.Add(quad + 2); indices.Add(quad + 3);
			}
			var built = new Mesh { name = "Lighthouse Beams", hideFlags = HideFlags.HideAndDontSave };
			built.SetVertices(positions);
			built.SetUVs(0, tags);
			built.SetTriangles(indices, 0);
			// Placed in the shader: any bounds would do, the draw's own decide.
			built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return built;
		}
	}
}
