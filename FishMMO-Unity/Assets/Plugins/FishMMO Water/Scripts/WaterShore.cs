using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FishMMO.Shared.Celestial;

namespace FishMMO.Water
{
	/// <summary>
	/// The shoreline: the sheet of water that runs up a beach and drains back, the foam at its lip,
	/// and the wet sand it leaves behind.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Separate from the ocean, and that is the whole design.</b> The sea is a deep-water
	/// spectrum drawn on a camera-centred disc; a shore is a different phenomenon on different
	/// ground. Its timing comes from when each wave ARRIVES rather than from any spectrum, its
	/// water is a centimetre-thick sheet rather than a displaced surface, and its foam has memory —
	/// made where the wave breaks, carried up the beach, left behind as the water drains. Trying
	/// to express that in the ocean's vertex shader is what produced a hard waterline, stranded
	/// puddles and no foam at all.
	/// </para>
	/// <para>
	/// <b>Projected, not meshed.</b> A skirt mesh would need its own tessellation along the
	/// waterline — which is exactly where the ocean disc's tessellation was already wrong — and
	/// could not conform to a rock or a pier standing in the surf. This reads the depth buffer,
	/// reconstructs each pixel's world position and shades it if it lies in the band, so it fits
	/// the ground and everything on it for free.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Shore")]
	[RequireComponent(typeof(WaterSurface))]
	public sealed class WaterShore : MonoBehaviour
	{
		private static readonly int PeriodId = Shader.PropertyToID("_FishWaterSwashPeriod");
		private static readonly int ReachId = Shader.PropertyToID("_FishWaterSwashReach");
		private static readonly int SeaId = Shader.PropertyToID("_FishWaterSwashSea");
		private static readonly int SkewId = Shader.PropertyToID("_FishWaterSwashSkew");
		private static readonly int TimeId = Shader.PropertyToID("_FishWaterShoreTime");
		private static readonly int CyclesId = Shader.PropertyToID("_FishWaterSwashCycles");
		private static readonly int FoamId = Shader.PropertyToID("_FoamTexture");

		[Tooltip("The shore material. Must use FishMMO/Water/Shore.")]
		public Material Material;

		[Header("Swash")]
		[Tooltip("Seconds between arriving waves. Real surf runs 6 to 14 depending on the swell.")]
		[Range(2f, 30f)] public float Period = 9f;
		[Tooltip("How far up the beach the water runs at full strength, in metres.")]
		[Range(0.5f, 60f)] public float Reach = 14f;
		[Tooltip("0 rushes up and drains at the same rate; 1 is a fast rush and a long drain.")]
		[Range(0f, 1f)] public float Skew = 0.75f;

		[Header("Sea state")]
		[Tooltip("Let the wind drive how far the swash reaches and how often waves arrive.")]
		public bool DriveFromSea = true;

		[Header("Foam left behind")]
		[Tooltip("Seconds the foam a wave strands on the sand takes to fade to about a third.")]
		[Range(1f, 40f)] public float FoamLingers = 8f;
		[Tooltip("Seconds the whitewater a breaker's bore leaves on the water takes to fade to about a third. Bubbles on moving water go faster than foam on sand.")]
		[Range(0.5f, 20f)] public float WhitewaterLingers = 3.5f;
		[Tooltip("The pass that keeps it. Referenced so a build includes it; found by name otherwise.")]
		public Shader FoamMemoryShader;

		/// <summary>The foam memory's shader, for finding it when the reference was never set.</summary>
		public const string FoamMemoryShaderName = "Hidden/FishMMO/Water/ShoreFoamMemory";

		private static readonly int MemoryId = Shader.PropertyToID("_FishWaterFoamMemory");
		private static readonly int PreviousId = Shader.PropertyToID("_FoamMemoryPrevious");
		private static readonly int MemorySizeId = Shader.PropertyToID("_FoamMemorySize");
		private static readonly int MemoryStepId = Shader.PropertyToID("_FoamMemoryStep");

		/// <summary>How often the memory is stepped, per second of the swash's clock.</summary>
		private const float MemoryRate = 30f;

		private Material memoryMaterial;
		private RenderTexture memoryA;
		private RenderTexture memoryB;
		private bool memoryInA = true;
		private WaterShoreField field;
		private double memoryClock = -1.0;
		private int memoryFrame = -1;

