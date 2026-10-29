using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Water dripping in the caves near the camera (2026-10-20, caves livened): the nearest <see cref="MaxSources"/> drips within
	/// <see cref="Reach"/> (<see cref="SceneEmitterKind.Drip"/>, baked by the cut) each drop on the shared clock (SceneDrip),
	/// streak down their fall, spatter where they land and send rings over their puddles (FishCaveDrips.shader). One draw of a
	/// pre-built set of quads for all of them; their sound is CaveDripPresenter's.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Cost.</b> On the CPU a scan of the scene's emitters each time the camera has moved <see cref="Refind"/> metres (shared
	/// with the other emitter readers' kind of search), then per camera per frame four small arrays of <see cref="MaxSources"/>
	/// (each drip's beat read from the clock in double) and one RenderMesh of 768 vertices. On the GPU the puddles' few square
	/// metres and the drops' handful of pixels, each through the lights' loop. Nothing at all where no drip is in reach.
	/// </para>
	/// <para>Play mode, client only, self-starting like the site embers. The shader comes from the weather render profile (so a
	/// client build includes it), else by name in the editor.</para>
	/// </remarks>
	public static class CaveDripField
	{
		/// <summary>Drips drawn at once (FishCaveDrips.shader's DRIP_SOURCES).</summary>
		public const int MaxSources = 24;
		/// <summary>Metres from the camera a drip is drawn to: a drop is far under a pixel past it, a puddle a few.</summary>
		public const float Reach = 40f;
		public const string ShaderName = "FishMMO/World/Cave Drips";
		private const float Refind = 4f;
		/// <summary>Quads a drip has: its puddle, three drops in the air, four droplets of spatter.</summary>
		private const int Drops = 3, Spatter = 4, QuadsPerSource = 1 + Drops + Spatter;

		private static readonly int SourcesId = Shader.PropertyToID("_DripSources");
		private static readonly int ShapeId = Shader.PropertyToID("_DripShape");
		private static readonly int TimesId = Shader.PropertyToID("_DripTimes");
		private static readonly int ExtraId = Shader.PropertyToID("_DripExtra");
		private static readonly int CountId = Shader.PropertyToID("_DripCount");

		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static readonly Vector4[] sources = new Vector4[MaxSources];
		private static readonly Vector4[] shapes = new Vector4[MaxSources];
		private static readonly Vector4[] times = new Vector4[MaxSources];
		private static readonly Vector4[] extras = new Vector4[MaxSources];
		private static Material material;
		private static Shader materialShader;
		private static Mesh mesh;
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int foundVersion = -1;
		private static bool hooked;

		private static readonly Unity.Profiling.ProfilerMarker DrawMarker = new Unity.Profiling.ProfilerMarker("CaveDripField.Draw");

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
			near.Clear();
		}

		private static Material ResolveMaterial()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.CaveDripsShader != null ? profile.CaveDripsShader : materialShader != null ? materialShader : Shader.Find(ShaderName);
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
				material = new Material(shader) { name = "Cave Drips", hideFlags = HideFlags.HideAndDontSave };
				materialShader = shader;
			}
			return material;
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (camera == Camera.main && (SceneSiteEmitters.Version != foundVersion || (eye - foundAt).sqrMagnitude > Refind * Refind))
			{
				foundVersion = SceneSiteEmitters.Version;
				foundAt = eye;
				SceneSiteEmitters.Near(eye, Reach + Refind, kind => kind == SceneEmitterKind.Drip, MaxSources, near);
			}
			if (near.Count == 0)
			{
				return;
			}
			using (DrawMarker.Auto())
			{
				Draw(camera, eye);
			}
		}

		private static void Draw(Camera camera, Vector3 eye)
		{
			Material mat = ResolveMaterial();
			if (mat == null)
			{
				return;
			}
			if (mesh == null)
			{
				mesh = BuildMesh();
			}
			double seconds = WorldMotion.Seconds;
			var bounds = new Bounds(near[0].Position, Vector3.one);
			for (int i = 0; i < MaxSources; i++)
			{
				if (i >= near.Count)
				{
					sources[i] = shapes[i] = times[i] = extras[i] = Vector4.zero;
					continue;
				}
				SceneEmitter e = near[i];
				float distance = Vector3.Distance(eye, e.Position);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.75f, Reach, distance));
				Fill(e, seconds, fade, out sources[i], out shapes[i], out times[i], out extras[i]);
				float puddle = Mathf.Max(0.3f, shapes[i].x);
				bounds.Encapsulate(new Bounds(e.Position + Vector3.up * (0.5f * e.Reach), new Vector3(2f * puddle, e.Reach + 0.6f, 2f * puddle)));
			}
			/* No property block, so the draw stays in the SRP Batcher: the arrays are globals (this is the only draw that reads
			 * them, once per camera, set in that camera's own hook before it renders) and the count is the material's own (it is
			 * this system's, never shared). */
			Shader.SetGlobalVectorArray(SourcesId, sources);
			Shader.SetGlobalVectorArray(ShapeId, shapes);
			Shader.SetGlobalVectorArray(TimesId, times);
			Shader.SetGlobalVectorArray(ExtraId, extras);
			mat.SetFloat(CountId, Mathf.Min(near.Count, MaxSources));
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
		/// A drip's numbers for the shader at a moment of the motion clock: where its drops land and how far they fall; its puddle,
		/// a drop's fall in seconds, its fade and a ring's life; the seconds to its drops' landings −1..+2 about its beat; the
		/// number of drop 0 (mod 4096) and its landing. Public for the tests: the shader reads exactly these.
		/// </summary>
		public static void Fill(in SceneEmitter e, double seconds, float fade, out Vector4 source, out Vector4 shape, out Vector4 time, out Vector4 extra)
		{
			uint seed = e.Seed;
			SceneDrip.ClockAt(seconds, seed, out long cycle, out float into);
			source = new Vector4(e.Position.x, e.Position.y, e.Position.z, e.Reach);
			shape = new Vector4(SceneDrip.PuddleOf(seed), SceneDrip.FallSeconds(e.Reach), fade, SceneDrip.RingLifeOf(seed));
			time = new Vector4(SceneDrip.SecondsToLanding(seed, cycle, into, -1), SceneDrip.SecondsToLanding(seed, cycle, into, 0),
				SceneDrip.SecondsToLanding(seed, cycle, into, 1), SceneDrip.SecondsToLanding(seed, cycle, into, 2));
			extra = new Vector4(cycle % 4096, (float)SceneDrip.LandingOf(seed), 0f, 0f);
		}

		/// <summary>The quads: per drip its puddle, its three drops and its four droplets, each vertex naming its drip, part and which.</summary>
		private static Mesh BuildMesh()
		{
			int quads = MaxSources * QuadsPerSource;
			var positions = new Vector3[quads * 4];
			var corners = new Vector2[quads * 4];
			var indices = new int[quads * 6];
			int q = 0;
			for (int s = 0; s < MaxSources; s++)
			{
				for (int k = 0; k < QuadsPerSource; k++, q++)
				{
					int part = k == 0 ? 0 : k <= Drops ? 1 : 2;
					int which = k == 0 ? 0 : k <= Drops ? k - 1 : k - 1 - Drops;
					for (int c = 0; c < 4; c++)
					{
						positions[q * 4 + c] = new Vector3(s, part, which);
						corners[q * 4 + c] = new Vector2(c & 1, c >> 1);
					}
					indices[q * 6 + 0] = q * 4 + 0;
					indices[q * 6 + 1] = q * 4 + 2;
					indices[q * 6 + 2] = q * 4 + 1;
					indices[q * 6 + 3] = q * 4 + 1;
					indices[q * 6 + 4] = q * 4 + 2;
					indices[q * 6 + 5] = q * 4 + 3;
				}
			}
			var built = new Mesh { name = "Cave Drips", hideFlags = HideFlags.HideAndDontSave };
			built.SetVertices(positions);
			built.SetUVs(0, corners);
			built.SetTriangles(indices, 0, false);
			built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return built;
		}
	}
}
