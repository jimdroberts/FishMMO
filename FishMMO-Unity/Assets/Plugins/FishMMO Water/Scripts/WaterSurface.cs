using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Water
{
	/// <summary>
	/// The sea in a scene: where its surface is, how it moves, and what the shader is allowed to
	/// ask the render pipeline for.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The still-water level is this object's Y, in metres, and it is not decoration.</b> A
	/// generated scene is cut out of a planet whose sea level the surface function knows exactly,
	/// so the sea belongs at the height the globe says and nowhere else — the shore is then where
	/// the map draws it. One world unit is one metre throughout, which is also what makes the wave
	/// speeds right: deep-water waves travel at √(g/k), so the numbers only work at true scale.
	/// </para>
	/// <para>
	/// <b>It follows the camera.</b> The mesh is a disc centred on whichever camera is about to
	/// render, moved in XZ only; the waves are a function of world position, so the sea does not
	/// travel with the viewer even though its geometry does.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Surface")]
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class WaterSurface : MonoBehaviour
	{
		// ── Shader globals ────────────────────────────────────────────
		private static readonly int WaveId = Shader.PropertyToID("_FishWaterWave");
		private static readonly int MotionId = Shader.PropertyToID("_FishWaterMotion");
		private static readonly int CountId = Shader.PropertyToID("_FishWaterWaveCount");
		private static readonly int LevelId = Shader.PropertyToID("_FishWaterLevel");
		private static readonly int TimeId = Shader.PropertyToID("_FishWaterTime");
		private static readonly int CloudShadowId = Shader.PropertyToID("_FishWaterCloudShadow");
		private static readonly int WindId = Shader.PropertyToID("_FishWaterWind");
		private static readonly int WhitecapId = Shader.PropertyToID("_FishWaterWhitecap");
		private static readonly int UnderwaterTintId = Shader.PropertyToID("_UnderwaterTint");
		private static readonly int UnderwaterDepthId = Shader.PropertyToID("_UnderwaterDepth");

		private const string DepthKeyword = "_WATER_DEPTH";
		private const string RefractionKeyword = "_WATER_REFRACTION";

		/// <summary>
		/// Seconds before the clock wraps.
		/// </summary>
		/// <remarks>
		/// A float holding ten thousand seconds still resolves a millisecond, while <c>_Time.y</c>
		/// after a few hours of a session no longer resolves a frame and the sea visibly judders —
		/// which a client that stays open all evening will reach. The wrap puts a single
		/// imperceptible discontinuity in every 2.8 hours in exchange.
		/// </remarks>
		private const double WrapSeconds = 10000.0;

		[Header("Sea")]
		[Tooltip("The material. Must use FishMMO/Water/Ocean.")]
		public Material Material;

		[Header("Waves")]
		[Tooltip("Which way the wind blows TOWARD, clockwise from north.")]
		[Range(0f, 360f)] public float WindDirectionDegrees = 45f;
		[Tooltip("Metres per second. 5 is a breeze, 15 a gale. Sets the wavelength as well as the height.")]
		[Range(0f, 30f)] public float WindSpeed = 8f;
		[Tooltip("0 every wave runs with the wind, 1 they fan across the swell.")]
		[Range(0f, 1f)] public float Spread = 0.6f;
		[Tooltip("0 rolling sine swell, 1 sharp crests that throw foam.")]
		[Range(0f, 1f)] public float Choppiness = 0.5f;
		[Tooltip("Multiplies every wave height, for a sheltered bay or a storm.")]
		[Range(0f, 3f)] public float WaveScale = 1f;
		[Tooltip("How many waves are summed. Each costs one sine per vertex.")]
		[Range(1, WaterWaves.MaximumWaves)] public int WaveCount = 6;

		[Header("Geometry")]
		[Tooltip("Radius of the solid centre disc, in metres.")]
		public float InnerRadius = 4f;
		[Tooltip("How far the sea reaches, in metres. Beyond the camera's far clip is wasted.")]
		public float OuterRadius = 12000f;
		[Tooltip("Rings of vertices from the centre out.")]
		[Range(8, 256)] public int Rings = 96;
		[Tooltip("Vertices around each ring. Low values show as a polygonal horizon.")]
		[Range(8, 512)] public int Segments = 160;

		[Header("Quality")]
		[Tooltip("Refract what is behind the water. Needs the URP asset's Opaque Texture; ignored when it is off.")]
		public bool Refraction = true;
		[Tooltip("Shore foam, soft edges and depth absorption. Needs the URP asset's Depth Texture; ignored when it is off.")]
		public bool DepthEffects = true;
		[Tooltip("Warn once in the log when a quality option had to be turned off.")]
		public bool ReportQuality = true;

		[Header("Underwater")]
		[Tooltip("Tint and dim the whole scene while the camera is below the surface.")]
		public bool UnderwaterEffect = true;
		[Tooltip("The colour the deep fades to.")]
		public Color UnderwaterTint = new Color(0.05f, 0.28f, 0.36f);
		[Tooltip("Roughly how far a diver can see, in metres.")]
		[Range(1f, 200f)] public float UnderwaterVisibility = 22f;

		private MeshFilter meshFilter;
		private MeshRenderer meshRenderer;
		private Mesh mesh;
		private readonly WaterWave[] waves = new WaterWave[WaterWaves.MaximumWaves];
		private readonly Vector4[] packed = new Vector4[WaterWaves.MaximumWaves];
		private readonly Vector4[] packedMotion = new Vector4[WaterWaves.MaximumWaves];
		private int liveWaves;
		private double clock;
		private double lastRealtime = -1.0;
		private int builtRings, builtSegments;
		private float builtInner, builtOuter;
		private bool reported;
		private GameObject underwaterHost;
		private MeshRenderer underwaterRenderer;
		private Material underwaterMaterial;
		private Mesh underwaterMesh;

		/// <summary>
		/// Mean sea level in world metres — this object's own height, and what the scene was
		/// generated against.
		/// </summary>
		/// <remarks>
		/// The transform is never written by the tide. A generated scene places this at the height
		/// the planet says its water line is, and that is the reference the terrain, the shore
		/// field and every placed object were built around; a tide that moved it would make the
		/// scene's own datum drift.
		/// </remarks>
		public float MeanSeaLevel => transform.position.y;

		/// <summary>
		/// How far the tide stands above mean level right now, in metres. Driven from outside.
		/// </summary>
		public float TideMetres { get; set; }

		/// <summary>Where the still water actually is: mean level plus the tide.</summary>
		public float SeaLevel => MeanSeaLevel + TideMetres;

		/// <summary>The sea state, for anything that needs to float on it.</summary>
		public WaterWave[] Waves => waves;

		/// <summary>How many of <see cref="Waves"/> are in use.</summary>
		public int WaveLiveCount => liveWaves;

		/// <summary>The clock the surface is being drawn at, in seconds.</summary>
		public double Clock => clock;

		/// <summary>
		/// 1 under open sky, 0 fully under cloud. Driven by the weather system where there is one.
		/// </summary>
		[HideInInspector] public float CloudShadow = 1f;

		/// <summary>
		/// The sea's surface height above or below a world position, in metres.
		/// </summary>
		/// <remarks>
		/// The same wave sum the shader draws, so a boat sits in the trough the player can see
		/// rather than a centimetre-accurate average of one. Gerstner waves move water sideways as
		/// well as up, so this solves for the piece of water that ENDS UP here — see
		/// <see cref="WaterWaves.SampleHeight"/>.
		/// </remarks>
		public float HeightAt(Vector3 worldPosition)
		{
			return WaterWaves.SampleHeight(waves, liveWaves, worldPosition, SeaLevel, (float)clock);
		}

		/// <summary>True when a world position is under the sea's surface.</summary>
		public bool IsSubmerged(Vector3 worldPosition) => worldPosition.y < HeightAt(worldPosition);

		private void OnEnable()
		{
			meshFilter = GetComponent<MeshFilter>();
			meshRenderer = GetComponent<MeshRenderer>();
			Rebuild();
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			Discard(underwaterHost);
			Discard(underwaterMesh);
			Discard(underwaterMaterial);
			underwaterHost = null;
			underwaterMesh = null;
			underwaterMaterial = null;
			if (mesh != null)
			{
				// HideAndDontSave, so nothing else will ever clean it up.
				if (Application.isPlaying)
				{
					Destroy(mesh);
				}
				else
				{
					DestroyImmediate(mesh);
				}
				mesh = null;
			}
		}

		/// <summary>Destroys an object made here, in whichever way the current mode allows.</summary>
		private static void Discard(UnityEngine.Object victim)
		{
			if (victim == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Destroy(victim);
			}
			else
			{
				DestroyImmediate(victim);
			}
		}

		private void OnValidate()
		{
			OuterRadius = Mathf.Max(InnerRadius * 4f, OuterRadius);
			if (isActiveAndEnabled)
			{
				Rebuild();
			}
		}

		/// <summary>Rebuilds the sea state, and the mesh if its shape changed.</summary>
		public void Rebuild()
		{
			liveWaves = WaterWaves.Build(waves, WindDirectionDegrees, WindSpeed, Spread, Choppiness, WaveScale);
			liveWaves = Mathf.Min(liveWaves, Mathf.Clamp(WaveCount, 1, WaterWaves.MaximumWaves));

			float tallest = 0f;
			for (int i = 0; i < liveWaves; i++)
			{
				tallest += waves[i].Amplitude;
			}

			if (mesh == null || builtRings != Rings || builtSegments != Segments
				|| !Mathf.Approximately(builtInner, InnerRadius) || !Mathf.Approximately(builtOuter, OuterRadius))
			{
				if (mesh != null)
				{
					DestroyImmediate(mesh);
				}
				mesh = WaterMesh.Build(InnerRadius, OuterRadius, Rings, Segments, tallest);
				builtRings = Rings;
				builtSegments = Segments;
				builtInner = InnerRadius;
				builtOuter = OuterRadius;
			}

			if (meshFilter == null)
			{
				meshFilter = GetComponent<MeshFilter>();
			}
			if (meshRenderer == null)
			{
				meshRenderer = GetComponent<MeshRenderer>();
			}
			meshFilter.sharedMesh = mesh;
			if (Material != null && meshRenderer.sharedMaterial != Material)
			{
				meshRenderer.sharedMaterial = Material;
			}
			/* Nothing the sea does belongs in a lightmap or a probe, and it must not cast: water
			 * that casts a shadow darkens the sea bed it exists to show through. */
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = true;
			meshRenderer.lightProbeUsage = LightProbeUsage.Off;
			meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
			meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
		}

		private void Advance()
		{
			double now = Time.realtimeSinceStartupAsDouble;
			if (lastRealtime < 0.0)
			{
				lastRealtime = now;
			}
			// Clamped so a domain reload, a breakpoint or a long frame does not jump the sea.
			double delta = Mathf.Clamp((float)(now - lastRealtime), 0f, 0.25f);
			lastRealtime = now;
			clock = (clock + delta) % WrapSeconds;
		}

		/// <summary>Drives the clock from somewhere else, for a scene whose time is not wall time.</summary>
		public void SetClock(double seconds)
		{
			clock = seconds % WrapSeconds;
			lastRealtime = Time.realtimeSinceStartupAsDouble;
		}

		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera == null || mesh == null)
			{
				return;
			}
			// Reflection probe bakes and preview cameras would each drag the ocean to themselves
			// and leave it there for the camera that matters.
			if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
			{
				return;
			}

			Advance();
			ApplyQuality();
			UpdateUnderwater(camera);

			/* Centred on the camera in XZ only, and NOT snapped to a grid. Snapping a radial mesh
			 * makes the whole ring pattern jump by a step; letting the vertices slide is invisible
			 * instead, because the surface they sample is smooth and stays put in world space. */
			Vector3 position = transform.position;
			transform.position = new Vector3(camera.transform.position.x, position.y, camera.transform.position.z);

			for (int i = 0; i < liveWaves; i++)
			{
				packed[i] = waves[i].Packed;
				packedMotion[i] = waves[i].PackedMotion;
			}
			for (int i = liveWaves; i < WaterWaves.MaximumWaves; i++)
			{
				packed[i] = Vector4.zero;
				packedMotion[i] = Vector4.zero;
			}

			Shader.SetGlobalVectorArray(WaveId, packed);
			Shader.SetGlobalVectorArray(MotionId, packedMotion);
			Shader.SetGlobalFloat(CountId, liveWaves);
			Shader.SetGlobalFloat(LevelId, position.y + TideMetres);
			Shader.SetGlobalFloat(TimeId, (float)clock);
			Shader.SetGlobalFloat(CloudShadowId, Mathf.Clamp01(CloudShadow));

			// The ripples run with the wind, so the fragment stage needs it too.
			float radians = WindDirectionDegrees * Mathf.Deg2Rad;
			Shader.SetGlobalVector(WindId, new Vector4(Mathf.Sin(radians), Mathf.Cos(radians), 0f, 0f));

			/* Past about 15 m/s a fully developed sea stops getting steeper, so the wave geometry
			 * stops producing more breaking crests while a real ocean goes on whitening. This is
			 * the part of white-capping that comes from the wind tearing the tops off rather than
			 * from the shape of the wave. */
			Shader.SetGlobalFloat(WhitecapId, Mathf.Clamp01((WindSpeed - 12f) / 13f));
		}

		/// <summary>
		/// Turns off anything the running pipeline cannot supply.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is the edge case that matters most in this project.</b> Of the three URP assets
		/// here, <c>URP-Performant</c> has <c>Depth Texture</c> off and ALL THREE have
		/// <c>Opaque Texture</c> off. Sampling a texture URP has not bound is not an error and does
		/// not go black — it returns whatever was last written to that slot, so the water would
		/// refract last frame's bloom buffer and foam in mid-air, on the quality tier the most
		/// players are on and on nobody's development machine.
		/// </para>
		/// <para>
		/// Global keywords rather than material ones, so nothing here writes into the material
		/// asset and leaves it modified in the working tree.
		/// </para>
		/// </remarks>
		private void ApplyQuality()
		{
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			bool depth = DepthEffects && pipeline != null && pipeline.supportsCameraDepthTexture;
			bool refraction = Refraction && pipeline != null && pipeline.supportsCameraOpaqueTexture;

			SetKeyword(DepthKeyword, depth);
			SetKeyword(RefractionKeyword, refraction);

			if (!ReportQuality || reported || pipeline == null)
			{
				return;
			}
			if (DepthEffects && !depth)
			{
				Debug.LogWarning($"[Water] '{pipeline.name}' has Depth Texture off, so shore foam, soft edges and " +
					"depth absorption are disabled. Turn it on in the URP asset, or clear Depth Effects here to stop asking.", this);
			}
			if (Refraction && !refraction)
			{
				Debug.LogWarning($"[Water] '{pipeline.name}' has Opaque Texture off, so the water cannot refract what is " +
					"behind it and falls back to blending. Turn it on in the URP asset, or clear Refraction here to stop asking.", this);
			}
			reported = DepthEffects && !depth || Refraction && !refraction;
		}

		/// <summary>
		/// Shows or hides the full-screen underwater pass for the camera about to render.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A mesh in the transparent queue rather than a <c>ScriptableRendererFeature</c>, for one
		/// reason: a renderer feature has to be added to every URP Renderer asset in the project by
		/// hand, and there are three here. A plugin that only works after somebody edits three
		/// project assets is a plugin that is reported as broken.
		/// </para>
		/// <para>
		/// <b>Tested against the WAVE height, not the flat sea level.</b> A camera at the still
		/// level with a two-metre swell running is above the water in the troughs and under it at
		/// the crests, and a test against the flat plane pops the whole screen between the two as
		/// the swell passes.
		/// </para>
		/// </remarks>
		private void UpdateUnderwater(Camera camera)
		{
			bool wanted = UnderwaterEffect && liveWaves > 0
				&& camera.transform.position.y < HeightAt(camera.transform.position);

			if (!wanted)
			{
				if (underwaterHost != null)
				{
					underwaterHost.SetActive(false);
				}
				return;
			}

			if (underwaterHost == null)
			{
				Shader shader = Shader.Find("FishMMO/Water/Underwater");
				if (shader == null)
				{
					UnderwaterEffect = false;
					Debug.LogWarning("[Water] 'FishMMO/Water/Underwater' is missing, so there is no underwater effect.", this);
					return;
				}
				underwaterMaterial = new Material(shader) { name = "Underwater", hideFlags = HideFlags.HideAndDontSave };

				/* One triangle covering the screen, in clip coordinates. Its bounds are enormous
				 * because the vertex shader ignores the transform entirely — culling would
				 * otherwise throw it away the moment its origin left the frustum. */
				underwaterMesh = new Mesh { name = "Underwater", hideFlags = HideFlags.HideAndDontSave };
				underwaterMesh.vertices = new[]
				{
					new Vector3(-1f, -1f, 0f),
					new Vector3(3f, -1f, 0f),
					new Vector3(-1f, 3f, 0f),
				};
				underwaterMesh.triangles = new[] { 0, 1, 2 };
				underwaterMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);

				underwaterHost = new GameObject("Underwater") { hideFlags = HideFlags.DontSave };
				underwaterHost.transform.SetParent(transform, false);
				underwaterHost.AddComponent<MeshFilter>().sharedMesh = underwaterMesh;
				underwaterRenderer = underwaterHost.AddComponent<MeshRenderer>();
				underwaterRenderer.sharedMaterial = underwaterMaterial;
				underwaterRenderer.shadowCastingMode = ShadowCastingMode.Off;
				underwaterRenderer.receiveShadows = false;
				underwaterRenderer.lightProbeUsage = LightProbeUsage.Off;
				underwaterRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			}

			underwaterMaterial.SetColor(UnderwaterTintId, UnderwaterTint);
			underwaterMaterial.SetFloat(UnderwaterDepthId, UnderwaterVisibility);
			underwaterHost.SetActive(true);
		}

		private static void SetKeyword(string keyword, bool on)
		{
			if (on == Shader.IsKeywordEnabled(keyword))
			{
				return;
			}
			if (on)
			{
				Shader.EnableKeyword(keyword);
			}
			else
			{
				Shader.DisableKeyword(keyword);
			}
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.8f);
			Vector3 centre = transform.position;
			const int Steps = 64;
			Vector3 previous = centre + new Vector3(OuterRadius, 0f, 0f);
			for (int i = 1; i <= Steps; i++)
			{
				float angle = i * Mathf.PI * 2f / Steps;
				Vector3 point = centre + new Vector3(Mathf.Cos(angle) * OuterRadius, 0f, Mathf.Sin(angle) * OuterRadius);
				Gizmos.DrawLine(previous, point);
				previous = point;
			}
			UnityEngine.Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.35f);
			Gizmos.DrawLine(centre - Vector3.right * OuterRadius, centre + Vector3.right * OuterRadius);
			Gizmos.DrawLine(centre - Vector3.forward * OuterRadius, centre + Vector3.forward * OuterRadius);
		}
	}
}