		private WaterSurface surface;
		private WaterEnvironment environment;
		private MeshRenderer meshRenderer;
		private MeshFilter meshFilter;
		private Mesh mesh;
		private double clock;
		private double lastRealtime = -1.0;

		// ── The rhythm the breakers keep time with (FishWaterSurf.hlsl) ──

		/// <summary>The shore's clock, in seconds: what the swash and the breakers both run on.</summary>
		public double SurfClock => clock;

		/// <summary>
		/// Which wave the shore is on, in waves: the whole part numbers the wave (the shaders draw each
		/// wave's height from it, FishWaterWaveShare), the fraction is how far through it the swash is.
		/// </summary>
		/// <remarks>
		/// <para>
		/// It was the clock over the period. The period is the live sea's, so it drifts as the wind
		/// does, and with the clock thousands of seconds in, a drift of one part in ten thousand moved
		/// the swash a fifth of a wave. Then it was added up a frame at a time at the period of the
		/// moment, which fixed that but made it each client's own: how long it had run, at the periods
		/// its own easing had passed through. Two players saw different waves on the same beach.
		/// </para>
		/// <para>
		/// <b>Now a function of the shared clock, in windows.</b> The period is held for each
		/// <see cref="SwashWindowSeconds"/> of the world-motion clock and snapped so the window holds a
		/// whole number of waves; within it the count is that number times the share of the window
		/// gone. So the phase comes round to a wave's start exactly as each window ends, and is the same
		/// for every player, and still follows the sea's period, a window behind at most.
		/// </para>
		/// <para>
		/// <b>The numbering cannot carry over, so it is eased over.</b> A count continuous across
		/// windows would be the sum of every earlier window's waves since the epoch, and the period each
		/// window held is the eased sea state — the weather sampled along the coast and a fetch march
		/// over the planet — which no client can replay for the ~200 000 windows since then. So each
		/// window numbers its waves from its own base (<see cref="SwashBase"/>), and where the number
		/// changes at a window's start the shaders ease each wave's height from the old number's draw to
		/// the new one's over the first few waves (<c>_FishWaterSwashRelabel</c>), rather than every
		/// wave in flight changing height at once. Only the fraction has to be continuous for the
		/// motion, and it is.
		/// </para>
		/// </remarks>
		public double SurfCycles => cycles;
		private double cycles;

		/// <summary>Seconds of the shared clock the swash's period is held for: a dozen waves or so.</summary>
		private const double SwashWindowSeconds = 120.0;
		/// <summary>Wave numbers each window may use: more than a window can hold at the shortest period, half a second.</summary>
		private const int SwashWindowStride = 256;
		/// <summary>Windows before the numbering comes round, keeping it under 1024 where a float still resolves a cycle finely.</summary>
		private const int SwashWindowLabels = 4;
		/// <summary>Waves over which the heights ease from the last window's numbering to this one's.</summary>
		private const float SwashRelabelWaves = 3f;
		private static readonly int RelabelId = Shader.PropertyToID("_FishWaterSwashRelabel");
		private long heldPeriodWindow = long.MinValue;
		private float heldPeriod;
		private long swashWindow = long.MinValue;
		private int swashWaves;
		private bool swashRelabels;
		private float swashRelabel;

		/// <summary>The first wave number of a window: windows number their waves from their own base.</summary>
		private static long SwashBase(long window)
		{
			long label = window % SwashWindowLabels;
			return (label < 0 ? label + SwashWindowLabels : label) * SwashWindowStride;
		}

		/// <summary>Seconds between arriving waves, as last published; 0 before the first frame.</summary>
		public float SurfPeriod { get; private set; }

		/// <summary>The significant height of the sea making the surf, in metres, as last published.</summary>
		public float SurfSeaHeight { get; private set; }

