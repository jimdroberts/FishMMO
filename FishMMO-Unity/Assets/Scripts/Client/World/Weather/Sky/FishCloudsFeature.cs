using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the volumetric clouds: a raymarch at a fraction of the screen, rebuilt against the frames
	/// before it, then composited over the sky and behind the world.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The clouds are a volume, not a picture painted on the sky. Everything about their shape,
	/// place and lighting comes from the globals <see cref="SkySystem"/> sets each frame, so this
	/// feature only has to march them: it owns the buffers and the order, nothing else.
	/// </para>
	/// <para>
	/// One system on every tier, scaled: the resolution, the number of steps, the detail, the
	/// temporal blend and how finely the result is rebuilt all come from the quality tier, so a browser
	/// and a desktop draw the same sky at different costs. The pass asks for the depth texture itself,
	/// so no pipeline asset has to be changed for it.
	/// </para>
	/// <para>
	/// The march's resolution is what it costs, not what it shows. Each frame its rays look through
	/// a different one of sixteen places inside their texels, and the steadying — a temporal upsampler
	/// (FishCloudResolve.hlsl: a tent over the texels' true places, the history carried with each
	/// cloud's own band drift, variance-clipped to this frame's neighbourhood, a moving average over
	/// sixteen frames) — averages those places into the rebuilt buffer, so the march's grid never shows.
	/// The march writes a second, small target for it: each texel's mean cloud distance and its bands'
	/// mean wind gain (<see cref="CloudPass.MotionFormat"/>). What the steadying costs is memory: each
	/// camera keeps two half-float buffers of clouds and two of weights at the rebuilt size — twenty
	/// bytes a pixel, about 41 MB at 1920×1080 and 74 MB at 2560×1440, which is also where it stops
	/// growing (<see cref="CloudPass.MaxHistoryPixels"/>).
	/// </para>
	/// <para>
	/// <b>Where the GPU time goes, and the two ways the steadying runs.</b> The steadying reads 34
	/// textures for every pixel it rebuilds whatever the tier, which on the cheapest tier costs about as
	/// much as the march it steadies. Where the platform runs compute kernels well
	/// (<see cref="ComputeResolveSupported"/>) it runs as <c>FishCloudResolve.compute</c>, which loads
	/// the marched texels an 8×8 group of pixels shares once into groupshared memory (about 8 reads a
	/// pixel instead of 34) and writes clear air outright where nothing but clear air was marched; the
	/// same arithmetic stays as pass 1 of the cloud shader for everywhere else. Both skip a pixel whose
	/// own ray ends below the lowest cloud there can be. And a tier may rebuild at less than the
	/// screen (<see cref="WeatherTierSettings.CloudHistoryScale"/>): Performant does, at about two thirds, and the
	/// composite's depth-weighted taps bring it the rest of the way.
	/// </para>
	/// </remarks>
	public class FishCloudsFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/Weather/Clouds";
		/// <summary>The Scene view's march, as a share of the tier's across: half, a quarter of the rays.</summary>
		public const float SceneViewShare = 0.5f;

		// The composite's softening (an edge-aware Gaussian over the rebuilt buffer, in screen pixels) is
		// the profile's now: VolumetricCloudDiagnostics.BlurPixels, 3 as shipped — see its remarks.

		public const string ResolveComputeName = "FishCloudResolve";
		public const string WeatherMapComputeName = "FishWeatherMap";

		[Tooltip("The cloud shader's material. Left empty, the feature finds the shader itself.")]
		public Material CloudMaterial;

		[Tooltip("The steadying as a compute kernel (FishCloudResolve.compute). Left empty, the feature finds it by name in the editor; a build needs it assigned. Without it the shader's own pass does the same work.")]
		public ComputeShader ResolveCompute;

		[Tooltip("The storms' weather map laid down on the GPU (FishWeatherMap.compute). Left empty, the feature finds it by name in the editor; a build needs it assigned. Without it the map is rasterised on the CPU.")]
		public ComputeShader WeatherMapCompute;

		private CloudPass pass;

		/// <summary>
		/// Lets the compute kernels run where the platform supports them. A switch for the probes, which
		/// render the same frame both ways to compare them; nothing in the game turns it off.
		/// </summary>
		public static bool AllowCompute = true;

		/// <summary>
		/// Lets the compute kernels run on WebGPU. Off until a WebGPU build has been seen to draw them.
		/// </summary>
		/// <remarks>
		/// WebGPU compiles a kernel through GLSL to WGSL, where a storage texture has to name its texel
		/// format and may only be written, not read, unless it is one of the 32-bit single-channel
		/// formats. These kernels only write their targets and read everything else as ordinary
		/// textures, and they write the same float4-into-half-float targets the froxel fog already does
		/// (FishVolumetricFog.compute) — so if the fog draws on WebGPU these will too. Until that has
		/// been seen on a WebGPU build the fragment pass and the CPU raster, which are known to work
		/// there, do the job: a slower sky is better than none on the platform the game is first for.
		/// </remarks>
		public static bool ComputeOnWebGPU = false;

		/// <summary>
		/// The storms' weather-map kernel, for <see cref="WeatherMap"/>; null where it cannot run, and
		/// the map is rasterised on the CPU.
		/// </summary>
		public static ComputeShader WeatherMapShader => ComputeAllowed ? weatherMapShader : null;
		private static ComputeShader weatherMapShader;

		/// <summary>
		/// Whether this machine runs these compute kernels at all: compute shaders, a graphics API they are
		/// proven on, and half-float targets a kernel can write.
		/// </summary>
		/// <remarks>
		/// OpenGL and OpenGL ES are left to the fragment pass: WebGL has no compute at all, and GLES's
		/// image formats are fixed at compile time from the declared type, which for a float4 is 32-bit —
		/// not the half-float targets these write. Nothing this project ships runs compute on either.
		/// </remarks>
		public static bool ComputeAllowed
		{
			get
			{
				if (!AllowCompute || !SystemInfo.supportsComputeShaders)
				{
					return false;
				}
				switch (SystemInfo.graphicsDeviceType)
				{
					case GraphicsDeviceType.OpenGLCore:
					case GraphicsDeviceType.OpenGLES3:
					case GraphicsDeviceType.Null:
						return false;
					case GraphicsDeviceType.WebGPU:
						if (!ComputeOnWebGPU)
						{
							return false;
						}
						break;
				}
				return SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.LoadStore);
			}
		}

		/// <summary>Whether the steadying runs as a compute kernel on this machine (<see cref="ComputeAllowed"/>, and a weight format it can write).</summary>
		public static bool ComputeResolveSupported => ComputeAllowed && CloudPass.WeightFormat(true) != GraphicsFormat.None;

		/// <summary>
		/// The cloud buffer the last frame ended with: rgb scattered light, a transmittance (1 clear
		/// sky, 0 solid cloud), at the rebuilt resolution. A game camera's, when one has drawn —
		/// in the editor the Scene view draws too, and it is the game's sky that is being measured.
		/// The probe reads coverage off this instead of guessing it from pixel colours, which glare
		/// and sunlit tops both fool.
		/// </summary>
		public static Texture LastCloudBuffer => lastPass != null ? lastPass.LastBuffer : null;
		private static CloudPass lastPass;

		/// <summary>Whether the last frame's steadying ran as the compute kernel, for the probes to report.</summary>
		public static bool LastResolvedByCompute => lastPass != null && lastPass.LastByCompute;

		/// <summary>
		/// The reconstruction options under trial (issue #238: "the towers look very spotty"), all off
		/// by default, and off is the rendering exactly as it was. Set by the probes from
		/// <c>FISHMMO_CLOUD_OPTIONS</c> so each can be rendered alone and together; nothing in the game
		/// sets it, and product code reads no environment. See <see cref="CloudOptions"/>.
		/// </summary>
		public static CloudOptions Options;

		/// <summary>
		/// Option B's narrow kernel's width, in pixels of the rebuilt buffer, which its halo was measured
		/// from (<see cref="CloudOptions.BlurSigma2"/>). Option B is no longer read by the steadying.
		/// </summary>
		public const float SampleSigmaPixels = 0.55f;

		/// <summary>Places inside a texel the march cycles through.</summary>
		public const int SubPixelPlaces = 16;

		/// <summary>
		/// The fewest frames a settled pixel averages over (<c>_FishCloudTemporal.x</c>): its moving average
		/// takes each new frame as one part in this many (α = 1/16). A whole cycle of the
		/// <see cref="SubPixelPlaces"/> places, so they average into the picture rather than shimmer through
		/// it, and with them the rays' phases (<see cref="CloudOptions.JitterPhase"/>). MEASURED on a CPU
		/// copy (four pixels a texel, noise of 0.03 on every ray, 128 frames): 0.0038 of it left. The
		/// profile's <c>TemporalBlend</c> may ask for longer (a blend b is b / (1 − b) frames), never shorter.
		/// </summary>
		public const float SettledFrames = SubPixelPlaces;

		/// <summary>The places a side: a texel's <see cref="SubPixelPlaces"/> places are its four-by-four cells.</summary>
		public const int SubPixelSide = 4;

		/// <summary>
		/// Which of the <see cref="SubPixelPlaces"/> places inside its texel the march looks through on
		/// <paramref name="frame"/>, 0-based (<see cref="SubPixelPlace"/> says where it is), the order
		/// turned on by one every 64 frames (the ray jitter's own period) so a place never keeps the same
		/// few ray phases.
		/// </summary>
		public static int SubPixelSlot(int frame) => (frame + frame / 64) % SubPixelPlaces;

		/// <summary>
		/// The 4 × 4 Bayer (ordered-dither) matrix: the frame, within a cycle, on which each cell of a texel
		/// is looked through, row by row (Bayer, "An optimum method for two-level rendition of
		/// continuous-tone pictures", 1973). Consecutive frames land as far apart as a four-by-four grid
		/// allows — the first four on the four corners of a two-by-two, the next four between them — so
		/// any run of frames after a reset is already spread over the texel. It is the order Horizon Zero
		/// Dawn's clouds update one pixel in sixteen by (Schneider, "The Real-time Volumetric Cloudscapes of
		/// Horizon Zero Dawn", SIGGRAPH 2015).
		/// </summary>
		private static readonly int[] BayerFrames = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

		/// <summary>
		/// Where place <paramref name="slot"/> is inside its texel, in texels either side of the middle
		/// (-0.5..0.5): the middle of the four-by-four cell the Bayer order gives that frame of the cycle.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cells' middles: a stratified cover of the texel, one place in each sixteenth, so the
		/// steadying's tent averaged over one cycle is the tent convolved with the sky and nothing of the
		/// grid is left. On a tier that rebuilds at four pixels a texel
		/// (<see cref="CloudTierSettings.AutoPixelsPerTexel"/>) they are also the texel's sixteen pixels'
		/// middles, so every pixel is looked through once a cycle by the ray a full-resolution march
		/// would cast for it. The sixteen Halton (2, 3) places used until 2026-09-28 left two pixels of
		/// every sixteen never looked through (two others twice).
		/// </para>
		/// <para>
		/// Where a texel spans fewer pixels (3⅓ on High, where the rebuild meets the screen), the cells are
		/// narrower than a pixel and every pixel still holds at least one of them.
		/// </para>
		/// </remarks>
		public static Vector2 SubPixelPlace(int slot)
		{
			int cell = System.Array.IndexOf(BayerFrames, ((slot % SubPixelPlaces) + SubPixelPlaces) % SubPixelPlaces);
			int x = cell % SubPixelSide;
			int y = cell / SubPixelSide;
			return new Vector2((x + 0.5f) / SubPixelSide - 0.5f, (y + 0.5f) / SubPixelSide - 0.5f);
		}

		/// <summary>
		/// Marched texels across <paramref name="rebuilt"/> pixels for a tier that asked for
		/// <paramref name="asked"/>: never fewer than one for every <see cref="SubPixelSide"/> pixels, so
		/// each pixel holds a place of its own (<see cref="SubPixelPlace"/>), and never fewer than 16.
		/// </summary>
		public static int MarchTexelsFor(int rebuilt, int asked) =>
			Mathf.Max(Mathf.Max(16, asked), (rebuilt + SubPixelSide - 1) / SubPixelSide);

		/// <summary>
		/// Rebuilt pixels to a marched texel on the last game camera drawn: 4 on a tier that leaves the
		/// rebuild to the renderer. Option C's floor is in marched texels while the march's detail cone is
		/// now a rebuilt pixel's (<see cref="CloudOptions.DetailConeShare"/>), and this converts one to the
		/// other. Before any camera has drawn, the renderer's own choice.
		/// </summary>
		public static float PixelsPerMarchedTexel { get; private set; } = CloudTierSettings.AutoPixelsPerTexel;

		public override void Create()
		{
			pass = new CloudPass
			{
				// After the skybox and the opaque world, before transparents: the clouds sit behind
				// anything on the ground and in front of the sky.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
			ResolveCompute = FindCompute(ResolveCompute, ResolveComputeName);
			WeatherMapCompute = FindCompute(WeatherMapCompute, WeatherMapComputeName);
			if (WeatherMapCompute != null)
			{
				weatherMapShader = WeatherMapCompute;
			}
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Material material = Resolve();
			if (material == null || !SkySystem.CloudsReady)
			{
				return;
			}
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			// The Scene view draws the clouds only with its Fog effect on, as URP's own fog does: in play
			// mode a visible Scene view repaints every frame, and it was paying for the whole march a
			// second time behind the game view.
			if (renderingData.cameraData.cameraType == CameraType.SceneView && !CoreUtils.IsSceneViewFogEnabled(renderingData.cameraData.camera))
			{
				return;
			}
			/* Under the sea nothing of the sky reaches the eye (the water's own volume draws what is above): the whole
			 * march was being paid for behind it, 27 ms of a 42 ms frame at 2560×1440 on High (ScenePerfProbe). */
			if (SurfaceWater.IsUnder(renderingData.cameraData.camera.transform.position))
			{
				return;
			}
			pass.Setup(material, ResolveCompute);
			lastPass = pass;
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (CloudMaterial != null)
			{
				return CloudMaterial;
			}
			Shader shader = Shader.Find(ShaderName);
			if (shader == null)
			{
				return null;
			}
			CloudMaterial = CoreUtils.CreateEngineMaterial(shader);
			return CloudMaterial;
		}

		/// <summary>A compute shader as assigned, or found by name in the editor; a build must have it assigned.</summary>
		private static ComputeShader FindCompute(ComputeShader assigned, string name)
		{
			if (assigned != null)
			{
				return assigned;
			}
			#if UNITY_EDITOR
			foreach (string guid in UnityEditor.AssetDatabase.FindAssets($"{name} t:ComputeShader"))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
				{
					return UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
				}
			}
			#endif
			return null;
		}

		protected override void Dispose(bool disposing)
		{
			pass?.Dispose();
			pass = null;
		}

		/// <summary>
		/// Height above the sea over a curved world of this radius, m: FishCloudAltitude's form
		/// (FishCloudVolume.hlsl), which never subtracts two planet-sized numbers —
		/// |p − c|² − R² = p·p + 2R·p.y, and h = that / (R + √(R² + that)).
		/// </summary>
		public static double AltitudeOver(Vector3 position, double radius)
		{
			double over = (double)position.x * position.x + (double)position.y * position.y + (double)position.z * position.z
				+ 2.0 * radius * position.y;
			return over / (radius + System.Math.Sqrt(System.Math.Max(0.0, radius * radius + over)));
		}

		/// <summary>The march, the steadying and the composite, in one pass object.</summary>
		private sealed class CloudPass : ScriptableRenderPass
		{
			private const string MarchName = "Fish Clouds (march)";
			private const string TemporalName = "Fish Clouds (steady)";
			private const string TemporalComputeName = "Fish Clouds (steady, compute)";
			private const string CompositeName = "Fish Clouds (composite)";
			private const string RayName = "Fish Clouds (god rays)";
			private const string RayCompositeName = "Fish Clouds (god rays composite)";

			/// <summary>
			/// The most pixels a camera's rebuilt clouds are kept at. Up to 2560×1440 that is the
			/// screen itself; past it (a 4K display) the buffer is scaled down to this many and the
			/// composite scales it back up with its depth-weighted taps. Twenty bytes a pixel is 74 MB
			/// here and would be 166 MB at 3840×2160, for detail a march at a fraction of the
			/// screen does not have in the first place.
			/// </summary>
			public const int MaxHistoryPixels = 2560 * 1440;

			/// <summary>
			/// How far, in metres, a camera may move in one frame before its history is thrown away.
			/// Further than that it has been put somewhere, not moved there — a teleport, a cut, a
			/// probe setting up its next shot — and what the last frame saw is somewhere else's sky.
			/// Turning needs no such rule: a turned camera's history is carried across exactly, and
			/// what comes into view is new to it either way.
			/// </summary>
			private const float CutDistance = 100f;

			/// <summary>
			/// How far, in metres, the air may carry the clouds in one frame before the history is
			/// thrown away. The World Sim bed's fast clock moves them a kilometre and a half a second;
			/// further than this in a frame is the clock being set, not run, and the history is of
			/// another sky.
			/// </summary>
			private const float MaxDriftStep = 20000f;

			/// <summary>
			/// Seconds a camera may go undrawn before its buffers are let go. Generous on purpose: in
			/// the editor the Scene view draws only when something changes, and each time its history
			/// is let go it has to build up again.
			/// </summary>
			private const float ForgetAfterSeconds = 120f;

			/// <summary>Places inside a texel the march cycles through.</summary>
			private const int SubPixelCycle = SubPixelPlaces;

			private static readonly int InverseVPId = Shader.PropertyToID("_FishCloudInverseVP");
			private static readonly int PreviousVPId = Shader.PropertyToID("_FishCloudPreviousVP");
			private static readonly int MarchParamsId = Shader.PropertyToID("_FishCloudMarchParams");
			private static readonly int LodParamsId = Shader.PropertyToID("_FishCloudLodParams");
			private static readonly int TemporalId = Shader.PropertyToID("_FishCloudTemporal");
			private static readonly int JitterId = Shader.PropertyToID("_FishCloudJitter");
			private static readonly int UpsampleId = Shader.PropertyToID("_FishCloudUpsample");
			private static readonly int MotionId = Shader.PropertyToID("_FishCloudMotion");
			private static readonly int CompositeId = Shader.PropertyToID("_FishCloudComposite");
			private static readonly int CurrentId = Shader.PropertyToID("_FishCloudCurrent");
			private static readonly int CurrentMotionId = Shader.PropertyToID("_FishCloudCurrentMotion");
			private static readonly int HistoryId = Shader.PropertyToID("_FishCloudHistory");
			private static readonly int HistoryWeightId = Shader.PropertyToID("_FishCloudHistoryWeight");
			private static readonly int BufferId = Shader.PropertyToID("_FishCloudBuffer");
			private static readonly int ScreenId = Shader.PropertyToID("_FishCloudScreen");
			private static readonly int BufferTexelId = Shader.PropertyToID("_FishCloudBufferTexel");
			private static readonly int RaySourceId = Shader.PropertyToID("_FishGodRaySource");
			private static readonly int RayBufferId = Shader.PropertyToID("_FishGodRayBuffer");
			private static readonly int RayCloudsId = Shader.PropertyToID("_FishGodRayClouds");
			private static readonly int RayDirId = Shader.PropertyToID("_FishGodRayDir");
			private static readonly int RayParamsId = Shader.PropertyToID("_FishGodRayParams");
			private static readonly int RayColorId = Shader.PropertyToID("_FishGodRayColor");
			private static readonly int RayMaskId = Shader.PropertyToID("_FishGodRayMask");
			private static readonly int RayVPId = Shader.PropertyToID("_FishGodRayVP");
			private static readonly int RayAirId = Shader.PropertyToID("_FishGodRayAir");
			private static readonly int RayLaneId = Shader.PropertyToID("_FishGodRayLane");
			private static readonly int ResolveShellId = Shader.PropertyToID("_FishCloudResolveShell");
			private static readonly int ResolveCameraId = Shader.PropertyToID("_FishCloudResolveCamera");
			private static readonly int ResolveForwardId = Shader.PropertyToID("_FishCloudResolveForward");
			private static readonly int ResolveDepthSizeId = Shader.PropertyToID("_FishCloudResolveDepth");
			private static readonly int ResolveZId = Shader.PropertyToID("_FishCloudResolveZ");
			private static readonly int ResolveDepthId = Shader.PropertyToID("_FishCloudDepth");
			private static readonly int ResolvedId = Shader.PropertyToID("_FishCloudResolved");
			private static readonly int ResolvedWeightId = Shader.PropertyToID("_FishCloudResolvedWeight");
			private static readonly int CloudLayerId = Shader.PropertyToID("_FishCloudLayer");
			private static readonly int OptionsId = Shader.PropertyToID("_FishCloudOptions");
			private static readonly int FramePhaseId = Shader.PropertyToID("_FishCloudFramePhase");

			/// <summary>The kernel's thread group, a side: FISH_RESOLVE_GROUP in FishCloudResolve.compute.</summary>
			private const int ResolveGroup = 8;

			private Material material;
			private ComputeShader resolveCompute;
			private int resolveKernel = -1;
			private ComputeShader kernelOf;

			/// <summary>
			/// Each camera's own history. One shared between them — as there used to be — is
			/// reprojected with the other camera's matrices whenever the editor's Scene and Game views
			/// both draw, and reallocated every frame when they differ in size; the old steadying got
			/// away with that, being a light blend, but a history that holds the detail itself never
			/// settles on either.
			/// </summary>
			private readonly Dictionary<Camera, CloudHistory> histories = new Dictionary<Camera, CloudHistory>();
			private readonly List<Camera> forgotten = new List<Camera>();
			private CloudHistory lastDrawn;
			private CloudHistory lastGameDrawn;

			/// <summary>The buffer the last frame's composite was drawn from; a game camera's when there is one.</summary>
			public Texture LastBuffer => lastGameDrawn?.Latest ?? lastDrawn?.Latest;

			/// <summary>Whether the last frame's steadying ran as the compute kernel.</summary>
			public bool LastByCompute { get; private set; }

			public void Setup(Material cloudMaterial, ComputeShader resolve)
			{
				material = cloudMaterial;
				if (resolve != kernelOf)
				{
					kernelOf = resolve;
					// A kernel that failed to build still loads, without its kernel: the pass then does
					// the work rather than this throwing every frame.
					resolveCompute = resolve != null && resolve.HasKernel("Resolve") ? resolve : null;
					resolveKernel = resolveCompute != null ? resolveCompute.FindKernel("Resolve") : -1;
				}
			}

			/// <summary>Whether this pass can run the steadying as its kernel on this machine.</summary>
			private bool KernelReady => resolveKernel >= 0 && ComputeResolveSupported;

			/// <summary>
			/// The format of the march's second target, each texel's motion (<c>_FishCloudCurrentMotion</c>:
			/// its mean cloud distance in km and its bands' mean wind gain): two half floats where the
			/// platform can draw into them, else two floats, else four half floats.
			/// </summary>
			public static GraphicsFormat MotionFormat()
			{
				GraphicsFormat[] candidates = { GraphicsFormat.R16G16_SFloat, GraphicsFormat.R32G32_SFloat };
				foreach (GraphicsFormat format in candidates)
				{
					if (SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
					{
						return format;
					}
				}
				return GraphicsFormat.R16G16B16A16_SFloat;
			}

			/// <summary>
			/// The format of a history's weights: a single half-float channel where the platform can
			/// draw into one (and, for the kernel, write one), else a single float, else the clouds' own
			/// format at four times the size. <see cref="GraphicsFormat.None"/> when the kernel can write
			/// none of them.
			/// </summary>
			/// <remarks>
			/// Half floats and not bytes: a byte could not hold the small steps a pixel's weight grows
			/// by between samples, and would round them away.
			/// </remarks>
			public static GraphicsFormat WeightFormat(bool randomWrite)
			{
				GraphicsFormat[] candidates = { GraphicsFormat.R16_SFloat, GraphicsFormat.R32_SFloat, GraphicsFormat.R16G16B16A16_SFloat };
				foreach (GraphicsFormat format in candidates)
				{
					if (SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)
						&& (!randomWrite || SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore)))
					{
						return format;
					}
				}
				return randomWrite ? GraphicsFormat.None : GraphicsFormat.R16G16B16A16_SFloat;
			}

			public void Dispose()
			{
				foreach (CloudHistory history in histories.Values)
				{
					history.Release();
				}
				histories.Clear();
				lastDrawn = null;
				lastGameDrawn = null;
			}

			private class MarchData
			{
				public Material Material;
				public int Pass;
				public TextureHandle Source;
				public TextureHandle Motion;
				public TextureHandle History;
				public TextureHandle HistoryWeight;
				public TextureHandle Clouds;
			}

			/// <summary>What the steadying kernel is handed: a kernel sees no material, and no camera globals.</summary>
			private class ResolveData
			{
				public ComputeShader Compute;
				public int Kernel;
				public TextureHandle Current, CurrentMotion, History, HistoryWeight, Depth, Clouds, Weight;
				public Matrix4x4 InverseViewProjection, PreviousViewProjection;
				public Vector4 Temporal, Motion, Jitter, Upsample, Shell, Camera, Forward, DepthSize, ZBuffer, Options;
				public int GroupsX, GroupsY;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (material == null || !SkySystem.CloudsReady)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}
				SkySystem sky = SkySystem.Instance;
				CloudTierSettings tier = sky.CloudTier;
				// The profile's diagnostic switches for the reconstruction, live (the tier's Temporal has
				// already been overridden by them in SkySystem); all at their defaults, as shipped.
				VolumetricCloudDiagnostics diagnostics = sky.CloudDiagnostics ?? new VolumetricCloudDiagnostics();

				int screenWidth = cameraData.cameraTargetDescriptor.width;
				int screenHeight = cameraData.cameraTargetDescriptor.height;
				// The Scene view is for placing things, not judging the sky: a quarter of the rays.
				bool sceneView = cameraData.cameraType == CameraType.SceneView;
				float marchShare = sceneView ? tier.Resolution * SceneViewShare : tier.Resolution;
				float historyShare = sceneView
					? CloudTierSettings.HistoryScaleFor(tier.HistoryScale > 0f ? tier.HistoryScale * SceneViewShare : 0f, marchShare)
					: tier.EffectiveHistoryScale;
				int width = Mathf.Max(16, Mathf.RoundToInt(screenWidth * marchShare));
				int height = Mathf.Max(16, Mathf.RoundToInt(screenHeight * marchShare));
				// The rebuilt clouds are the tier's share of the screen (CloudTierSettings.HistoryScaleFor)
				// unless the screen is past the cap.
				float shrink = Mathf.Min(historyShare, Mathf.Sqrt(MaxHistoryPixels / Mathf.Max(1f, (float)screenWidth * screenHeight)));
				int outputWidth = Mathf.Max(16, Mathf.RoundToInt(screenWidth * shrink));
				int outputHeight = Mathf.Max(16, Mathf.RoundToInt(screenHeight * shrink));
				bool screenSized = outputWidth == screenWidth && outputHeight == screenHeight;
				// Never more than four rebuilt pixels to a marched texel across (SubPixelSide): the
				// sixteen places are a texel's four-by-four cells, and a texel spread wider than four
				// pixels would leave some pixels no place of their own, looked through never. Rounding is
				// all this ever moves on a tier that leaves the rebuild to the renderer (a texel a side at
				// most: 1306 pixels over 326 texels becomes over 327).
				width = MarchTexelsFor(outputWidth, width);
				height = MarchTexelsFor(outputHeight, height);
				if (cameraData.cameraType == CameraType.Game)
				{
					PixelsPerMarchedTexel = Mathf.Max(1f, (float)outputHeight / height);
				}

				// The kernel where it can run: it needs the depth to hand over, and its groupshared
				// block is sized for a rebuilt buffer no coarser than the march — past the cap on a very
				// large screen (above about 5K on High) the march outgrows it, and the pass takes over.
				bool randomWrite = KernelReady;
				bool byCompute = randomWrite && resourceData.cameraDepthTexture.IsValid()
					&& width <= outputWidth && height <= outputHeight;

				float now = Time.realtimeSinceStartup;
				Forget(now);
				CloudHistory state = HistoryFor(cameraData.camera, outputWidth, outputHeight, randomWrite);
				state.LastDrawn = now;
				byCompute &= state.RandomWrite;

				Matrix4x4 view = cameraData.GetViewMatrix();
				// The camera's own projection, never URP's jittered one (GetProjectionMatrix carries the
				// pipeline's temporal antialiasing offset when the player picks TAA): the clouds have their own
				// sub-texel places (SubPixel) and history, and carried from one jittered matrix to the next their
				// history would be fetched off by the pipeline's offset every frame, shaking the sky.
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
				Matrix4x4 viewProjection = projection * view;
				Vector3 position = cameraData.worldSpaceCameraPos;
				// How far the air carried the low clouds since this camera last drew: the drift is
				// worked out from the world clock and wrapped to fit a float, so the step is taken
				// across the wrap the short way round.
				Vector2 drift = sky.CloudDrift;
				Vector2 driftStep = drift - state.PreviousDrift;
				float wrap = (float)WeatherDriver.DriftWrapMetres;
				driftStep.x -= Mathf.Round(driftStep.x / wrap) * wrap;
				driftStep.y -= Mathf.Round(driftStep.y / wrap) * wrap;
				bool cut = state.Valid
					&& ((position - state.PreviousPosition).sqrMagnitude > CutDistance * CutDistance
						|| driftStep.sqrMagnitude > MaxDriftStep * MaxDriftStep);
				bool temporal = tier.Temporal && state.Valid && !cut;
				if (!temporal)
				{
					driftStep = Vector2.zero;
				}

				material.SetMatrix(InverseVPId, viewProjection.inverse);
				material.SetMatrix(PreviousVPId, state.PreviousViewProjection);
				// The frame, wrapped. It feeds the jitter as a float multiplied by 5.6 and added to a pixel
				// coordinate: left to count for ever it passes a million inside an hour, where a float no
				// longer tells one pixel from the next, and the jitter every ray's phase depends on goes
				// coarse the longer the game has been running. Sixty-four frames is the noise's own period.
				material.SetVector(MarchParamsId, new Vector4(tier.Steps, tier.Detail, state.Frame % 64, sky.CloudFarDistance));
				// How wide one ray's cone opens, in metres per metre of distance. The projection's
				// [1][1] is 1/tan(halfFov), so 2/(m11 * rows) is the height of one of `rows` pixels a
				// metre in front of the camera. It has to be worked out here and nowhere else: this is
				// the only place that knows how the screen is divided.
				//
				// The REBUILT pixel's cone, not the marched texel's. Each ray is cast through one
				// place — the middle of one rebuilt pixel (SubPixel) — and is a point sample of the sky
				// there; the steadying averages sixteen such places a texel, which is supersampling, and
				// its tent is the only low-pass the picture should get. So what a ray stands for is that
				// pixel's cone, exactly as a ray of a full-resolution march does. Taken at the marched texel (as it was, 2026-09-28) the
				// cone was four times too wide on Balanced, and the march drew a different cloud than
				// the full-resolution march of the same sky: the eddies' finer octaves dropped
				// (FishCloudDetailResolved), every edge's ramp four times as wide (FishCloudEdgeRampWidth),
				// a coarser mip. MEASURED on the probe (clouds-from-above, Balanced, 1600 × 900): the
				// deck read 0.729 against the full-resolution march's 0.663 (sRGB) over a flat patch, and
				// the image's "any cloud" cover 82 % against 56 % — in the upscale with the steadying off
				// as much as with it on, so it was the march and not the steadying that drew them.
				float m11 = Mathf.Abs(cameraData.GetProjectionMatrix().m11);
				float spread = m11 > 1e-4f ? 2f / (m11 * outputHeight) : 0f;
				Shader.SetGlobalVector(LodParamsId, new Vector4(spread, 4f, 1f, sky.CloudFarDistance));

				// Where inside its texel each ray looks this frame. Only while the frames are being
				// accumulated: with the steadying off a moving grid would only make the sky swim.
				Vector2 offset = tier.Temporal ? SubPixel(state.Frame) : Vector2.zero;
				var jitter = new Vector4(offset.x, offset.y, width, height);
				material.SetVector(JitterId, jitter);
				// How many frames a settled pixel averages over (SettledFrames, or longer if the tier's
				// blend asks). What keeps a moving cloud from trailing through them is the reprojection,
				// which follows the camera and each texel's own band drift (the low clouds' step below,
				// times the band's wind gain the march hands back), and past it the variance clip, which
				// reins in whatever moved some other way.
				float blend = Mathf.Clamp(tier.TemporalBlend, 0f, 0.98f);
				// The history's length is the diagnostics' History Frames (SettledFrames, 16, as shipped); z the
				// variance clip's width as its difference from FISH_RESOLVE_GAMMA (0 as shipped), w 1 with the
				// clip off (FishCloudResolve.hlsl).
				float settledFrames = Mathf.Clamp(diagnostics.HistoryFrames, 1, 64);
				var temporalParams = new Vector4(Mathf.Max(settledFrames, blend / (1f - blend)), temporal ? 1f : 0f,
					Mathf.Clamp(diagnostics.ClipGamma, 0.25f, 4f) - VolumetricCloudDiagnostics.DefaultClipGamma, diagnostics.HistoryClip ? 0f : 1f);
				var motion = new Vector4(driftStep.x, driftStep.y, 0f, 0f);
				material.SetVector(TemporalId, temporalParams);
				material.SetVector(MotionId, motion);
				// With the steadying off, w is 0 and the rebuild is only the tent upscale of this frame
				// (FishCloudResolve): there is no history to average, only an upscale to do well. z is how big
				// a change a hard clip must be before a pixel's history is let go of (the diagnostics' History
				// Reset Change, 0.1 as shipped).
				var upsample = new Vector4(outputWidth, outputHeight, Mathf.Clamp(diagnostics.HistoryResetChange, 0f, 0.5f), tier.Temporal ? 1f : 0f);
				material.SetVector(UpsampleId, upsample);
				material.SetVector(CompositeId, new Vector4(1f / outputWidth, 1f / outputHeight, screenSized ? 1f : 0f, Mathf.Max(0f, diagnostics.BlurPixels)));
				Vector4 shell = ResolveShell(position);
				material.SetVector(ResolveShellId, shell);
				// The options under trial: the rays' phase for the march, A's kernel for the steadying.
				Vector4 options = Options.ShaderVector(state.Frame);
				// The diagnostics' Decorrelated Ray Phase off: the old order, neighbouring places nearly in phase.
				if (!diagnostics.DecorrelatedRayPhase)
				{
					options.w = CloudOptions.JitterPhaseLegacy(state.Frame);
				}
				// The diagnostics' B-spline kernel is option A's, asked for from the inspector.
				if (diagnostics.Kernel == CloudReconstructionKernel.BSpline)
				{
					options.x = 1f;
				}
				material.SetVector(OptionsId, options);
				material.SetFloat(FramePhaseId, Mathf.Repeat(state.Frame * 0.6180340f, 1f));

				var marchDesc = new TextureDesc(width, height)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = "FishCloudsMarch",
					clearBuffer = false,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
				};
				TextureHandle marched = renderGraph.CreateTexture(marchDesc);
				var motionDesc = new TextureDesc(width, height)
				{
					colorFormat = MotionFormat(),
					name = "FishCloudsMarchMotion",
					clearBuffer = false,
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
				};
				TextureHandle marchedMotion = renderGraph.CreateTexture(motionDesc);
				TextureHandle historyRead = renderGraph.ImportTexture(state.Clouds[state.Index]);
				TextureHandle historyWrite = renderGraph.ImportTexture(state.Clouds[1 - state.Index]);
				TextureHandle weightRead = renderGraph.ImportTexture(state.Weight[state.Index]);
				TextureHandle weightWrite = renderGraph.ImportTexture(state.Weight[1 - state.Index]);

				// 1. March the volume at the tier's resolution: the clouds, and each texel's motion.
				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(MarchName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 0;
					builder.SetRenderAttachment(marched, 0);
					builder.SetRenderAttachment(marchedMotion, 1);
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass));
				}

				// 2. Rebuild it at the buffer's resolution with the frames before it: this frame's tent
				// estimate, blended into the history carried with each cloud's own drift and clipped to
				// this frame's neighbourhood. Runs with the steadying off too — then it is only the
				// upscale — so the composite and everything reading the published buffer see one
				// resolution whatever the tier.
				// The kernel where it runs, the shader's pass 1 everywhere else: the same arithmetic.
				if (byCompute)
				{
					using (var builder = renderGraph.AddComputePass<ResolveData>(TemporalComputeName, out ResolveData data))
					{
						data.Compute = resolveCompute;
						data.Kernel = resolveKernel;
						data.Current = marched;
						data.CurrentMotion = marchedMotion;
						data.History = historyRead;
						data.HistoryWeight = weightRead;
						data.Depth = resourceData.cameraDepthTexture;
						data.Clouds = historyWrite;
						data.Weight = weightWrite;
						data.InverseViewProjection = viewProjection.inverse;
						data.PreviousViewProjection = state.PreviousViewProjection;
						data.Temporal = temporalParams;
						data.Motion = motion;
						data.Jitter = jitter;
						data.Upsample = upsample;
						data.Shell = shell;
						data.Options = options;
						data.Camera = new Vector4(position.x, position.y, position.z, 0f);
						// The fragment pass's -UNITY_MATRIX_V[2]: the view matrix's third row is the
						// camera's back.
						Vector4 back = view.GetRow(2);
						data.Forward = new Vector4(-back.x, -back.y, -back.z, 0f);
						data.DepthSize = new Vector4(screenWidth, screenHeight, 0f, 0f);
						data.ZBuffer = ZBufferParams(cameraData.camera);
						data.GroupsX = (outputWidth + ResolveGroup - 1) / ResolveGroup;
						data.GroupsY = (outputHeight + ResolveGroup - 1) / ResolveGroup;
						builder.UseTexture(marched, AccessFlags.Read);
						builder.UseTexture(marchedMotion, AccessFlags.Read);
						builder.UseTexture(historyRead, AccessFlags.Read);
						builder.UseTexture(weightRead, AccessFlags.Read);
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
						builder.UseTexture(historyWrite, AccessFlags.Write);
						builder.UseTexture(weightWrite, AccessFlags.Write);
						builder.AllowPassCulling(false);
						builder.SetRenderFunc((ResolveData d, ComputeGraphContext context) =>
						{
							ComputeCommandBuffer cmd = context.cmd;
							cmd.SetComputeMatrixParam(d.Compute, InverseVPId, d.InverseViewProjection);
							cmd.SetComputeMatrixParam(d.Compute, PreviousVPId, d.PreviousViewProjection);
							cmd.SetComputeVectorParam(d.Compute, TemporalId, d.Temporal);
							cmd.SetComputeVectorParam(d.Compute, MotionId, d.Motion);
							cmd.SetComputeVectorParam(d.Compute, JitterId, d.Jitter);
							cmd.SetComputeVectorParam(d.Compute, UpsampleId, d.Upsample);
							cmd.SetComputeVectorParam(d.Compute, ResolveShellId, d.Shell);
							cmd.SetComputeVectorParam(d.Compute, OptionsId, d.Options);
							cmd.SetComputeVectorParam(d.Compute, ResolveCameraId, d.Camera);
							cmd.SetComputeVectorParam(d.Compute, ResolveForwardId, d.Forward);
							cmd.SetComputeVectorParam(d.Compute, ResolveDepthSizeId, d.DepthSize);
							cmd.SetComputeVectorParam(d.Compute, ResolveZId, d.ZBuffer);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, CurrentId, d.Current);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, CurrentMotionId, d.CurrentMotion);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, HistoryId, d.History);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, HistoryWeightId, d.HistoryWeight);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, ResolveDepthId, d.Depth);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, ResolvedId, d.Clouds);
							cmd.SetComputeTextureParam(d.Compute, d.Kernel, ResolvedWeightId, d.Weight);
							cmd.DispatchCompute(d.Compute, d.Kernel, d.GroupsX, d.GroupsY, 1);
						});
					}
				}
				else
				{
					using (var builder = renderGraph.AddRasterRenderPass<MarchData>(TemporalName, out MarchData data))
					{
						data.Material = material;
						data.Pass = 1;
						data.Source = marched;
						data.Motion = marchedMotion;
						data.History = historyRead;
						data.HistoryWeight = weightRead;
						builder.SetRenderAttachment(historyWrite, 0);
						builder.SetRenderAttachment(weightWrite, 1);
						builder.UseTexture(marched, AccessFlags.Read);
						builder.UseTexture(marchedMotion, AccessFlags.Read);
						builder.UseTexture(historyRead, AccessFlags.Read);
						builder.UseTexture(weightRead, AccessFlags.Read);
						// Depth, to keep each pixel to the samples that looked at what it looks at and to
						// carry the world's pixels by their own distance. Asked for, like every pass that
						// reads it: undeclared, it reads as the near plane.
						builder.UseAllGlobalTextures(true);
						if (resourceData.cameraDepthTexture.IsValid())
						{
							builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
						}
						builder.AllowPassCulling(false);
						builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
						{
							d.Material.SetTexture(CurrentId, d.Source);
							d.Material.SetTexture(CurrentMotionId, d.Motion);
							d.Material.SetTexture(HistoryId, d.History);
							d.Material.SetTexture(HistoryWeightId, d.HistoryWeight);
							Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
						});
					}
				}
				LastByCompute = byCompute;
				TextureHandle steadied = historyWrite;

				// 3. Composite: scattered light added, what is behind kept by the transmittance.
				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(CompositeName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 2;
					data.Source = steadied;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(steadied, AccessFlags.Read);
					// Only a buffer smaller than the screen (a tier that rebuilds at less than the
					// screen, or a screen past the cap) needs the depth here, to know which of its four
					// taps belong to this pixel — but it has to be asked for all the same. A pass that samples a texture it never declared gets whatever happens to be
					// bound — in reversed Z an undeclared depth reads as the near plane, which is not a
					// wrong answer so much as a confident one.
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
					{
						d.Material.SetTexture(BufferId, d.Source);
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
					});
				}

				// 4. God rays: shafts from the sun, or from around a body eclipsing it. The clouds
				// are already in the frame, so they cast the shafts they should. Gathered at half the
				// march's size as before: the rebuilt clouds are sharper than a shaft needs.
				/* Not from under the water (Jim, 2026-10-06: the sky's glow showed on things under the sea): the shafts rake
				 * out from the sun across the frame, and under the surface the water's own pass (FishMMO/Water/Underwater)
				 * draws the sun's shafts as the water bends and scatters them. */
				if (sky.GodRayIntensity > 0.001f && SkySystem.DrawGodRays && !SurfaceWater.IsUnder(cameraData.camera.transform.position))
				{
					DrawGodRays(renderGraph, resourceData, sky, steadied, viewProjection, width / 2, height / 2);
				}

				// The sky bodies are drawn in the transparent queue, which runs after this composite,
				// so a moon or a planet is otherwise painted over the cloud that should be in front
				// of it. They read this buffer's transmittance to put themselves back behind it — and
				// so does the water, which is transparent too and otherwise cut the clouds off from
				// above (FishWaterFog.hlsl).
				//
				// Published here, on the CPU, and not with SetGlobalTexture inside the composite
				// pass: binding a graph-managed target as a global from within a raster pass upsets
				// RenderGraph's tracking of it and the whole frame comes out empty. The steadied
				// result lives in an imported RTHandle, which is a plain texture the rest of the
				// frame can read, so hand that over directly. Published with the steadying off as
				// well now: the rebuild writes the same buffer either way.
				RTHandle written = state.Clouds[1 - state.Index];
				bool published = written != null && written.rt != null;
				if (published)
				{
					Shader.SetGlobalTexture(BufferId, written.rt);
					/* Its texel size too, for the water, which lays these clouds back over itself with the
					 * composite's own depth-weighted taps (FishWaterFog.hlsl): a global texture's
					 * _TexelSize is not something to rely on being filled. The rebuilt buffer's, not the
					 * march's: at the screen's size those taps land half a pixel either side, which is
					 * one pixel read four times. */
					Shader.SetGlobalVector(BufferTexelId, new Vector4(1f / outputWidth, 1f / outputHeight, outputWidth, outputHeight));
				}
				// SkySystem clears this each frame before anything renders; raising it here is what
				// says the buffer above is real. Left at zero, the bodies do not attenuate at all,
				// which is the right way to fail: an unbound buffer samples as zero and would
				// otherwise read as "fully occluded" and empty the sky.
				Shader.SetGlobalVector(ScreenId, new Vector4(published ? 1f : 0f, 0f, 0f, 0f));

				state.PreviousViewProjection = viewProjection;
				state.PreviousPosition = position;
				state.PreviousDrift = drift;
				state.Valid = tier.Temporal;
				state.Index = 1 - state.Index;
				// Wrapped at a whole number of every cycle that reads it — the jitter's 64, the places'
				// 16, and the 64 x 16 over which SubPixel turns their order — so wrapping skips nothing.
				state.Frame = (state.Frame + 1) % (64 * SubPixelCycle);
				lastDrawn = state;
				if (cameraData.cameraType == CameraType.Game)
				{
					lastGameDrawn = state;
				}
			}

			/// <summary>
			/// Where inside its texel the march looks on a frame, in texels either side of the middle:
			/// <see cref="SubPixelPlace"/> of the frame's place.
			/// </summary>
			private static Vector2 SubPixel(int frame) => SubPixelPlace(SubPixelSlot(frame));

			/// <summary>
			/// A camera's history, made the right size. A new one, or one whose size has changed, is
			/// not valid until it has been drawn once: the first frame after it is built from this
			/// frame alone.
			/// </summary>
			private CloudHistory HistoryFor(Camera camera, int width, int height, bool randomWrite)
			{
				if (!histories.TryGetValue(camera, out CloudHistory state))
				{
					state = new CloudHistory();
					histories.Add(camera, state);
				}
				if (!state.Fits(width, height, randomWrite))
				{
					state.Allocate(width, height, randomWrite);
				}
				return state;
			}

			/// <summary>
			/// The shell floor the steadying may take as "no cloud below here", for this camera:
			/// x the floor (m), y the world's radius (m), z 1 when the camera stands below the floor and
			/// the skip applies at all (<c>FishResolveBelowClouds</c>).
			/// </summary>
			/// <remarks>
			/// The same figures the march runs from — SkySystem's <c>_FishCloudLayer</c>, already widened
			/// down to the lowest storm floor and to the ground whenever there is fog or rain haze to draw
			/// — so the skip can never disagree with what the march would have found. The camera's height
			/// is worked out here in double, over the same curved world.
			/// </remarks>
			private static Vector4 ResolveShell(Vector3 camera)
			{
				Vector4 layer = Shader.GetGlobalVector(CloudLayerId);
				bool drawn = layer.z > 0f && layer.y > layer.x;
				double radius = System.Math.Max(1000.0, layer.z);
				bool below = drawn && AltitudeOver(camera, radius) < layer.x - 1.0;
				return new Vector4(layer.x, (float)radius, below ? 1f : 0f, 0f);
			}

			/// <summary>
			/// This camera's <c>_ZBufferParams</c>, for the kernel, which is handed no camera globals: URP's
			/// own (ScriptableRenderer.SetPerCameraShaderVariables), so the kernel linearises depth to the
			/// same numbers the pass does.
			/// </summary>
			private static Vector4 ZBufferParams(Camera camera)
			{
				float near = camera.nearClipPlane;
				float far = camera.farClipPlane;
				float invNear = Mathf.Approximately(near, 0f) ? 0f : 1f / near;
				float invFar = Mathf.Approximately(far, 0f) ? 0f : 1f / far;
				float zc0 = 1f - far * invNear;
				float zc1 = far * invNear;
				var zBuffer = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);
				if (SystemInfo.usesReversedZBuffer)
				{
					zBuffer.y += zBuffer.x;
					zBuffer.x = -zBuffer.x;
					zBuffer.w += zBuffer.z;
					zBuffer.z = -zBuffer.z;
				}
				return zBuffer;
			}

			/// <summary>Lets go of the buffers of cameras that are gone, or have not drawn for a while.</summary>
			private void Forget(float now)
			{
				foreach (KeyValuePair<Camera, CloudHistory> entry in histories)
				{
					if (entry.Key == null || now - entry.Value.LastDrawn > ForgetAfterSeconds)
					{
						forgotten.Add(entry.Key);
					}
				}
				if (forgotten.Count == 0)
				{
					return;
				}
				foreach (Camera camera in forgotten)
				{
					CloudHistory state = histories[camera];
					state.Release();
					histories.Remove(camera);
					if (lastDrawn == state)
					{
						lastDrawn = null;
					}
					if (lastGameDrawn == state)
					{
						lastGameDrawn = null;
					}
				}
				forgotten.Clear();
			}

			/// <summary>
			/// Gathers the light still reaching the camera from around the sun into shafts, and adds
			/// them to the frame.
			/// </summary>
			/// <remarks>
			/// A shaft is air lit by the sun beside air that is not. The gather finds, for each pixel,
			/// how much of the way to the light is open — cloud blocks by how little it lets through,
			/// the world only from far enough off to be a ridge — and the composite both lights the
			/// open air and darkens the shadowed lane beside it. How much there is to light is the
			/// medium: haze, mist and rain, with a small floor. During an eclipse the light comes from
			/// the ring around the body covering the sun, which is where the rays should rake out from.
			/// </remarks>
			private void DrawGodRays(RenderGraph renderGraph, UniversalResourceData resourceData, SkySystem sky, TextureHandle clouds, Matrix4x4 viewProjection, int width, int height)
			{
				material.SetMatrix(RayVPId, viewProjection);
				Vector3 direction = sky.GodRayDirection;
				material.SetVector(RayDirId, new Vector4(direction.x, direction.y, direction.z, 0f));
				// An eclipse's rays belong around the body, not across the whole sky, so they reach a
				// shorter way — what they rake around is the silhouette itself.
				float reach = sky.GodRayEclipse > 0.02f ? 0.35f : 0.9f;
				float medium = sky.GodRayMedium;
				// Falloff per screen height, how near the world has to be to be left out of it, reach, taps.
				// Near things are left out on purpose: only cloud and distant terrain cast a shaft.
				material.SetVector(RayParamsId, new Vector4(1.6f, 400f, reach, 32f));
				material.SetVector(RayMaskId, new Vector4(0.25f, 0.2f, sky.GodRayEclipse, 0f));
				// The thicker the air, the less of it a shaft needs in front of a hillside to show.
				// The clouds are translucent: below 0.2 of the light they block, above 0.85 they pass.
				material.SetVector(RayAirId, new Vector4(medium, Mathf.Lerp(8000f, 1200f, medium), 0.2f, 0.85f));
				// The lanes answer to the same strength as the light: no shafts, no shadows of them.
				material.SetVector(RayLaneId, new Vector4(0.9f * Mathf.Clamp01(sky.GodRayIntensity), 0f, 0f, 0f));
				Color colour = sky.GodRayColor;
				colour.a = sky.GodRayIntensity * 3f;
				material.SetColor(RayColorId, colour);

				var rayDesc = new TextureDesc(Mathf.Max(8, width), Mathf.Max(8, height))
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = "FishGodRays",
					clearBuffer = false,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
				};
				TextureHandle rays = renderGraph.CreateTexture(rayDesc);

				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(RayName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 4;
					data.Source = resourceData.activeColorTexture;
					data.Clouds = clouds;
					builder.SetRenderAttachment(rays, 0);
					builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);
					builder.UseTexture(clouds, AccessFlags.Read);
					// The depth texture has to be asked for here too: by this point in the frame its last
					// reader was the march, and without this it is gone and every pixel reads as solid.
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.UseAllGlobalTextures(true);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
					{
						d.Material.SetTexture(RaySourceId, d.Source);
						d.Material.SetTexture(RayCloudsId, d.Clouds);
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
					});
				}

				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(RayCompositeName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 5;
					data.Source = rays;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(rays, AccessFlags.Read);
					// This pass reads the depth too — how much air stands in front of each pixel, at the
					// screen's own resolution — and an undeclared depth reads as the near plane, which
					// would make every pixel "a wall at arm's length" and the shafts vanish without a word.
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
					{
						d.Material.SetTexture(RayBufferId, d.Source);
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
					});
				}
			}

			/// <summary>
			/// One camera's clouds as the frames have built them up: two buffers of clouds and two of
			/// weights, read from one of each pair and written to the other, turn about.
			/// </summary>
			private sealed class CloudHistory
			{
				/// <summary>rgb scattered light, a transmittance, at the rebuilt size.</summary>
				public readonly RTHandle[] Clouds = new RTHandle[2];

				/// <summary>
				/// What each pixel of those is worth: the weight of the samples behind it, capped where
				/// the history settles. Half floats — a byte could not hold the small steps a pixel's
				/// weight grows by between samples, and would round them away.
				/// </summary>
				public readonly RTHandle[] Weight = new RTHandle[2];

				/// <summary>Which of each pair holds the latest.</summary>
				public int Index;
				public bool Valid;
				public Matrix4x4 PreviousViewProjection = Matrix4x4.identity;
				public Vector3 PreviousPosition;
				public Vector2 PreviousDrift;
				public int Frame;
				public float LastDrawn;
				/// <summary>Whether the buffers were made for the kernel to write.</summary>
				public bool RandomWrite;

				public Texture Latest => Clouds[Index] != null ? Clouds[Index].rt : null;

				public bool Fits(int width, int height, bool randomWrite)
				{
					if (randomWrite != RandomWrite)
					{
						return false;
					}
					for (int i = 0; i < 2; i++)
					{
						if (Clouds[i] == null || Clouds[i].rt == null || Weight[i] == null || Weight[i].rt == null
							|| Clouds[i].rt.width != width || Clouds[i].rt.height != height)
						{
							return false;
						}
					}
					return true;
				}

				/// <param name="randomWrite">
				/// Made so the kernel can write them as well as the pass draw into them: decided by what
				/// the machine can do, not by which of the two runs this frame, so a frame that falls back
				/// to the pass keeps its history.
				/// </param>
				public void Allocate(int width, int height, bool randomWrite)
				{
					Release();
					GraphicsFormat weightFormat = WeightFormat(randomWrite);
					if (weightFormat == GraphicsFormat.None)
					{
						randomWrite = false;
						weightFormat = WeightFormat(false);
					}
					for (int i = 0; i < 2; i++)
					{
						Clouds[i] = RTHandles.Alloc(width, height, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
							filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: randomWrite, name: $"FishCloudsHistory{i}");
						Weight[i] = RTHandles.Alloc(width, height, colorFormat: weightFormat,
							filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: randomWrite, name: $"FishCloudsHistoryWeight{i}");
					}
					RandomWrite = randomWrite;
					Index = 0;
					Valid = false;
				}

				public void Release()
				{
					for (int i = 0; i < 2; i++)
					{
						Clouds[i]?.Release();
						Clouds[i] = null;
						Weight[i]?.Release();
						Weight[i] = null;
					}
					Valid = false;
				}
			}
		}
	}

	/// <summary>
	/// The cloud reconstruction options under trial (issue #238), each independent, each off by default,
	/// and off is the rendering exactly as it was. Jim: "Towers from 12 km and Above the deck look very
	/// spotty … can we add some blending/smoothing without major costs?" — rendered one at a time and
	/// together so the most realistic can be chosen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two artefacts were seen in full-resolution crops of the probe stills (Balanced, a marched texel
	/// four pixels across): flat rectangular patches a texel or a few wide, and speckle inside the
	/// clouds, each pixel its own ray's answer.
	/// </para>
	/// <list type="bullet">
	/// <item><b>A, smooth kernel</b> — the steadying's current estimate reads the nine texels round a
	/// pixel through the quadratic B-spline (never negative, continuous where the nearest texel changes)
	/// in place of the tent over the nearest four: smoother still, a little wider. Since the temporal
	/// upsampler of 2026-09-28 (FishCloudResolve.hlsl) this is the only thing A changes.</item>
	/// <item><b>B, confidence blur</b> — belonged to the steadying the temporal upsampler replaced (a halo
	/// lent to a pixel with few samples of its own); the steadying no longer reads it. Kept only so
	/// the probes' lists still parse: delete it (and <see cref="BlurSigma2"/>) once no probe asks for it.</item>
	/// <item><b>C, footprint-limited detail</b> — the eddies' octaves and the edge's ramp are drawn no
	/// finer than <see cref="FootprintTexels"/> marched texels (2 by default: the finest a single frame's
	/// grid can carry, Nyquist), where the share has been half a texel.</item>
	/// <item><b>D, low-discrepancy jitter</b> — the rays' phase keeps its spatial pattern and moves on
	/// in time stratified over a cycle and by the golden ratio between cycles (<see cref="JitterPhase"/>).
	/// Always on since 2026-09-28; the letter is only printed. Delete it with B.</item>
	/// </list>
	/// <para>
	/// E (more rays: <c>FISHMMO_WEATHER_VARIANT=res=…</c>) and F (a longer probe settle:
	/// <c>FISHMMO_CAPTURE_WARM_FRAMES</c>) are the probes' own, not options here.
	/// </para>
	/// </remarks>
	public struct CloudOptions
	{
		/// <summary>A: the smooth fill.</summary>
		public bool SmoothFill;
		/// <summary>B: the confidence blur.</summary>
		public bool ConfidenceBlur;
		/// <summary>C: the finest the eddies and the edge are drawn, in marched texels; 0 is off.</summary>
		public float FootprintTexels;
		/// <summary>D: the rays' low-discrepancy phase.</summary>
		public bool LowDiscrepancyJitter;

		/// <summary>
		/// Option C's floor when it is asked for without a number: two marched texels. One frame's march
		/// is a grid of samples a texel apart, and a pattern finer than twice that spacing cannot be told
		/// from a coarser one by it (Nyquist): drawn anyway it comes back as a different answer from each
		/// frame's grid — speckle — and the three-by-three of samples the steadying holds its history to
		/// no longer brackets what the pixel is. An octave is drawn in full while its cell is twice the
		/// floor and gone by the floor (FishCloudDetailResolved), so at 2 nothing finer than two texels
		/// is drawn and everything from four is.
		/// </summary>
		public const float DefaultFootprintTexels = 2f;

		/// <summary>The most option C may be set to: past eight texels it would erase the clouds' eddies at any distance.</summary>
		public const float MaxFootprintTexels = 8f;

		/// <summary>(√5 − 1) / 2: the step that leaves a sequence of phases most evenly spread however many are taken (Roberts 2018).</summary>
		public const double GoldenRatioConjugate = 0.6180339887498949;

		/// <summary>Whether any of the options is on.</summary>
		public bool Any => SmoothFill || ConfidenceBlur || FootprintTexels > 0f || LowDiscrepancyJitter;

		/// <summary>
		/// The options named in <paramref name="list"/>: letters A–D separated by commas, semicolons or
		/// spaces, any case; C may carry its floor as <c>C=1.5</c> (a bare C is
		/// <see cref="DefaultFootprintTexels"/>, <c>C=0</c> is off). Anything else is ignored and handed
		/// back in <paramref name="ignored"/>, so the probe can say it did not understand it rather than
		/// render something other than what was asked. Null or empty is every option off.
		/// </summary>
		public static CloudOptions Parse(string list, out string ignored)
		{
			var options = new CloudOptions();
			ignored = string.Empty;
			if (string.IsNullOrWhiteSpace(list))
			{
				return options;
			}
			foreach (string raw in list.Split(new[] { ',', ';', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
			{
				string token = raw.Trim();
				string name = token;
				string value = null;
				int equals = token.IndexOf('=');
				if (equals >= 0)
				{
					name = token.Substring(0, equals).Trim();
					value = token.Substring(equals + 1).Trim();
				}
				bool understood = true;
				switch (name.ToUpperInvariant())
				{
					case "A":
						options.SmoothFill = value == null;
						understood = value == null;
						break;
					case "B":
						options.ConfidenceBlur = value == null;
						understood = value == null;
						break;
					case "C":
						if (value == null)
						{
							options.FootprintTexels = DefaultFootprintTexels;
						}
						else if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float texels)
							&& !float.IsNaN(texels) && !float.IsInfinity(texels))
						{
							options.FootprintTexels = Mathf.Clamp(texels, 0f, MaxFootprintTexels);
						}
						else
						{
							understood = false;
						}
						break;
					case "D":
						options.LowDiscrepancyJitter = value == null;
						understood = value == null;
						break;
					default:
						understood = false;
						break;
				}
				if (!understood)
				{
					ignored = ignored.Length == 0 ? token : ignored + "," + token;
				}
			}
			return options;
		}

		/// <summary><see cref="Parse(string, out string)"/>, dropping what it did not understand.</summary>
		public static CloudOptions Parse(string list) => Parse(list, out _);

		/// <summary>The options as the probes name them, e.g. <c>A,C=2,D</c>; <c>none</c> when all are off.</summary>
		public override string ToString()
		{
			var parts = new List<string>(4);
			if (SmoothFill)
			{
				parts.Add("A");
			}
			if (ConfidenceBlur)
			{
				parts.Add("B");
			}
			if (FootprintTexels > 0f)
			{
				parts.Add("C=" + FootprintTexels.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
			}
			if (LowDiscrepancyJitter)
			{
				parts.Add("D");
			}
			return parts.Count == 0 ? "none" : string.Join(",", parts);
		}

		/// <summary>
		/// How much of a ray's cone the finest detail is drawn down to (SkySystem's
		/// <c>_FishCloudShapeParams.x</c>): <paramref name="share"/> as it is, or with option C on, never
		/// under its floor. The one number reaches both things C limits — which octaves of the eddies are
		/// drawn (FishCloudDetailResolved) and how narrow an edge's ramp may be (FishCloudEdgeRampWidth) —
		/// since both already scale with it, so C needs no shader of its own.
		/// </summary>
		/// <remarks>
		/// The cone is a rebuilt pixel's (FishCloudsFeature's spread, 2026-09-28: each ray answers for one
		/// pixel), and C's floor is in marched texels, so the floor is taken across by
		/// <see cref="FishCloudsFeature.PixelsPerMarchedTexel"/>. Without C the share is untouched.
		/// </remarks>
		public float DetailConeShare(float share) => DetailConeShare(share, FishCloudsFeature.PixelsPerMarchedTexel);

		/// <summary><see cref="DetailConeShare(float)"/> with <paramref name="pixelsPerTexel"/> rebuilt pixels to a marched texel.</summary>
		public float DetailConeShare(float share, float pixelsPerTexel) =>
			FootprintTexels > 0f ? Mathf.Max(share, FootprintTexels * Mathf.Max(1f, pixelsPerTexel)) : share;

		/// <summary>
		/// The options as the cloud shader reads them (<c>_FishCloudOptions</c>): x A (the B-spline kernel),
		/// y B, z D, each 1 on and 0 off — y and z are no longer read, only kept so the probes still print
		/// what was asked; w the rays' phase on <paramref name="frame"/> (<see cref="JitterPhase"/>),
		/// whatever the options: D's phase became the march's own on 2026-09-28.
		/// </summary>
		public Vector4 ShaderVector(int frame)
		{
			return new Vector4(SmoothFill ? 1f : 0f, ConfidenceBlur ? 1f : 0f, LowDiscrepancyJitter ? 1f : 0f, JitterPhase(frame));
		}

		/// <summary>
		/// The rays' phase for <paramref name="frame"/>, 0..1, added to each ray's spatial noise (interleaved
		/// gradient noise over the marched texels) and wrapped.
		/// </summary>
		/// <remarks>
		/// <para>
		/// On one frame every texel is looked through at the same place (<see cref="FishCloudsFeature.SubPixelSlot"/>),
		/// so the frame's phase is also that place's. What averages a rebuilt pixel's noise away is the phases
		/// of the samples that land near it — its own place and its neighbours', which the steadying's tent
		/// weighs most — so those must be as different as possible.
		/// </para>
		/// <para>
		/// The phase is the place's Bayer value over sixteen plus the golden ratio times the cycle. Within a
		/// cycle the sixteen places take the sixteen sixteenths once each, so a texel's rays cover the step
		/// evenly; neighbouring places differ by three to eight sixteenths, because the Bayer matrix puts
		/// its far-apart values side by side; and each place's own phases, a cycle apart, are the
		/// golden-ratio sequence, which stays evenly spread however many have been taken.
		/// </para>
		/// <para>
		/// It was the place's value BIT-REVERSED over sixteen (until 2026-09-29), chosen so consecutive frames
		/// were half a step apart. Bit-reversing the Bayer matrix gives a Morton ramp: 0 1 4 5 / 3 2 7 6 /
		/// 12 13 8 9 / 15 14 11 10 — neighbouring places a sixteenth apart. Each pixel was then steadied from
		/// samples of nearly one phase, its own, and the step error that phase leaves ramped smoothly across
		/// the texel's four pixels and jumped at its edge, where the next texel's noise began: a scale
		/// pattern four pixels across, re-rolled every sixteen frames, that no amount of averaging removed.
		/// The diagnostics' Decorrelated Ray Phase off brings it back (<see cref="JitterPhaseLegacy"/>).
		/// </para>
		/// </remarks>
		public static float JitterPhase(int frame)
		{
			frame = Mathf.Max(0, frame);
			int slot = FishCloudsFeature.SubPixelSlot(frame);
			int cycle = frame / FishCloudsFeature.SubPixelPlaces;
			double phase = cycle * GoldenRatioConjugate + slot / 16.0;
			return (float)(phase - System.Math.Floor(phase));
		}

		/// <summary>
		/// The rays' phase as it was until 2026-09-29: the place's value bit-reversed, so neighbouring places
		/// took nearly the same phase (see <see cref="JitterPhase"/>). For the diagnostics only.
		/// </summary>
		public static float JitterPhaseLegacy(int frame)
		{
			frame = Mathf.Max(0, frame);
			int slot = FishCloudsFeature.SubPixelSlot(frame);
			int cycle = frame / FishCloudsFeature.SubPixelPlaces;
			double phase = cycle * GoldenRatioConjugate + BitReversed(slot, 4) / 16.0;
			return (float)(phase - System.Math.Floor(phase));
		}

		/// <summary>The low <paramref name="bits"/> bits of <paramref name="value"/> in reverse order.</summary>
		private static int BitReversed(int value, int bits)
		{
			int result = 0;
			for (int i = 0; i < bits; i++)
			{
				result = (result << 1) | ((value >> i) & 1);
			}
			return result;
		}

		/// <summary>
		/// Option A's kernel along one axis, <paramref name="x"/> texels from a sample: the quadratic
		/// B-spline, ¾ − x² to |x| = ½ and ½(3/2 − |x|)² to 3/2 (de Boor 1978). Twin of FishResolveKernel's
		/// option-A branch (FishCloudResolve.hlsl).
		/// </summary>
		public static float BSpline(float x)
		{
			float a = Mathf.Abs(x);
			if (a <= 0.5f)
			{
				return 0.75f - a * a;
			}
			float tail = Mathf.Clamp01(1.5f - a);
			return 0.5f * tail * tail;
		}

		/// <summary>
		/// Option B's halo width, as a variance in pixels², for a pixel with <paramref name="confidence"/>
		/// behind it when a marched texel spans <paramref name="perTexel"/> pixels: σ₀² plus the rest of the
		/// way to perTexel²/2π — the width that gathers one sample's worth a frame — over 1 + confidence.
		/// No longer read by the steadying (option B is inert since the temporal upsampler of 2026-09-28).
		/// </summary>
		public static float BlurSigma2(float confidence, float perTexel)
		{
			float sharp = FishCloudsFeature.SampleSigmaPixels * FishCloudsFeature.SampleSigmaPixels;
			float wide = perTexel * perTexel / (2f * Mathf.PI);
			return sharp + Mathf.Max(0f, wide - sharp) / (1f + Mathf.Max(0f, confidence));
		}

		/// <summary>
		/// How many of the sixteen places a cycle land in each pixel, never less than one: sixteen over
		/// perTexel² pixels. It was what option A faded its fill out over, before the temporal upsampler of
		/// 2026-09-28; the steadying no longer reads it.
		/// </summary>
		public static float FillCycleWeight(float perTexel)
		{
			return Mathf.Max(1f, FishCloudsFeature.SubPixelPlaces / Mathf.Max(1f, perTexel * perTexel));
		}
	}

	/// <summary>
	/// The steadying's per-pixel arithmetic (FishCloudResolve in FishCloudResolve.hlsl) in C#, so its shape
	/// can be tested without a GPU: the current estimate's kernel, the reprojection of a cloud carried by
	/// its own band's drift, the variance clip, and the moving average.
	/// </summary>
	/// <remarks>
	/// Twin of the shader, constant for constant; change both together. What it leaves out is what the
	/// shader does with surfaces (the depth test that keeps a post from the sky's texels) and the
	/// platform's UV flip, which the tests do not need.
	/// </remarks>
	public static class CloudResolveTwin
	{
		/// <summary>Standard deviations of this frame's texels a history may stand from their mean (FISH_RESOLVE_GAMMA).</summary>
		public const float Gamma = 1.25f;
		/// <summary>The least a standard deviation is taken to be, of full scale (FISH_RESOLVE_SIGMA_FLOOR).</summary>
		public const float SigmaFloor = 0.002f;
		/// <summary>Opacity below which the texels round a pixel saw no cloud to reproject by (FISH_RESOLVE_CLOUD_SEEN).</summary>
		public const float CloudSeen = 0.0005f;
		/// <summary>How big a change a hard clip must be before it lets go (<c>_FishCloudUpsample.z</c>, the diagnostics' default).</summary>
		public const float ResetChange = VolumetricCloudDiagnostics.DefaultHistoryResetChange;
		/// <summary>The brightness a change is measured against at the least (FISH_RESOLVE_CHANGE_FLOOR).</summary>
		public const float ChangeFloor = 0.005f;

		/// <summary>The tent, max(0, 1 − |x|): the current estimate's kernel along one axis, x texels from a sample.</summary>
		public static float Tent(float x) => Mathf.Max(0f, 1f - Mathf.Abs(x));

		/// <summary>FishResolveKernel: the tent per axis, or option A's quadratic B-spline (<see cref="CloudOptions.BSpline"/>).</summary>
		public static float Kernel(Vector2 apart, bool bSpline) => bSpline
			? CloudOptions.BSpline(apart.x) * CloudOptions.BSpline(apart.y)
			: Tent(apart.x) * Tent(apart.y);

		/// <summary>HLSL's smoothstep(edge0, edge1, x) — not Mathf.SmoothStep, whose arguments mean something else.</summary>
		public static float SmoothStep(float edge0, float edge1, float x)
		{
			float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
			return t * t * (3f - 2f * t);
		}

		/// <summary>FishResolveToYCoCg: premultiplied light to luma and two chroma axes, transmittance as it is.</summary>
		public static Vector4 ToYCoCg(Vector4 value) => new Vector4(
			0.25f * value.x + 0.5f * value.y + 0.25f * value.z,
			0.5f * value.x - 0.5f * value.z,
			-0.25f * value.x + 0.5f * value.y - 0.25f * value.z,
			value.w);

		/// <summary>FishResolveFromYCoCg, the exact inverse of <see cref="ToYCoCg"/>.</summary>
		public static Vector4 FromYCoCg(Vector4 value) => new Vector4(
			value.x + value.y - value.z,
			value.x + value.z,
			value.x - value.y - value.z,
			value.w);

		/// <summary>
		/// FishResolveClip: <paramref name="history"/> clipped toward <paramref name="mean"/> onto the box
		/// mean ± <paramref name="extent"/>, along the line between them; <paramref name="units"/> is how far
		/// out it stood, in box half-widths (≤ 1 inside, where it is returned untouched).
		/// </summary>
		public static Vector4 Clip(Vector4 history, Vector4 mean, Vector4 extent, out float units)
		{
			Vector4 offset = history - mean;
			units = 0f;
			for (int i = 0; i < 4; i++)
			{
				units = Mathf.Max(units, Mathf.Abs(offset[i]) / Mathf.Max(extent[i], 1e-6f));
			}
			return units > 1f ? mean + offset / units : history;
		}

		/// <summary>
		/// Step 3 for a whole pixel: <paramref name="history"/> clipped to the mean ± γσ of
		/// <paramref name="neighbours"/> (this frame's three-by-three texels) in YCoCg and transmittance.
		/// </summary>
		public static Vector4 Rectify(Vector4 history, IList<Vector4> neighbours, out float units)
		{
			Vector4 sum = Vector4.zero, squares = Vector4.zero;
			foreach (Vector4 value in neighbours)
			{
				Vector4 y = ToYCoCg(value);
				sum += y;
				squares += Vector4.Scale(y, y);
			}
			float n = Mathf.Max(1, neighbours.Count);
			Vector4 mean = sum / n;
			var extent = new Vector4();
			for (int i = 0; i < 4; i++)
			{
				extent[i] = Gamma * (Mathf.Sqrt(Mathf.Max(0f, squares[i] / n - mean[i] * mean[i])) + SigmaFloor);
			}
			return FromYCoCg(Clip(ToYCoCg(history), mean, extent, out units));
		}

		/// <summary>How much of its frames a history keeps after the clip pulled it <paramref name="units"/> box half-widths.</summary>
		public static float Keeps(float units) => 1f - SmoothStep(1f, 3f, units);

		/// <summary>
		/// How real a change of <paramref name="change"/> (a share of the brightness, or of the opacity) is,
		/// 0..1, against <paramref name="threshold"/>: none under half of it, all of it over one and a half.
		/// A threshold of 0 takes every change as real (the old way).
		/// </summary>
		public static float Real(float change, float threshold = ResetChange) =>
			threshold > 0f ? SmoothStep(0.5f * threshold, 1.5f * threshold, change) : 1f;

		/// <summary>
		/// The frames a history of <paramref name="frames"/> keeps after the clip pulled it
		/// <paramref name="units"/> box half-widths, when it stood <paramref name="change"/> off (a share):
		/// let go of by how far out it stood, but only as far as the change is real.
		/// </summary>
		public static float FramesAfterClip(float frames, float units, float change, float threshold = ResetChange) =>
			frames * (1f - SmoothStep(1f, 3f, units) * Real(change, threshold));

		/// <summary>
		/// Step 4: the moving average. <paramref name="frames"/> behind the history (0 for none), capped at
		/// <paramref name="settled"/>: this frame counts for one part in (frames + 1).
		/// </summary>
		public static Vector4 Blend(Vector4 history, float frames, Vector4 current, float settled, out float framesAfter)
		{
			framesAfter = Mathf.Min(frames + 1f, Mathf.Max(1f, settled));
			return Vector4.Lerp(history, current, 1f / framesAfter);
		}

		/// <summary>
		/// Steps 3 and 4 for one channel of one pixel: the history clipped to <paramref name="mean"/> ±
		/// γ(<paramref name="sigma"/> + floor), let go of by how far — as far as the change is real
		/// (<paramref name="resetChange"/>) — and averaged with <paramref name="current"/>.
		/// <paramref name="usable"/> false is a pixel with no history (α = 1).
		/// </summary>
		public static float Step(float history, float frames, bool usable, float current, float mean, float sigma,
			float settled, out float framesAfter, float resetChange = ResetChange)
		{
			if (!usable)
			{
				history = current;
				frames = 0f;
			}
			else
			{
				float extent = Gamma * (sigma + SigmaFloor);
				float units = Mathf.Abs(history - mean) / Mathf.Max(extent, 1e-6f);
				// How far off the history stood, as a share of the brighter of the two: taken before the clip.
				float change = Mathf.Abs(history - mean) / Mathf.Max(Mathf.Max(history, mean), ChangeFloor);
				if (units > 1f)
				{
					history = mean + (history - mean) / units;
				}
				frames = FramesAfterClip(frames, units, change, resetChange);
			}
			framesAfter = Mathf.Min(frames + 1f, Mathf.Max(1f, settled));
			return Mathf.Lerp(history, current, 1f / framesAfter);
		}

		/// <summary>
		/// Step 2: where the cloud a pixel sees at <paramref name="distance"/> down its ray stood last frame —
		/// carried back by its own bands' drift, the low clouds' <paramref name="lowDriftStep"/> (m, world x
		/// and z) times their mean wind <paramref name="gain"/>.
		/// </summary>
		public static Vector3 CloudWas(Vector3 camera, Vector3 direction, float distance, float gain, Vector2 lowDriftStep) =>
			camera + direction * distance - gain * new Vector3(lowDriftStep.x, 0f, lowDriftStep.y);

		/// <summary>
		/// A point (w 1) or a direction at the far plane (w 0) through <paramref name="viewProjection"/>, as a
		/// screen uv (0..1, no platform flip); false behind the camera.
		/// </summary>
		public static bool ScreenOf(Matrix4x4 viewProjection, Vector4 point, out Vector2 uv)
		{
			Vector4 clip = viewProjection * point;
			float w = Mathf.Max(1e-5f, clip.w);
			uv = new Vector2(clip.x / w * 0.5f + 0.5f, clip.y / w * 0.5f + 0.5f);
			return clip.w > 1e-5f;
		}

		/// <summary>FishResolveRay without the platform flip: the world ray through screen <paramref name="uv"/>.</summary>
		public static Vector3 RayFor(Matrix4x4 inverseViewProjection, Vector2 uv, Vector3 camera)
		{
			Vector4 world = inverseViewProjection * new Vector4(uv.x * 2f - 1f, uv.y * 2f - 1f, 1f, 1f);
			return (new Vector3(world.x, world.y, world.z) / world.w - camera).normalized;
		}
	}
}
