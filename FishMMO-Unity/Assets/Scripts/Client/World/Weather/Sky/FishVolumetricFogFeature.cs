using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The weather's fog near the camera as a volume — the FALLBACK, where the cloud march is not drawing
	/// the fog: a layer of air you stand in, with banks and wisps in it, a top that heaves and frays and
	/// shines, and light shafts through it (P5).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The fog is the cloud march's now</b> (FishCloudVolume.hlsl): cloud whose base is the ground,
	/// walked with the clouds through the same volumes, at every distance, and what fills each froxel
	/// here is the very function the march reads (FishFogDensity). This volume stands down whenever the
	/// march has drawn the fog for the camera (<see cref="FogLayerView.MarchedThisFrame"/>) — which pays
	/// for marching it — and draws the near field only where the march does not run. What it could do
	/// that the march cannot is the light shafts through the main light's shadow map
	/// (<see cref="LightShafts"/>), off on every renderer: not reason enough to draw the same drops twice.
	/// </para>
	/// <para>
	/// The frustum is diced into voxels that follow the camera, so the cost is fixed per frame and
	/// does not care how far anything is. One compute pass decides what is in each froxel and how
	/// much light scatters out of it toward the viewer; a second walks each column outward
	/// accumulating that light and the transmittance through it; a full-screen pass then reads the
	/// result at (uv, depth) and composites it.
	/// </para>
	/// <para>
	/// <b>It is the fog, not an effect laid over it.</b> What is in each froxel is the weather's fog
	/// layer (<see cref="FishMMO.Shared.Weather.FogLayer"/>, FishFogLayer.hlsl): its own extinction from
	/// its water, from the ground up to its top, level over the pooled ground, lifted when the wind or
	/// the morning has lifted it — with the fog's structure moved about inside it without changing how
	/// much there is. The analytic <see cref="FishHeightFogFeature"/> draws the same layer from this
	/// volume's far edge on, and the whole of it wherever this cannot run; the pipeline's distance fog
	/// keeps only what is falling. Nothing is drawn twice.
	/// </para>
	/// <para>
	/// <b>It is cheaper than it sounds — cheaper than what already ships.</b> 160 x 90 x 64 is about
	/// a million froxels at two looks each, at a fixed cost that does not scale with screen
	/// resolution. The volumetric cloud march beside it is 3.6M samples on the CHEAPEST tier and
	/// 37M on the highest, and it does scale with resolution.
	/// </para>
	/// <para>
	/// <b>Compute, so WebGPU and desktop only.</b> WebGL2 has no compute shaders at all, and gets the
	/// analytic pass's closed form of the same layer, without the structure.
	/// </para>
	/// <para>
	/// <b>And the waterfalls' mist</b> (<see cref="FallMist"/>). The spray a fall throws up is a cloud like
	/// any fog, and in this volume it is lit as the fog is — shaded by the terrain, cut by the shadow map's
	/// shafts where they are on — so a fall in a gorge stands in shade with the sun slanting through its
	/// plume, which sprites cannot show. The march draws only the weather's layer, never the mist, so this
	/// volume runs whenever a fall's mist is in reach as well: with the layer in it where nothing else has
	/// drawn the layer, and the mist alone where the march has, or where the weather has no fog. The mist's
	/// billboards thin their own billows while it does (<see cref="FallMistVolumetricId"/>).
	/// </para>
	/// </remarks>
	public class FishVolumetricFogFeature : ScriptableRendererFeature
	{
		public const string ApplyShaderName = "Hidden/FishMMO/Weather/VolumetricFogApply";
		public const string ComputeName = "FishVolumetricFog";

		[Tooltip("The compute shader. Left empty, the feature finds it by name in the editor; a build needs it assigned.")]
		public ComputeShader Compute;

		[Tooltip("The compositing material. Left empty, the feature finds the shader itself.")]
		public Material ApplyMaterial;

		[Header("Froxel grid")]
		[Tooltip("Froxels across the screen. 160 x 90 is a sixth of 1080p and plenty: fog has no fine detail in it.")]
		[Min(32)] public int GridWidth = 160;
		[Min(18)] public int GridHeight = 90;
		[Tooltip("Slices from the near plane to the far. Spaced exponentially, so most sit where the fog is thickest.")]
		[Range(16, 128)] public int GridDepth = 64;

		[Header("Extent")]
		[Tooltip("Metres the volume starts at.")]
		[Min(0.1f)] public float NearDistance = 0.5f;
		[Tooltip("Metres the volume reaches. Beyond this the analytic height fog carries the same layer on alone.")]
		[Min(20f)] public float FarDistance = 500f;
		[Tooltip("How hard the slices bunch toward the camera. 1 is even in log space; higher crowds the near field harder.")]
		[Range(0.5f, 3f)] public float DepthCurve = 1.4f;

		[Header("Light")]
		[Tooltip("Stop the beam where the main light's shadow map says something stands in it: shafts between the trees. Needs the main light to cast shadows.")]
		public bool LightShafts = false;

		[Header("Waterfall mist")]
		[Tooltip("Draw the mist the nearest waterfalls throw up into this volume, lit and shaded with the fog. Runs the volume wherever a fall is in reach, fog or none; off on the cheapest renderer, whose falls keep their sprite mist alone.")]
		public bool FallMist = true;

		/// <summary>
		/// Global float: 1 on a camera whose volume carries the falls' mist this frame, 0 otherwise. The mist's
		/// billboards (the water's spray) thin their own billows while it is 1, or the same spray is drawn twice.
		/// </summary>
		public static readonly int FallMistVolumetricId = Shader.PropertyToID("_FishFallMistVolumetric");

		private FogPass pass;

		/// <summary>Whether this machine can run it at all.</summary>
		public static bool Supported => SystemInfo.supportsComputeShaders;

		// Which camera this frame the volume is drawn for, and how far it reaches: the analytic pass
		// starts where it stops. Decided while the passes are gathered, before either is recorded.
		private static Camera plannedCamera;
		private static int plannedFrame = -1;
		private static float plannedReach;

		/// <summary>
		/// How far in eye depth the volume draws the fog for this camera this frame, m; 0 when it does
		/// not run for it, and the analytic pass must draw from the camera out.
		/// </summary>
		public static float ReachFor(Camera camera)
		{
			return camera != null && camera == plannedCamera && Time.frameCount == plannedFrame ? plannedReach : 0f;
		}

		public override void Create()
		{
			pass = new FogPass
			{
				// With the height fog, over the opaque world and under transparents; after it, since the
				// fog this draws lies in front of what that draws.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 2,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Camera camera = renderingData.cameraData.camera;
			plannedCamera = camera;
			plannedFrame = Time.frameCount;
			plannedReach = 0f;
			// Down until this camera's volume is known to carry the mist (the pass raises it as it records).
			Shader.SetGlobalFloat(FallMistVolumetricId, 0f);
			if (!Supported)
			{
				return;
			}
			ComputeShader compute = ResolveCompute();
			Material material = ResolveMaterial();
			if (compute == null || material == null)
			{
				return;
			}
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			// An overlay draws into its base camera's frame, which the base has already fogged.
			if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
			{
				return;
			}
			// The weather's layer, or a fall's mist in reach: either is reason to run. Whether the march
			// has drawn the layer is only known as the passes record, which the pass asks then.
			bool layer = FogLayerView.Current.Visible;
			int plumes = FallMist ? pass.GatherPlumes(camera.transform.position, Mathf.Max(NearDistance + 1f, FarDistance)) : pass.ClearPlumes();
			if ((!layer && plumes == 0) || !pass.Setup(compute, material, this))
			{
				return;
			}
			// The analytic pass takes the layer over at the volume's far edge only where the volume holds
			// the layer: run for the mist alone, it holds none, and that pass draws from the camera out.
			plannedReach = layer ? LastSliceDepth() : 0f;
			renderer.EnqueuePass(pass);
		}

		/// <summary>
		/// The eye depth the volume's last texel holds the fog to, m: the middle of its last slice. A
		/// deeper pixel reads that texel, so the analytic pass takes over from exactly there — from the
		/// far edge instead, half a slice of fog would be in neither.
		/// </summary>
		private float LastSliceDepth()
		{
			float near = NearDistance;
			float far = Mathf.Max(near + 1f, FarDistance);
			int slices = Mathf.Max(1, GridDepth);
			return near * Mathf.Pow(far / near, Mathf.Pow((slices - 0.5f) / slices, DepthCurve));
		}

		private ComputeShader ResolveCompute()
		{
			if (Compute == null)
			{
				// Editor-only lookup; a build must have the reference assigned on the feature.
				#if UNITY_EDITOR
				string[] found = UnityEditor.AssetDatabase.FindAssets($"{ComputeName} t:ComputeShader");
				if (found.Length > 0)
				{
					Compute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]));
				}
				#endif
			}
			return Compute;
		}

		private Material ResolveMaterial()
		{
			if (ApplyMaterial != null)
			{
				return ApplyMaterial;
			}
			Shader shader = Shader.Find(ApplyShaderName);
			if (shader == null)
			{
				return null;
			}
			ApplyMaterial = CoreUtils.CreateEngineMaterial(shader);
			return ApplyMaterial;
		}

		protected override void Dispose(bool disposing)
		{
			pass?.Dispose();
			pass = null;
			if (ApplyMaterial != null)
			{
				CoreUtils.Destroy(ApplyMaterial);
				ApplyMaterial = null;
			}
		}

		private sealed class FogPass : ScriptableRenderPass
		{
			private const string ScatterName = "Fish Volumetric Fog (scatter)";
			private const string IntegrateName = "Fish Volumetric Fog (integrate)";
			private const string ApplyName = "Fish Volumetric Fog (apply)";
			private const string ShadowsKeyword = "FISH_VOLFOG_SHADOWS";

			private static readonly int ScatterTexId = Shader.PropertyToID("_FishVolFogScatter");
			private static readonly int IntegratedTexId = Shader.PropertyToID("_FishVolFogIntegrated");
			private static readonly int VolumeId = Shader.PropertyToID("_FishVolFogVolume");
			private static readonly int InverseVPId = Shader.PropertyToID("_FishVolFogInverseVP");
			private static readonly int SizeId = Shader.PropertyToID("_FishVolFogSize");
			private static readonly int RangeId = Shader.PropertyToID("_FishVolFogRange");
			private static readonly int CameraId = Shader.PropertyToID("_FishVolFogCamera");
			private static readonly int ForwardId = Shader.PropertyToID("_FishVolFogForward");
			private static readonly int MainShadowsId = Shader.PropertyToID("_MainLightShadowmapTexture");

			// What the fog layer is, handed to the kernel by name: FishFogLayer.hlsl reads them as globals
			// in the pixel passes, and a compute kernel is given its own.
			private static readonly int LayerId = Shader.PropertyToID("_FishFogLayer");
			private static readonly int ShapeId = Shader.PropertyToID("_FishFogLayerShape");
			private static readonly int DriftId = Shader.PropertyToID("_FishFogLayerDrift");
			private static readonly int LightId = Shader.PropertyToID("_FishFogLight");
			private static readonly int LightColorId = Shader.PropertyToID("_FishFogLightColor");
			private static readonly int AmbientId = Shader.PropertyToID("_FishFogAmbient");
			private static readonly int TerrainId = Shader.PropertyToID("_FishCloudTerrain");
			private static readonly int TerrainRectId = Shader.PropertyToID("_FishCloudTerrainRect");
			// The terrain's shadow, near and far (FishTerrainSunlit): a kernel is handed its own, never the globals.
			private static readonly int ShadeNearId = Shader.PropertyToID("_FishShadeNear");
			private static readonly int ShadeNearRectId = Shader.PropertyToID("_FishShadeNearRect");
			private static readonly int ShadeNearBaseId = Shader.PropertyToID("_FishShadeNearBase");
			private static readonly int ShadeFarId = Shader.PropertyToID("_FishShadeFar");
			private static readonly int ShadeFarRectId = Shader.PropertyToID("_FishShadeFarRect");
			private static readonly int ShadeFarBaseId = Shader.PropertyToID("_FishShadeFarBase");
			private static readonly int ShapeTexId = Shader.PropertyToID("_FishCloudShape");
			private static readonly int DetailTexId = Shader.PropertyToID("_FishCloudDetail");

			// The falls' mist (FishVolumetricFog.compute's FallMist): the arrays, and x count, y whether the
			// weather's layer is drawn here too, z how far the billows have risen.
			private static readonly int PlumeAId = Shader.PropertyToID("_FishFallPlumeA");
			private static readonly int PlumeBId = Shader.PropertyToID("_FishFallPlumeB");
			private static readonly int PlumeCId = Shader.PropertyToID("_FishFallPlumeC");
			private static readonly int PlumeDId = Shader.PropertyToID("_FishFallPlumeD");
			private static readonly int FallMistId = Shader.PropertyToID("_FishFallMist");

			/// <summary>The most falls whose mist one camera's volume carries: FISH_FALL_PLUMES in the compute.</summary>
			public const int MaxPlumes = 16;

			/// <summary>One tile of the billows the mist is torn into, m: FISH_FALL_NOISE_TILE in the compute.</summary>
			private const float BillowTile = 48f;

			/// <summary>
			/// How fast the mist climbs off the pool, m/s: the updraft the falling water's own entrained air and
			/// the spray's buoyancy keep up over a plunge pool, a metre or two a second. How long it takes to
			/// reach the plume's top is how long the wind has to lean it; the billows rise at it too.
			/// </summary>
			private const float PlumeRise = 1.5f;

			/// <summary>
			/// How far downstream the water's own throw carries the top of its plume, in plume radii, before
			/// any wind: the spray leaves the boil running with the river, and the air the fall drags down
			/// with it spills out of the pool the same way.
			/// </summary>
			private const float PlumeThrow = 0.4f;

			/// <summary>The wind channel's full scale, m/s (WeatherChannel.WindSpeed: 1 = 30 m/s).</summary>
			private const float WindChannelMetres = 30f;

			/// <summary>
			/// The mist's extinction where the water lands, 1/m, for a fall's power: six hundredths per
			/// metre for every tenfold above a kilowatt, from nothing for a trickle to a fifth for a
			/// megawatt mountain fall and 0.35, the most, for a Niagara — the thick white of spray a few
			/// metres of which hides the rock behind it, in the boil, and thinning from there. Half that
			/// left a fall's landing in plain view (Jim, 2026-10-08).
			/// </summary>
			public static float PeakExtinction(float power)
			{
				return Mathf.Clamp(0.06f * Mathf.Log10(Mathf.Max(1f, power) / 1000f), 0f, 0.35f);
			}

			// The falls this camera's volume carries this frame, nearest first (FallMist in the compute).
			private readonly Vector4[] plumeA = new Vector4[MaxPlumes];
			private readonly Vector4[] plumeB = new Vector4[MaxPlumes];
			private readonly Vector4[] plumeC = new Vector4[MaxPlumes];
			private readonly Vector4[] plumeD = new Vector4[MaxPlumes];
			private readonly float[] plumeDistance = new float[MaxPlumes];
			private int plumeCount;

			/* The integrated volume, published for TRANSPARENT surfaces: this pass fogs what is
			 * already in the frame, before the transparent queue, so the sea drawn after it came out
			 * clear through the thickest fog. It samples the same volume at its own depth. w is 1 when
			 * the volume is live this frame, 0 when it is not. */
			public static readonly int AirVolumeId = Shader.PropertyToID("_FishAirFogVolume");
			public static readonly int AirVolumeRangeId = Shader.PropertyToID("_FishAirFogVolumeRange");

			private ComputeShader compute;
			private Material material;
			private FishVolumetricFogFeature settings;
			private RenderTexture scatter, integrated;
			private RTHandle scatterHandle, integratedHandle;
			private int scatterKernel = -1, integrateKernel = -1;
			private LocalKeyword shadowsKeyword;
			private ComputeShader keywordFor;

			// Stand-ins for a sky that has not bound its textures: an even fog on flat ground.
			private static Texture3D flatShape;

			/// <summary>Takes the frame's settings and makes sure there are volumes to write; false when there cannot be.</summary>
			public bool Setup(ComputeShader computeShader, Material applyMaterial, FishVolumetricFogFeature feature)
			{
				compute = computeShader;
				material = applyMaterial;
				settings = feature;
				if (scatterKernel < 0 || keywordFor != compute)
				{
					// A compute shader that failed to build still loads, without its kernels: the analytic
					// pass then draws the whole layer rather than this throwing every frame.
					if (!compute.HasKernel("Scatter") || !compute.HasKernel("Integrate"))
					{
						return false;
					}
					scatterKernel = compute.FindKernel("Scatter");
					integrateKernel = compute.FindKernel("Integrate");
					shadowsKeyword = new LocalKeyword(compute, ShadowsKeyword);
					keywordFor = compute;
				}
				return EnsureVolumes(settings.GridWidth, settings.GridHeight, settings.GridDepth);
			}

			/// <summary>Carries no falls' mist this frame. Returns 0, the count, for the caller to test like <see cref="GatherPlumes"/>'s.</summary>
			public int ClearPlumes()
			{
				plumeCount = 0;
				return 0;
			}

			/// <summary>
			/// Picks the falls whose mist this camera's volume carries — the <see cref="MaxPlumes"/> nearest
			/// whose mist reaches within <paramref name="far"/> of <paramref name="eye"/> — and works out each
			/// one's plume for the compute. Returns how many.
			/// </summary>
			/// <remarks>
			/// <para>
			/// <b>The plume</b> is the size of the fall's power (<see cref="Waterfalls.PlumeRadius"/>,
			/// <see cref="Waterfalls.PlumeHeight"/>), and as thick in its boil as <see cref="PeakExtinction"/>
			/// says. Its top leans off the landing by as far as the wind carries the mist in the time it takes
			/// to climb there (<see cref="PlumeRise"/>), plus the water's own throw downstream — never more
			/// than the plume is high: past that a plume is bent flat along the ground and its mist is wiped
			/// off the pool into the wind faster than it climbs, which a straight leaning plume cannot draw.
			/// </para>
			/// <para>
			/// Worked out afresh every frame rather than kept until <see cref="Waterfalls.Version"/> moves: it is a
			/// handful of arithmetic a fall, the nearest change as the camera moves, and the lean follows the
			/// wind as it turns.
			/// </para>
			/// </remarks>
			public int GatherPlumes(Vector3 eye, float far)
			{
				plumeCount = 0;
				IReadOnlyList<Waterfalls.Fall> falls = Waterfalls.All;
				if (falls.Count == 0)
				{
					return 0;
				}
				Vector4 windGlobal = Shader.GetGlobalVector(WeatherShaderGlobals.Wind);
				Vector2 wind = new Vector2(windGlobal.x, windGlobal.y) * (Mathf.Clamp01(windGlobal.z) * WindChannelMetres);
				for (int i = 0; i < falls.Count; i++)
				{
					Waterfalls.Fall fall = falls[i];
					float peak = PeakExtinction(fall.Power);
					if (!(peak > 1e-3f))
					{
						continue;
					}
					float radius = Waterfalls.PlumeRadius(fall);
					float height = Waterfalls.PlumeHeight(fall);
					float veil = 0.5f * Mathf.Max(0f, fall.Width) + 1f;
					Vector2 downstream = new Vector2(fall.Downstream.x, fall.Downstream.z);
					Vector2 lean = wind * (height / PlumeRise) + downstream * (PlumeThrow * radius);
					if (lean.sqrMagnitude > height * height)
					{
						lean *= height / lean.magnitude;
					}

					// A sphere round all of it, for the kernel to skip the fall by and for this to rank it: the
					// plume's axis from the landing to its leaning top, as wide as its spread and reaching a
					// metre and a half under the landing (FallPlume), and the curtain from the lip down.
					Vector3 landing = fall.Landing;
					Vector3 top = landing + new Vector3(lean.x, height, lean.y);
					Vector3 lip = fall.Lip;
					Vector3 centre = 0.5f * (Vector3.Min(Vector3.Min(landing, top), lip) + Vector3.Max(Vector3.Max(landing, top), lip));
					float toLanding = Vector3.Distance(landing, centre);
					float bounds = Mathf.Max(Mathf.Max(toLanding, Vector3.Distance(top, centre)) + radius + 1.5f,
						Mathf.Max(toLanding, Vector3.Distance(lip, centre)) + veil);
					float distance = Mathf.Max(0f, Vector3.Distance(eye, centre) - bounds);
					if (!(distance <= far))
					{
						continue;
					}

					// Nearest first: an insertion into the short list, the farthest falling off its end.
					int at = plumeCount;
					while (at > 0 && plumeDistance[at - 1] > distance)
					{
						at--;
					}
					if (at >= MaxPlumes)
					{
						continue;
					}
					int last = Mathf.Min(plumeCount, MaxPlumes - 1);
					for (int j = last; j > at; j--)
					{
						plumeA[j] = plumeA[j - 1];
						plumeB[j] = plumeB[j - 1];
						plumeC[j] = plumeC[j - 1];
						plumeD[j] = plumeD[j - 1];
						plumeDistance[j] = plumeDistance[j - 1];
					}
					plumeA[at] = new Vector4(landing.x, landing.y, landing.z, radius);
					plumeB[at] = new Vector4(lean.x, lean.y, height, peak);
					plumeC[at] = new Vector4(lip.x, lip.y, lip.z, veil);
					plumeD[at] = new Vector4(centre.x, centre.y, centre.z, bounds);
					plumeDistance[at] = distance;
					plumeCount = Mathf.Min(plumeCount + 1, MaxPlumes);
				}
				return plumeCount;
			}

			/// <summary>How far the mist's billows have risen, m, wrapped to their tile so the wrap is a whole tile and never a jump.</summary>
			private static float BillowRise()
			{
				double risen = WorldMotion.Seconds * PlumeRise;
				return (float)(risen - System.Math.Floor(risen / BillowTile) * BillowTile);
			}

			public void Dispose()
			{
				scatterHandle?.Release();
				integratedHandle?.Release();
				scatterHandle = null;
				integratedHandle = null;
				if (scatter != null) { scatter.Release(); scatter = null; }
				if (integrated != null) { integrated.Release(); integrated = null; }
			}

			private bool EnsureVolumes(int w, int h, int d)
			{
				if (scatter != null && scatter.width == w && scatter.height == h && scatter.volumeDepth == d && scatter.IsCreated() && integrated.IsCreated())
				{
					return true;
				}
				Dispose();
				var desc = new RenderTextureDescriptor(w, h, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					dimension = TextureDimension.Tex3D,
					volumeDepth = d,
					enableRandomWrite = true,
					msaaSamples = 1,
				};
				scatter = new RenderTexture(desc) { name = "FishVolFogScatter", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
				integrated = new RenderTexture(desc) { name = "FishVolFogIntegrated", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
				if (!scatter.Create() || !integrated.Create())
				{
					Dispose();
					return false;
				}
				// Wrapped, not owned: the handles only let the render graph track the volumes' use.
				scatterHandle = RTHandles.Alloc(scatter);
				integratedHandle = RTHandles.Alloc(integrated);
				return true;
			}

			private static Texture3D FlatShape()
			{
				if (flatShape == null)
				{
					flatShape = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { name = "Fog Flat Shape", hideFlags = HideFlags.HideAndDontSave };
					flatShape.SetPixel(0, 0, 0, Color.white);
					flatShape.Apply(false, true);
				}
				return flatShape;
			}

			private class ScatterData
			{
				public ComputeShader Compute;
				public int Kernel;
				public TextureHandle Scatter;
				public TextureHandle Shadows;
				public bool UseShadows;
				public int GroupsX, GroupsY, GroupsZ;
				// The falls, copied: the pass's own lists are refilled for the next camera.
				public Vector4[] PlumeA = new Vector4[MaxPlumes];
				public Vector4[] PlumeB = new Vector4[MaxPlumes];
				public Vector4[] PlumeC = new Vector4[MaxPlumes];
				public Vector4[] PlumeD = new Vector4[MaxPlumes];
			}

			private class IntegrateData
			{
				public ComputeShader Compute;
				public int Kernel;
				public TextureHandle Scatter;
				public TextureHandle Integrated;
				public int GroupsX, GroupsY;
			}

			private class ApplyData
			{
				public Material Material;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				// No volume for transparents unless this pass fills one below.
				Shader.SetGlobalVector(AirVolumeRangeId, Vector4.zero);
				if (compute == null || material == null || settings == null || scatterHandle == null)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}
				FogLayerView layer = FogLayerView.Current;
				// The layer is this volume's to draw unless the cloud march has drawn it for this camera
				// already (it records before this pass). The falls' mist is this volume's whoever drew the
				// layer: the march draws none. Neither, and there is nothing to run for.
				bool drawsLayer = layer.Visible && !FogLayerView.MarchedThisFrame;
				int plumes = plumeCount;
				if (!drawsLayer && plumes == 0)
				{
					return;
				}

				int w = settings.GridWidth, h = settings.GridHeight, d = settings.GridDepth;
				float near = settings.NearDistance;
				float far = Mathf.Max(near + 1f, settings.FarDistance);

				Camera camera = cameraData.camera;
				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
				Vector3 eye = cameraData.worldSpaceCameraPos;
				Vector3 forward = camera.transform.forward;
				// How wide a froxel is for each metre of depth: what a far look has to stand for across the ray.
				float across = 2f * Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad) / Mathf.Max(1, h);

				// The sky's own textures, where it has bound them: its shape volume for the fog's
				// structure, its terrain for where the ground and the pooled air lie.
				Texture shape = Shader.GetGlobalTexture(ShapeTexId);
				bool structured = shape != null && shape.dimension == TextureDimension.Tex3D;
				// And its detail volume, for the eddies of the fog's top.
				Texture detail = Shader.GetGlobalTexture(DetailTexId);
				bool eddies = structured && detail != null && detail.dimension == TextureDimension.Tex3D;
				Texture terrain = Shader.GetGlobalTexture(TerrainId);
				Vector4 terrainRect = terrain != null ? Shader.GetGlobalVector(TerrainRectId) : Vector4.zero;

				FogLayerView.Lighting lighting = FogLayerView.PublishLighting();
				Vector4 motion = FogLayerView.Motion;

				// Set on the shader object while recording; the kernels run from it straight after.
				compute.SetMatrix(InverseVPId, (projection * view).inverse);
				compute.SetVector(SizeId, new Vector4(w, h, d, 0f));
				compute.SetVector(RangeId, new Vector4(near, far, settings.DepthCurve, across));
				compute.SetVector(CameraId, new Vector4(eye.x, eye.y, eye.z, structured ? 1f : 0f));
				compute.SetVector(ForwardId, new Vector4(forward.x, forward.y, forward.z, eddies ? 1f : 0f));
				compute.SetVector(LayerId, new Vector4(layer.Extinction, layer.Depth, layer.Lift, layer.TopSoftness));
				compute.SetVector(ShapeId, new Vector4(layer.Heave, layer.Patchiness, motion.z, motion.w));
				compute.SetVector(DriftId, new Vector4(motion.x, motion.y, 0f, 0f));
				compute.SetVector(LightId, lighting.ToLight);
				compute.SetVector(LightColorId, lighting.LightColor);
				compute.SetVector(AmbientId, lighting.Ambient);
				compute.SetVector(TerrainRectId, terrainRect);
				compute.SetTexture(scatterKernel, ShapeTexId, structured ? shape : FlatShape());
				compute.SetTexture(scatterKernel, DetailTexId, eddies ? detail : FlatShape());
				compute.SetTexture(scatterKernel, TerrainId, terrain != null ? terrain : Texture2D.blackTexture);
				Texture shadeNear = Shader.GetGlobalTexture(ShadeNearId);
				Texture shadeFar = Shader.GetGlobalTexture(ShadeFarId);
				compute.SetTexture(scatterKernel, ShadeNearId, shadeNear != null ? shadeNear : Texture2D.blackTexture);
				compute.SetTexture(scatterKernel, ShadeFarId, shadeFar != null ? shadeFar : Texture2D.blackTexture);
				compute.SetVector(ShadeNearRectId, shadeNear != null ? Shader.GetGlobalVector(ShadeNearRectId) : Vector4.zero);
				compute.SetVector(ShadeFarRectId, shadeFar != null ? Shader.GetGlobalVector(ShadeFarRectId) : Vector4.zero);
				compute.SetFloat(ShadeNearBaseId, Shader.GetGlobalFloat(ShadeNearBaseId));
				compute.SetFloat(ShadeFarBaseId, Shader.GetGlobalFloat(ShadeFarBaseId));
				// The layer's own values go in either way: the mist is lit through the layer above it, as the
				// march lit the layer it drew. Whether the volume holds the layer itself is y.
				compute.SetVector(FallMistId, new Vector4(plumes, drawsLayer ? 1f : 0f, plumes > 0 ? BillowRise() : 0f, 0f));

				// The shafts: only with a shadow map this frame to read them from.
				TextureHandle shadows = TextureHandle.nullHandle;
				bool useShadows = false;
				if (settings.LightShafts && resourceData.mainShadowsTexture.IsValid())
				{
					shadows = resourceData.mainShadowsTexture;
					useShadows = true;
				}
				compute.SetKeyword(shadowsKeyword, useShadows);

				TextureHandle scatterTexture = renderGraph.ImportTexture(scatterHandle);
				TextureHandle integratedTexture = renderGraph.ImportTexture(integratedHandle);

				using (var builder = renderGraph.AddComputePass<ScatterData>(ScatterName, out ScatterData data))
				{
					data.Compute = compute;
					data.Kernel = scatterKernel;
					data.Scatter = scatterTexture;
					data.Shadows = shadows;
					data.UseShadows = useShadows;
					data.GroupsX = Mathf.CeilToInt(w / 8f);
					data.GroupsY = Mathf.CeilToInt(h / 8f);
					data.GroupsZ = Mathf.CeilToInt(d / 4f);
					System.Array.Copy(plumeA, data.PlumeA, MaxPlumes);
					System.Array.Copy(plumeB, data.PlumeB, MaxPlumes);
					System.Array.Copy(plumeC, data.PlumeC, MaxPlumes);
					System.Array.Copy(plumeD, data.PlumeD, MaxPlumes);
					builder.UseTexture(scatterTexture, AccessFlags.Write);
					if (useShadows)
					{
						builder.UseTexture(shadows, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((ScatterData s, ComputeGraphContext context) =>
					{
						context.cmd.SetComputeTextureParam(s.Compute, s.Kernel, ScatterTexId, s.Scatter);
						if (s.UseShadows)
						{
							context.cmd.SetComputeTextureParam(s.Compute, s.Kernel, MainShadowsId, s.Shadows);
						}
						// Always all sixteen, the unused ones included: the count says how many are read,
						// and a shorter array would leave the kernel's tail as whatever was set last.
						context.cmd.SetComputeVectorArrayParam(s.Compute, PlumeAId, s.PlumeA);
						context.cmd.SetComputeVectorArrayParam(s.Compute, PlumeBId, s.PlumeB);
						context.cmd.SetComputeVectorArrayParam(s.Compute, PlumeCId, s.PlumeC);
						context.cmd.SetComputeVectorArrayParam(s.Compute, PlumeDId, s.PlumeD);
						context.cmd.DispatchCompute(s.Compute, s.Kernel, s.GroupsX, s.GroupsY, s.GroupsZ);
					});
				}

				using (var builder = renderGraph.AddComputePass<IntegrateData>(IntegrateName, out IntegrateData data))
				{
					data.Compute = compute;
					data.Kernel = integrateKernel;
					data.Scatter = scatterTexture;
					data.Integrated = integratedTexture;
					data.GroupsX = Mathf.CeilToInt(w / 8f);
					data.GroupsY = Mathf.CeilToInt(h / 8f);
					builder.UseTexture(scatterTexture, AccessFlags.Read);
					builder.UseTexture(integratedTexture, AccessFlags.Write);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((IntegrateData s, ComputeGraphContext context) =>
					{
						context.cmd.SetComputeTextureParam(s.Compute, s.Kernel, ScatterTexId, s.Scatter);
						context.cmd.SetComputeTextureParam(s.Compute, s.Kernel, IntegratedTexId, s.Integrated);
						context.cmd.DispatchCompute(s.Compute, s.Kernel, s.GroupsX, s.GroupsY, 1);
					});
				}

				material.SetTexture(VolumeId, integrated);
				material.SetVector(RangeId, new Vector4(near, far, settings.DepthCurve, d));
				// Published on the CPU from the volume itself — a plain texture the pass owns, never a
				// graph target — for the water to lay over itself.
				Shader.SetGlobalTexture(AirVolumeId, integrated);
				Shader.SetGlobalVector(AirVolumeRangeId, new Vector4(near, far, settings.DepthCurve, 1f));
				// For the mist's billboards: this camera's volume carries the falls' mist from here on.
				Shader.SetGlobalFloat(FallMistVolumetricId, plumes > 0 ? 1f : 0f);

				using (var builder = renderGraph.AddRasterRenderPass<ApplyData>(ApplyName, out ApplyData data))
				{
					data.Material = material;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseAllGlobalTextures(true);
					builder.UseTexture(integratedTexture, AccessFlags.Read);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((ApplyData a, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, Vector2.one, a.Material, 0));
				}
			}
		}
	}
}