		/// <summary>The deep-water wavelength at the period the waves arrive at, in metres.</summary>
		public float SurfDeepWavelength { get; private set; }

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			/* Subscribed BEFORE Build, deliberately. If Build throws, an [ExecuteAlways]
			 * component that had not yet subscribed is dead for good — the lazy repair inside
			 * OnBeginCameraRendering can never run because OnBeginCameraRendering is never
			 * called. Wiring up first makes the failure recoverable on the next frame. */
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			Build();
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			/* No shore keeping time any more: the breakers and their whitewater stop. Left set, they
			 * would run on a shore clock that no longer advances, and freeze mid-break. */
			Shader.SetGlobalFloat(PeriodId, 0f);
			SurfPeriod = 0f;
			ReleaseMemory();
			if (memoryMaterial != null)
			{
				if (Application.isPlaying)
				{
					Destroy(memoryMaterial);
				}
				else
				{
					DestroyImmediate(memoryMaterial);
				}
				memoryMaterial = null;
			}
			Shader.SetGlobalTexture(MemoryId, Texture2D.blackTexture);
			if (mesh != null)
			{
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

		/// <summary>Builds the full-screen triangle the pass is drawn with.</summary>
		private void Build()
		{
			Transform host = transform.Find("Shore");
			GameObject child = host != null ? host.gameObject : null;
			if (child == null)
			{
				child = new GameObject("Shore") { hideFlags = HideFlags.DontSave };
				child.transform.SetParent(transform, false);
			}

			/* Explicit == null, never ??.
			 *
			 * A missing component comes back as a UnityEngine.Object whose native side is gone —
			 * "fake null" — and the ?? operator tests for a REAL null reference, so it keeps the
			 * fake one and never calls AddComponent. The next line then throws
			 * MissingComponentException from inside OnEnable, which in an [ExecuteAlways]
			 * component means the rest of the method never runs and the material is silently never
			 * created. Unity's own == overload is the only null test that understands this. */
			meshFilter = child.GetComponent<MeshFilter>();
			if (meshFilter == null)
			{
				meshFilter = child.AddComponent<MeshFilter>();
			}
			meshRenderer = child.GetComponent<MeshRenderer>();
			if (meshRenderer == null)
			{
				meshRenderer = child.AddComponent<MeshRenderer>();
			}

			if (mesh == null)
			{
				mesh = new Mesh { name = "Shore", hideFlags = HideFlags.HideAndDontSave };
				mesh.vertices = new[]
				{
					new Vector3(-1f, -1f, 0f),
					new Vector3(3f, -1f, 0f),
					new Vector3(-1f, 3f, 0f),
				};
				mesh.triangles = new[] { 0, 1, 2 };
				/* Enormous, because the vertex shader ignores the transform entirely: culling
				 * would otherwise throw the pass away the moment its origin left the frustum. */
				mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
			}

			meshFilter.sharedMesh = mesh;
			if (Material == null)
			{
				/* Found rather than referenced, so the component works the moment it is added and
				 * no material asset has to be wired up by hand for a scene to have a shoreline. */
				Shader shader = Shader.Find("FishMMO/Water/Shore");
				if (shader != null)
				{
					Material = new Material(shader) { name = "Shore", hideFlags = HideFlags.HideAndDontSave };
					Texture foam = Shader.GetGlobalTexture("_FishWaterFoamTexture");
					if (foam != null)
					{
						Material.SetTexture("_FoamTexture", foam);
					}
				}
			}
			if (Material != null)
			{
				meshRenderer.sharedMaterial = Material;
			}
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = false;
			meshRenderer.lightProbeUsage = LightProbeUsage.Off;
			meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
		}

		/// <summary>
		/// Drives the swash clock from outside, for a scene whose time is not wall time — and for
		/// probes, which have to step through a cycle deterministically to see one at all.
		/// </summary>
		public void SetClock(double seconds)
		{
			clock = seconds % 10000.0;
			// Stepped by hand, a probe expects the clock over the period, and gets it.
			cycles = (seconds / Mathf.Max(0.5f, SurfPeriod)) % 1000.0;
			lastRealtime = Time.realtimeSinceStartupAsDouble;
			overridden = true;
			// From then on this shore keeps its own clock, carried on from here as it always was.
			manualClock = true;
		}

		private bool overridden;
		private bool manualClock;

		/// <summary>
		/// Which wave the shore is on now, from the shared clock (<see cref="SurfCycles"/> says why it is
		/// windowed), and how far the heights have eased onto this window's numbering.
		/// </summary>
		/// <remarks>
		/// The period published to the shaders stays the eased one, not the held one: the breakers place
		/// each wave's run in by it (FishWaterBreakerAt), and a held period would step them all at a
		/// window's start. The count runs at most half a wave a window off it, a few per cent. The held
		/// one is the sea's at the window's start, worked out from the clock (WindowPeriod), so every
		/// client counts the same waves.
		/// </remarks>
		private void CountWaves()
		{
			double seconds = WorldMotion.Seconds;
			double whole = System.Math.Floor(seconds / SwashWindowSeconds);
			long window = (long)whole;
			double into = System.Math.Max(0.0, System.Math.Min(SwashWindowSeconds, seconds - whole * SwashWindowSeconds));
			if (window != heldPeriodWindow)
			{
				heldPeriodWindow = window;
				heldPeriod = WindowPeriod(whole * SwashWindowSeconds);
			}
			float held = heldPeriod;
			int waves = Mathf.Clamp(Mathf.RoundToInt((float)(SwashWindowSeconds / Mathf.Max(0.5f, held))), 1, SwashWindowStride - 1);
			if (window != swashWindow)
			{
				/* Straight on from the last window, the number the last one ended on is carried for the
				 * heights to ease off; after a gap (a join, a stall, a hop) there is nothing on screen to
				 * keep, and the new numbering takes over at once. */
				swashRelabels = swashWindow != long.MinValue && window == swashWindow + 1;
				if (swashRelabels)
				{
					// What a wave was numbered in the last window less what it is numbered now.
					swashRelabel = SwashBase(swashWindow) + swashWaves - SwashBase(window);
				}
				swashWindow = window;
				swashWaves = waves;
			}
			double gone = into * swashWaves / SwashWindowSeconds;
			cycles = SwashBase(window) + gone;
			float eased = swashRelabels ? Mathf.Clamp01((float)gone / SwashRelabelWaves) : 1f;
			Shader.SetGlobalVector(RelabelId, new Vector4(swashRelabels ? swashRelabel : 0f, eased, 0f, 0f));
		}

		/// <summary>
		/// The period a window of the wave count holds, s: the sea's own at the moment the window began,
		/// worked out afresh by the weather then (<see cref="WaterEnvironment.PeakPeriodAt"/>) — the same
		/// number on every client, whenever it joined. The eased period it held before was each client's
		/// own, and two could round to a different number of waves in a window. Without a sea that
		/// follows the weather, the period this shore is using now.
		/// </summary>
		private float WindowPeriod(double windowStartSeconds)
		{
			if (DriveFromSea)
			{
				environment ??= GetComponent<WaterEnvironment>();
				if (environment != null && environment.SignificantHeight > 0f)
				{
					float period = environment.PeakPeriodAt(windowStartSeconds);
					if (!float.IsNaN(period))
					{
						return Mathf.Clamp(period, 3f, 20f);
					}
				}
			}
			return SurfPeriod;
		}

		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera == null || camera.cameraType == CameraType.Reflection
				|| camera.cameraType == CameraType.Preview)
			{
				return;
			}

