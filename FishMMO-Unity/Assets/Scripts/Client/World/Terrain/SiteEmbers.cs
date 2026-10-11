using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Sparks off the sites' campfires, cooking fires and forges (SceneSiteEmitters, baked by the cut): the nearest
	/// <see cref="MaxSources"/> within <see cref="Reach"/> of the camera each throw up a stream of embers that rise, drift
	/// downwind, cool and go out (FishSiteEmbers.shader). One draw of one pre-built field of quads for all of them.
	/// </summary>
	/// <remarks>
	/// Play mode, client only, self-starting like the site lights. The shader comes from the weather render profile
	/// (so a client build includes it), else by name in the editor.
	/// </remarks>
	public static class SiteEmbers
	{
		/// <summary>Fires and forges sparking at once (FishSiteEmbers.shader's EMBER_SOURCES).</summary>
		public const int MaxSources = 16;
		/// <summary>Metres from the camera a fire's sparks are drawn to: a spark is a pixel past it.</summary>
		public const float Reach = 90f;
		/// <summary>Embers in the field, shared among the sources.</summary>
		public const int Particles = MaxSources * 48;
		public const string ShaderName = "FishMMO/World/Site Embers";
		private const float Refind = 8f;

		private static readonly int SourcesId = Shader.PropertyToID("_EmberSources");
		private static readonly int StrengthId = Shader.PropertyToID("_EmberStrength");
		private static readonly int WindId = Shader.PropertyToID("_EmberWind");
		private static readonly int NightLightsId = Shader.PropertyToID("_FishNightLights");
		private static readonly int WeatherWindId = Shader.PropertyToID("_FishWeatherWind");

		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static readonly Vector4[] sources = new Vector4[MaxSources];
		private static readonly Vector4[] strengths = new Vector4[MaxSources];
		private static MaterialPropertyBlock block;
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
			near.Clear();
		}

		private static Material ResolveMaterial()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.SiteEmbersShader != null ? profile.SiteEmbersShader : materialShader != null ? materialShader : Shader.Find(ShaderName);
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
				material = new Material(shader) { name = "Site Embers", hideFlags = HideFlags.HideAndDontSave };
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
				SceneSiteEmitters.Near(eye, Reach + Refind, kind => kind == SceneEmitterKind.Fire || kind == SceneEmitterKind.Forge, MaxSources, near);
			}
			if (near.Count == 0)
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
				mesh = PrecipitationField.BuildMesh(Particles, 104723);
				mesh.name = "Site Embers";
			}
			block ??= new MaterialPropertyBlock();

			float night = Mathf.Clamp01(Shader.GetGlobalFloat(NightLightsId));
			var bounds = new Bounds(near[0].Position, Vector3.one * 8f);
			for (int i = 0; i < MaxSources; i++)
			{
				if (i >= near.Count)
				{
					sources[i] = Vector4.zero;
					strengths[i] = Vector4.zero;
					continue;
				}
				SceneEmitter e = near[i];
				float distance = Vector3.Distance(eye, e.Position);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.7f, Reach, distance));
				bool forge = e.Kind == SceneEmitterKind.Forge;
				// A fire burns up and dies down over minutes, each on its own beat; a forge sparks in bursts as it is worked.
				float phase = (e.Seed & 0xFFFF) * 0.0137f;
				double t = WorldMotion.Seconds;
				float burn = forge
					? Mathf.Clamp01(0.35f + 0.65f * Mathf.Sin((float)(t * 0.9 % 6283.0) + phase))
					: 0.65f + 0.35f * Mathf.Sin((float)(t * 0.05 % 6283.0) + phase);
				sources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, forge ? 2f : 1f);
				strengths[i] = new Vector4(fade, forge ? 0.25f + 0.6f * burn : 0.35f + 0.4f * burn, 0f, 0f);
				bounds.Encapsulate(new Bounds(e.Position + Vector3.up * 3f, new Vector3(10f, 8f, 10f)));
			}
			// The wind near the ground: the weather's direction and speed (0..1 of 30 m/s), the plume's share of it.
			Vector4 wind = Shader.GetGlobalVector(WeatherWindId);
			float speed = wind.z * 30f * 0.35f;
			block.Clear();
			block.SetVectorArray(SourcesId, sources);
			block.SetVectorArray(StrengthId, strengths);
			block.SetVector(WindId, new Vector4(wind.x * speed, night, wind.y * speed, PrecipitationField.SubPixelSigma));
			var rp = new RenderParams(mat)
			{
				camera = camera,
				matProps = block,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
				worldBounds = bounds,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}
	}
}
