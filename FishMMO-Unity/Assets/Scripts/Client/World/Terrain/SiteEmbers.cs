using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The sites' fires near the camera (SceneSiteEmitters, baked by the cut): campfires, hearths, forges and torches. The
	/// nearest <see cref="MaxSources"/> within <see cref="Reach"/> each burn with tongues of flame (FishSiteFlames.shader)
	/// and throw up a stream of embers that rise, drift downwind, cool and go out (FishSiteEmbers.shader). Two draws of two
	/// pre-built fields of quads for all of them. A lava tube's molten lava (<see cref="SceneEmitterKind.Magma"/>, 2026-10-20)
	/// throws its embers up off the whole width of its body in the same field, and its fumes
	/// (<see cref="SceneEmitterKind.Fumes"/>) drift there as soft puffs under the roof: no draw of their own.
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
		public const string FlamesShaderName = "FishMMO/World/Site Flames";
		/// <summary>Tongues of flame in the field, shared among the sources.</summary>
		public const int Flames = MaxSources * 9;
		private const float Refind = 8f;

		private static readonly int SourcesId = Shader.PropertyToID("_EmberSources");
		private static readonly int StrengthId = Shader.PropertyToID("_EmberStrength");
		private static readonly int WindId = Shader.PropertyToID("_EmberWind");
		private static readonly int NightLightsId = Shader.PropertyToID("_FishNightLights");
		private static readonly int WeatherWindId = Shader.PropertyToID("_FishWeatherWind");
		private static readonly int FlameSourcesId = Shader.PropertyToID("_FlameSources");
		private static readonly int FlameStrengthId = Shader.PropertyToID("_FlameStrength");
		private static readonly int FlameWindId = Shader.PropertyToID("_FlameWind");
		private static readonly Vector4[] flameSources = new Vector4[MaxSources];
		private static Material flameMaterial;
		private static Mesh flameMesh;

		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static readonly Vector4[] sources = new Vector4[MaxSources];
		private static readonly Vector4[] strengths = new Vector4[MaxSources];
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
			if (flameMaterial != null)
			{
				Object.Destroy(flameMaterial);
				flameMaterial = null;
			}
			if (flameMesh != null)
			{
				Object.Destroy(flameMesh);
				flameMesh = null;
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

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("SiteEmbers.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (camera == Camera.main && (SceneSiteEmitters.Version != foundVersion || (eye - foundAt).sqrMagnitude > Refind * Refind))
			{
				foundVersion = SceneSiteEmitters.Version;
				foundAt = eye;
				// With a lava tube's molten lava and the fumes under its roof (2026-10-20): its embers rise off the lava, its fumes
				// hang in the same field (kinds 5 and 6 of FishSiteEmbers.shader).
				// And a way light's own fire (2026-10-26, ScenePyre): a beacon's, flaming only once it is kindled at dusk.
				SceneSiteEmitters.Near(eye, Reach + Refind, kind => kind == SceneEmitterKind.Fire || kind == SceneEmitterKind.Forge || kind == SceneEmitterKind.Hearth
					|| kind == SceneEmitterKind.Torch || kind == SceneEmitterKind.Magma || kind == SceneEmitterKind.Fumes || kind == SceneEmitterKind.Pyre, MaxSources, near);
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
			float night = Mathf.Clamp01(Shader.GetGlobalFloat(NightLightsId));
			var bounds = new Bounds(near[0].Position, Vector3.one * 8f);
			for (int i = 0; i < MaxSources; i++)
			{
				if (i >= near.Count)
				{
					sources[i] = Vector4.zero;
					strengths[i] = Vector4.zero;
					flameSources[i] = Vector4.zero;
					flameStrengths[i] = Vector4.zero;
					continue;
				}
				SceneEmitter e = near[i];
				float distance = Vector3.Distance(eye, e.Position);
				float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Reach * 0.7f, Reach, distance));
				if (e.Kind == SceneEmitterKind.Magma || e.Kind == SceneEmitterKind.Fumes)
				{
					// Lava's embers over the width of its body (z, metres), or the fumes over their spread (Reach): no flames, no
					// melted ring, no weather (underground). Their pace is the shader's, on the shared clock.
					bool magma = e.Kind == SceneEmitterKind.Magma;
					sources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, magma ? 5f : 6f);
					strengths[i] = magma
						? new Vector4(fade, SceneMagma.Sparks(e.Seed), Mathf.Max(0.3f, SceneMagma.HalfWidthOf(e.Seed)), (float)SceneMagma.BodyOf(e.Seed))
						: new Vector4(fade, FumePuffShare, Mathf.Max(1f, e.Reach), 0f);
					flameSources[i] = Vector4.zero;
					flameStrengths[i] = Vector4.zero;
					heat[i] = Vector4.zero;
					float spread = magma ? strengths[i].z + 2f : strengths[i].z + 3f;
					bounds.Encapsulate(new Bounds(e.Position + Vector3.up * (magma ? 4f : 0f), new Vector3(2f * spread, magma ? 10f : 4f, 2f * spread)));
					continue;
				}
				if (e.Kind == SceneEmitterKind.Pyre)
				{
					// A way light's own fire (2026-10-26, ScenePyre): a beacon's flames and sparks as a campfire's, once it is kindled
					// (dark by day, caught at its own moment of the dusk); a brimstone lamp's blue flame is its piece's own, no
					// orange tongues or sparks. No weather on either (a keeper tends a beacon), no melted ring.
					float kindled = ScenePyre.Blue(e.Seed) ? 0f : ScenePyre.Kindled(night, e.Seed);
					float beat = 0.65f + 0.35f * Mathf.Sin((float)(WorldMotion.Seconds * 0.05 % 6283.0) + (e.Seed & 0xFFFF) * 0.0137f);
					sources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, 1f);
					strengths[i] = new Vector4(fade * kindled, 0.35f + 0.4f * beat, 0f, 0f);
					flameSources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, 1f);
					flameStrengths[i] = new Vector4(fade * kindled, Mathf.InverseLerp(0.3f, 1f, beat), 0f, 0f);
					heat[i] = Vector4.zero;
					bounds.Encapsulate(new Bounds(e.Position + Vector3.up * 3f, new Vector3(10f, 8f, 10f)));
					continue;
				}
				bool forge = e.Kind == SceneEmitterKind.Forge;
				bool hearth = e.Kind == SceneEmitterKind.Hearth, torch = e.Kind == SceneEmitterKind.Torch;
				// A fire burns up and dies down over minutes, each on its own beat; a forge sparks in bursts as it is worked.
				float phase = (e.Seed & 0xFFFF) * 0.0137f;
				double t = WorldMotion.Seconds;
				float burn = forge
					? Mathf.Clamp01(0.35f + 0.65f * Mathf.Sin((float)(t * 0.9 % 6283.0) + phase))
					: 0.65f + 0.35f * Mathf.Sin((float)(t * 0.05 % 6283.0) + phase);
				// Rain, snow and wind on an outdoor fire (SiteFireState): doused, it dies to a smoulder; out, nothing rises.
				SiteFireState.State fire = SiteFireState.Of(e);
				burn *= fire.Burn;
				heat[i] = fire.MeltRadius > 0f ? new Vector4(e.Position.x, e.Position.y, e.Position.z, fire.MeltRadius) : Vector4.zero;
				sources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, forge ? 2f : 1f);
				// A hearth under its hood and a torch spit only a few sparks (a stream of them rose into the ceiling as a cloud
				// of orange dots); a campfire and a forge their full share.
				float sparks = forge ? 0.25f + 0.6f * burn : 0.35f + 0.4f * burn;
				strengths[i] = new Vector4(fade, (hearth || torch ? sparks * 0.18f : sparks) * fire.Burn, 0f, 0f);
				flameSources[i] = new Vector4(e.Position.x, e.Position.y, e.Position.z, forge ? 2f : hearth ? 3f : torch ? 4f : 1f);
				// The flames: the same fade, and the fire's own burn as their height (a forge's bursts too).
				flameStrengths[i] = new Vector4(fade, forge ? burn : Mathf.InverseLerp(0.3f, 1f, burn), 0f, 0f);
				bounds.Encapsulate(new Bounds(e.Position + Vector3.up * 3f, new Vector3(10f, 8f, 10f)));
			}
			PublishHeat(near.Count);
			// The wind near the ground: the weather's direction and speed (0..1 of 30 m/s), the plume's share of it.
			Vector4 wind = Shader.GetGlobalVector(WeatherWindId);
			float speed = wind.z * 30f * 0.35f;
			/* No property block, so the draw stays in the SRP Batcher: the arrays are globals (only this draw reads them, set in the
			 * camera's own hook before it renders) and the wind is the material's own (this system's, never shared). */
			Shader.SetGlobalVectorArray(SourcesId, sources);
			Shader.SetGlobalVectorArray(StrengthId, strengths);
			mat.SetVector(WindId, new Vector4(wind.x * speed, night, wind.y * speed, PrecipitationField.SubPixelSigma));
			var rp = new RenderParams(mat)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
				worldBounds = bounds,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
			DrawFlames(camera, bounds, night, wind.x * speed, wind.y * speed);
		}

		private static readonly Vector4[] heat = new Vector4[MaxSources];
		private static readonly int HeatId = Shader.PropertyToID("_FishHeat");
		private static readonly int HeatCountId = Shader.PropertyToID("_FishHeatCount");

		/// <summary>
		/// The burning fires as heat for the snow (FishWeather.hlsl FishHeatClear): each melts a ring round itself while it
		/// burns, so a campfire in the snow stands on bare, wet ground.
		/// </summary>
		private static void PublishHeat(int count)
		{
			int lit = 0;
			for (int i = 0; i < MaxSources; i++)
			{
				if (i < count && heat[i].w > 0f)
				{
					heat[lit++] = heat[i];
				}
			}
			for (int i = lit; i < MaxSources; i++)
			{
				heat[i] = Vector4.zero;
			}
			Shader.SetGlobalVectorArray(HeatId, heat);
			Shader.SetGlobalFloat(HeatCountId, lit);
		}

		/// <summary>
		/// The flames: one draw of a field of quads the shader hands out among the same sources as the embers (in
		/// <see cref="flameSources"/>, kinds 1 fire, 2 forge, 3 hearth, 4 torch; <see cref="flameStrengths"/> their fade and burn).
		/// </summary>
		private static void DrawFlames(Camera camera, Bounds bounds, float night, float windX, float windZ)
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.SiteFlamesShader != null ? profile.SiteFlamesShader : Shader.Find(FlamesShaderName);
			if (shader == null)
			{
				return;
			}
			if (flameMaterial == null || flameMaterial.shader != shader)
			{
				if (flameMaterial != null)
				{
					Object.Destroy(flameMaterial);
				}
				flameMaterial = new Material(shader) { name = "Site Flames", hideFlags = HideFlags.HideAndDontSave };
			}
			if (flameMesh == null)
			{
				flameMesh = PrecipitationField.BuildMesh(Flames, 61211);
				flameMesh.name = "Site Flames";
			}
			// As the embers: the flames' own arrays as globals, their wind the material's own.
			Shader.SetGlobalVectorArray(FlameSourcesId, flameSources);
			Shader.SetGlobalVectorArray(FlameStrengthId, flameStrengths);
			flameMaterial.SetVector(FlameWindId, new Vector4(windX, night, windZ, 0f));
			var rp = new RenderParams(flameMaterial)
			{
				camera = camera,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
				worldBounds = bounds,
			};
			Graphics.RenderMesh(rp, flameMesh, 0, Matrix4x4.identity);
		}

		private static readonly Vector4[] flameStrengths = new Vector4[MaxSources];

		/// <summary>
		/// The share of a fume source's quads drawn as puffs (of its <see cref="Particles"/> / <see cref="MaxSources"/>): two dozen
		/// soft puffs each a few metres across, enough for a haze under the roof, few enough that the overdraw stays small.
		/// </summary>
		public const float FumePuffShare = 0.5f;
	}
}