			/* Rebuilt here rather than only at enable, for the same reason the FFT is created
			 * lazily: a component's fields are assigned AFTER AddComponent returns, and anything
			 * that could not be resolved at enable time — the shader, the material, the renderer —
			 * would otherwise stay null for the life of the object with no error anywhere. */
			if (meshRenderer == null || Material == null)
			{
				Build();
			}
			if (Material == null)
			{
				return;
			}

			/* The foam mask, retried until it is there.
			 *
			 * WaterSurface publishes it from its own material during ITS begin-camera callback,
			 * which is after this component's OnEnable — so a one-shot attempt at build time
			 * always came back null and the mask silently fell back to the default white texture.
			 * A mask that is 1 everywhere multiplies to nothing: the foam line rendered as a solid
			 * band of cream with no structure in it at all, which is not a shading problem and
			 * cannot be tuned away. */
			if (Material.GetTexture(FoamId) == null)
			{
				Texture foam = Shader.GetGlobalTexture("_FishWaterFoamTexture");
				if (foam != null)
				{
					Material.SetTexture(FoamId, foam);
				}
			}

			double now = Time.realtimeSinceStartupAsDouble;
			if (lastRealtime < 0.0)
			{
				lastRealtime = now;
			}
			if (manualClock && !overridden)
			{
				// Driven by hand (a probe): carried on as it always was, a frame's step at a time.
				double step = WorldMotion.Scale(Mathf.Clamp((float)(now - lastRealtime), 0f, 0.25f));
				clock = (clock + step) % 10000.0;
				cycles = (cycles + step / Mathf.Max(0.5f, SurfPeriod)) % 1000.0;
			}
			else if (!manualClock)
			{
				// The shared world-motion clock, which stops and slows with the world and never races
				// (WorldMotion): every player's shore on the same second. The foam scroll that reads it
				// (0.12 a second) moves a whole 1200 tiles in the wrap, so the wrap is seamless.
				clock = WorldMotion.Repeat(WorldMotion.Seconds, WorldMotion.ShaderWrapSeconds);
			}
			overridden = false;
			lastRealtime = now;

