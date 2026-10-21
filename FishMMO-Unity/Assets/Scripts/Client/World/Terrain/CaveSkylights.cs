using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The openings in caves' roofs up to the sky round the camera (<see cref="SceneEmitterKind.Skylight"/>, baked by the cut;
	/// 2026-10-20, caves livened): what lets the day into a cave under one. The nearest <see cref="MaxNear"/> are handed to every
	/// lit surface (<c>_FishSkylights</c>, FishSkylights.hlsl), so under an opening the sky's share comes back and the sun's
	/// beam is kept (the shadow map drawing it); the camera's own ambient eases off under one (<see cref="OpenAt"/>, for
	/// SkySystem); and the shaft of sunlit air with dust turning in it is drawn as one mesh of prisms and mote quads that
	/// FishSkylightShafts.shader hands out among them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>By the sky.</b> The shaft takes the main light's own colour and its cloud shadow at the opening (its cookie): bright
	/// under a clear noon sun, gone under a storm or after dusk (a moonbeam at night, faintly). Nothing is simulated: the motes
	/// drift on the shared motion clock, so every player sees the same dust.
	/// </para>
	/// <para>Play mode, client only, self-starting like the site lights. The shader comes from the weather render profile (so a
	/// client build includes it), else by name in the editor.</para>
	/// </remarks>
	public static class CaveSkylights
	{
		/// <summary>Openings handed to the shaders at once (FishSkylights.hlsl's FISH_SKYLIGHTS, the shaft shader's SLOTS).</summary>
		public const int MaxNear = SceneSkylight.MaxNear;
		/// <summary>How far from the camera an opening is looked for, metres: a shaft seen across a great hall, an opening from the hill.</summary>
		public const float Reach = 260f;
		/// <summary>How far its shaft is drawn, its motes, metres (both fade over their last quarter).</summary>
		public const float ShaftReach = 220f, MoteReach = 60f;
		public const string ShaderName = "FishMMO/World/Cave Skylight Shafts";

		/// <summary>How much light a metre of the shaft's air scatters toward the eye (cave air is dusty), and the shaft's and motes' brightness.</summary>
		public static float AirDensity = 0.045f, ShaftBrightness = 0.6f, MoteBrightness = 1.4f;

		private const float Refind = 8f;
		/// <summary>The prism's sides round an opening, and the motes in each shaft.</summary>
		private const int Sides = 20, Motes = 96;

		private static readonly int SkylightsId = Shader.PropertyToID("_FishSkylights");
		private static readonly int SkylightCountId = Shader.PropertyToID("_FishSkylightCount");
		private static readonly int SlotsId = Shader.PropertyToID("_SkylightSlots");
		private static readonly int FloorsId = Shader.PropertyToID("_SkylightFloors");
		private static readonly int AirId = Shader.PropertyToID("_SkylightAir");

		private static readonly List<SceneEmitter> near = new List<SceneEmitter>();
		private static readonly Vector4[] slots = new Vector4[MaxNear];
		private static readonly Vector4[] floors = new Vector4[MaxNear];
		private static MaterialPropertyBlock block;
		private static Material material;
		private static Shader materialShader;
		private static Mesh mesh;
		private static Vector3 foundAt = new Vector3(float.MaxValue, 0f, 0f);
		private static int foundVersion = -1;
		private static bool hooked, published;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			hooked = false;
			published = false;
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
			Shader.SetGlobalFloat(SkylightCountId, 0f);
		}

		/// <summary>
		/// How much of the sky a point sees through the openings round the camera, 0..1 (SceneSkylight.SkyShare, the greatest):
		/// SkySystem eases the camera's own underground darkness off by it, so a character under an opening is lit by the sky
		/// it can see.
		/// </summary>
		public static float OpenAt(Vector3 point) => near.Count == 0 ? 0f : SceneSkylight.SkyShareAt(near, point);

		private static Material ResolveMaterial()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.CaveSkylightShaftsShader != null ? profile.CaveSkylightShaftsShader : materialShader != null ? materialShader : Shader.Find(ShaderName);
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
				material = new Material(shader) { name = "Cave Skylight Shafts", hideFlags = HideFlags.HideAndDontSave };
				materialShader = shader;
			}
			return material;
		}

		/// <summary>The nearest openings round the eye, and every lit surface told of them (only when they change).</summary>
		private static void Find(Vector3 eye)
		{
			SceneSiteEmitters.Near(eye, Reach, kind => kind == SceneEmitterKind.Skylight, MaxNear, near);
			for (int i = 0; i < MaxNear; i++)
			{
				if (i < near.Count)
				{
					SceneEmitter e = near[i];
					Vector3 opening = SceneSkylight.Opening(e);
					slots[i] = new Vector4(opening.x, opening.y, opening.z, SceneSkylight.Radius(e));
				}
				else
				{
					slots[i] = Vector4.zero;
				}
			}
			Shader.SetGlobalVectorArray(SkylightsId, slots);
			Shader.SetGlobalFloat(SkylightCountId, near.Count);
			published = true;
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			if (camera == Camera.main)
			{
				if (SceneSiteEmitters.Version != foundVersion || (eye - foundAt).sqrMagnitude > Refind * Refind || !published)
				{
					foundVersion = SceneSiteEmitters.Version;
					foundAt = eye;
					Find(eye);
				}
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
				mesh = BuildMesh();
			}
			block ??= new MaterialPropertyBlock();

			var bounds = new Bounds(slots[0], Vector3.zero);
			for (int i = 0; i < MaxNear; i++)
			{
				if (i >= near.Count)
				{
					floors[i] = Vector4.zero;
					continue;
				}
				SceneEmitter e = near[i];
				Vector3 opening = SceneSkylight.Opening(e);
				Vector3 floor = SceneSkylight.Floor(e);
				float distance = Vector3.Distance(eye, new Vector3(opening.x, Mathf.Clamp(eye.y, floor.y, opening.y), opening.z));
				float shaft = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShaftReach * 0.75f, ShaftReach, distance));
				float motes = distance < MoteReach + SceneSkylight.Radius(e) ? 1f : 0f;
				floors[i] = new Vector4(floor.y, shaft, motes, (e.Seed >> SceneSkylight.HeightBits & 0xFFu) / 255f);
				// The shaft may lean far out along a low sun; the draw's bounds take a generous box round each opening.
				float lean = Mathf.Max(40f, 3f * (opening.y - floor.y));
				bounds.Encapsulate(new Bounds((opening + floor) * 0.5f, new Vector3(2f * lean, opening.y - floor.y + 10f, 2f * lean)));
			}
			block.Clear();
			block.SetVectorArray(SlotsId, slots);
			block.SetVectorArray(FloorsId, floors);
			block.SetVector(AirId, new Vector4(AirDensity, ShaftBrightness, MoteBrightness, MoteReach));
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

		/// <summary>
		/// The field: for each opening a closed prism (a ring at the opening and one at the floor, <see cref="Sides"/> round, and
		/// a cap on each) and <see cref="Motes"/> quads. A prism's vertex holds its unit circle (xy) and 0 at the opening or 1 at
		/// the floor (z); a quad's its corner (xy, −1..1). TEXCOORD0 is (opening, 0, 0) for a prism, (opening, 1, mote) for a
		/// quad; the shader places, leans and sizes them all.
		/// </summary>
		private static Mesh BuildMesh()
		{
			var positions = new List<Vector3>();
			var tags = new List<Vector3>();
			var indices = new List<int>();
			for (int s = 0; s < MaxNear; s++)
			{
				int first = positions.Count;
				for (int ring = 0; ring < 2; ring++)
				{
					for (int k = 0; k < Sides; k++)
					{
						float a = k * 2f * Mathf.PI / Sides;
						positions.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), ring));
						tags.Add(new Vector3(s, 0f, 0f));
					}
				}
				for (int k = 0; k < Sides; k++)
				{
					int a0 = first + k, a1 = first + (k + 1) % Sides, b0 = a0 + Sides, b1 = a1 + Sides;
					indices.Add(a0); indices.Add(b0); indices.Add(a1);
					indices.Add(a1); indices.Add(b0); indices.Add(b1);
				}
				// The caps: drawn by their far side too, so the shaft shows looking up it from its floor or down it from the opening.
				for (int ring = 0; ring < 2; ring++)
				{
					int middle = positions.Count;
					positions.Add(new Vector3(0f, 0f, ring));
					tags.Add(new Vector3(s, 0f, 0f));
					for (int k = 0; k < Sides; k++)
					{
						indices.Add(middle);
						indices.Add(first + ring * Sides + k);
						indices.Add(first + ring * Sides + (k + 1) % Sides);
					}
				}
				for (int m = 0; m < Motes; m++)
				{
					int quad = positions.Count;
					positions.Add(new Vector3(-1f, -1f, 0f));
					positions.Add(new Vector3(1f, -1f, 0f));
					positions.Add(new Vector3(1f, 1f, 0f));
					positions.Add(new Vector3(-1f, 1f, 0f));
					for (int c = 0; c < 4; c++)
					{
						tags.Add(new Vector3(s, 1f, m));
					}
					indices.Add(quad); indices.Add(quad + 1); indices.Add(quad + 2);
					indices.Add(quad); indices.Add(quad + 2); indices.Add(quad + 3);
				}
			}
			var built = new Mesh { name = "Cave Skylight Shafts", hideFlags = HideFlags.HideAndDontSave };
			built.SetVertices(positions);
			built.SetUVs(0, tags);
			built.SetTriangles(indices, 0);
			// Placed in the shader: any bounds would do, the draw's own decide.
			built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
			return built;
		}

		// ── The light off the sunlit floor (SiteLightPool) ────────

		/// <summary>The bounce light's brightness at its fullest: the sun straight down a wide opening onto the floor.</summary>
		public const float BounceIntensity = 1.8f;

		/// <summary>
		/// The soft daylight a sunlit floor under an opening throws round its chamber (SiteLightPool's light at the emitter): the
		/// sky's share always by day, the sun's when its beam reaches the floor, both through the cloud as the ground has them;
		/// sky-blue to sun-warm with which is more. Nothing at night.
		/// </summary>
		public static float Bounce(in SceneEmitter e, out Color colour, out float range)
		{
			float radius = SceneSkylight.Radius(e);
			range = Mathf.Clamp(2.5f * radius + 8f, 10f, 30f);
			colour = new Color(0.75f, 0.85f, 1f);
			SkySystem sky = SkySystem.Instance;
			Light sun = sky != null && sky.Sun != null ? sky.Sun : RenderSettings.sun;
			if (sun == null)
			{
				return 0f;
			}
			Vector3 toSun = -sun.transform.forward;
			float day = SceneSkylight.SmoothStep(-0.05f, 0.15f, toSun.y);
			if (day <= 0f)
			{
				return 0f;
			}
			float altitude = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
			float skyLight = day * (sky != null ? sky.CloudSkyThrough : 1f);
			float sunLight = SceneSkylight.BeamOnFloor(SceneSkylight.Height(e.Seed) + SceneSkylight.LitOver, radius, toSun) * Mathf.Max(0f, toSun.y)
				* (sky != null ? sky.CloudLightThrough(altitude) : 1f);
			float size = Mathf.Clamp(radius / 4f, 0.6f, 1.6f);
			colour = Color.Lerp(colour, sun.color, sunLight / Mathf.Max(1e-3f, sunLight + 0.35f * skyLight));
			return BounceIntensity * size * (0.35f * skyLight + sunLight);
		}
	}
}