			float period = Period;
			float reach = Reach;
			// The sea the swash is made by. Without one to read, the wave that runs a beach up by the
			// authored reach: about a thirteenth of it, as the reach is set from the sea below.
			float waveHeight = Reach / 13f;
			if (DriveFromSea)
			{
				environment ??= GetComponent<WaterEnvironment>();
				if (environment != null && environment.SignificantHeight > 0f)
				{
					waveHeight = environment.SignificantHeight;
					/* From the sea state itself: waves arrive at the sea's peak period, and run up
					 * a distance that grows with their height. On a beach of ordinary slope the
					 * run-up travels a dozen or so metres for every metre of wave. */
					period = Mathf.Clamp(environment.PeakPeriod, 3f, 20f);
					reach = Mathf.Clamp(environment.SignificantHeight * 13f, 1.5f, 60f);
				}
				else if (surface != null)
				{
					float wind = Mathf.Clamp(surface.WindSpeed, 0f, 30f);
					period = Mathf.Lerp(5f, 14f, Mathf.InverseLerp(2f, 22f, wind));
					reach = Reach * Mathf.Lerp(0.35f, 2.2f, Mathf.InverseLerp(2f, 22f, wind));
					waveHeight = reach / 13f;
				}
			}

			SurfPeriod = Mathf.Max(0.5f, period);
			if (!manualClock)
			{
				CountWaves();
			}
			else
			{
				Shader.SetGlobalVector(RelabelId, new Vector4(0f, 1f, 0f, 0f));
			}
			Shader.SetGlobalFloat(PeriodId, SurfPeriod);
			Shader.SetGlobalFloat(ReachId, Mathf.Max(0.5f, reach));
			/* What the run-up is worked out from, per beach, in the shader: the wave height, and the
			 * deep-water wavelength at the period the waves arrive at, g·T²/2π. */
			float gravity = surface != null ? Mathf.Max(0.05f, surface.Gravity) : 9.81f;
			float deepWavelength = gravity * period * period / (2f * Mathf.PI);
			SurfSeaHeight = Mathf.Max(0.05f, waveHeight);
			SurfDeepWavelength = deepWavelength;
			Shader.SetGlobalVector(SeaId, new Vector4(SurfSeaHeight, SurfDeepWavelength, 0f, 0f));
			Shader.SetGlobalFloat(SkewId, Mathf.Clamp01(Skew));
			Shader.SetGlobalFloat(TimeId, (float)clock);
			Shader.SetGlobalFloat(CyclesId, (float)cycles);

			// Once a frame, whichever camera is first: the memory is the beach's, not the camera's.
			if (memoryFrame != Time.frameCount)
			{
				memoryFrame = Time.frameCount;
				StepFoamMemory();
			}
		}

		/// <summary>
		/// Keeps the foam the swash strands: what was there fades, and whatever the lip is laying now
		/// is added. Stepped at a fixed rate on the swash's own clock, so it stops with the world.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two channels: R the foam the swash strands on the sand, G the whitewater the breakers' bores
		/// leave on the water, which the ocean reads.
		/// </para>
		/// <para>
		/// <b>Half float, not eight bits.</b> A fade is a multiply by a factor just under one, and an
		/// eight-bit value times 0.998 rounds straight back to itself — the foam would never go. The
		/// fixed step rate keeps the factor far enough from one for half float to resolve it at any
		/// frame rate.
		/// </para>
		/// <para>
		/// Over the shore field's own rectangle, at its resolution, so a stranded line sits where the
		/// field puts the water and costs one texture — not a window that follows the camera and
		/// forgets every beach it leaves.
		/// </para>
		/// </remarks>
		private void StepFoamMemory()
		{
			if (field == null)
			{
				field = GetComponent<WaterShoreField>();
			}
			int size = field != null ? field.Resolution : 0;
			if (size <= 0)
			{
				Shader.SetGlobalTexture(MemoryId, Texture2D.blackTexture);
				return;
			}
			if (memoryMaterial == null)
			{
				if (FoamMemoryShader == null)
				{
					FoamMemoryShader = Shader.Find(FoamMemoryShaderName);
				}
				if (FoamMemoryShader == null || !FoamMemoryShader.isSupported)
				{
					Shader.SetGlobalTexture(MemoryId, Texture2D.blackTexture);
					return;
				}
				memoryMaterial = new Material(FoamMemoryShader) { name = "Shore foam memory", hideFlags = HideFlags.HideAndDontSave };
			}
			/* Resized in place, never destroyed and made again: this runs inside a render callback,
			 * where Unity refuses DestroyImmediate — the same trap the sea's mesh and the shore
			 * field both fell into from OnValidate. */
			if (memoryA == null)
			{
				memoryA = CreateMemory(size);
				memoryB = CreateMemory(size);
				memoryInA = true;
				memoryClock = clock;
			}
			else if (memoryA.width != size)
			{
				Resize(memoryA, size);
				Resize(memoryB, size);
				memoryInA = true;
				memoryClock = clock;
			}

			// How far the swash's clock has run since the last step; it wraps at 10,000 s.
			double elapsed = clock - memoryClock;
			if (elapsed < 0.0)
			{
				elapsed += 10000.0;
			}
			if (elapsed >= 1.0 / MemoryRate)
			{
				memoryClock = clock;
				RenderTexture source = memoryInA ? memoryA : memoryB;
				RenderTexture target = memoryInA ? memoryB : memoryA;
				memoryMaterial.SetTexture(PreviousId, source);
				memoryMaterial.SetVector(MemorySizeId, new Vector4(size, size, 1f / size, 1f / size));
				memoryMaterial.SetVector(MemoryStepId, new Vector4(
					Mathf.Exp(-(float)elapsed / Mathf.Max(0.1f, FoamLingers)), 1f,
					Mathf.Exp(-(float)elapsed / Mathf.Max(0.1f, WhitewaterLingers)), 0f));

				RenderTexture previous = RenderTexture.active;
				Graphics.SetRenderTarget(target);
				memoryMaterial.SetPass(0);
				Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
				RenderTexture.active = previous;
				memoryInA = !memoryInA;
			}
			Shader.SetGlobalTexture(MemoryId, memoryInA ? memoryA : memoryB);
		}

		private static void Resize(RenderTexture texture, int size)
		{
			texture.Release();
			texture.width = size;
			texture.height = size;
			texture.Create();
			Clear(texture);
		}

		private static void Clear(RenderTexture texture)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			GL.Clear(false, true, Color.clear);
			RenderTexture.active = previous;
		}

		private static RenderTexture CreateMemory(int size)
		{
			GraphicsFormat format =
				SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R16G16_SFloat
				: SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R32G32_SFloat
				: GraphicsFormat.R16G16B16A16_SFloat;
			var texture = new RenderTexture(size, size, 0, format)
			{
				name = "Shore foam memory",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				useMipMap = false,
				hideFlags = HideFlags.HideAndDontSave,
			};
			texture.Create();
			// Starts clean: a new render texture's contents are undefined.
			Clear(texture);
			return texture;
		}

		private void ReleaseMemory()
		{
			ReleaseTexture(ref memoryA);
			ReleaseTexture(ref memoryB);
		}

		private static void ReleaseTexture(ref RenderTexture texture)
		{
			if (texture == null)
			{
				return;
			}
			texture.Release();
			if (Application.isPlaying)
			{
				Destroy(texture);
			}
			else
			{
				DestroyImmediate(texture);
			}
			texture = null;
		}
	}
}
