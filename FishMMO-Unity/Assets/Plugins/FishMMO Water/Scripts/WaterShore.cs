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

		// ── The rhythm the sea's surf train keeps time with (FishWaterSurf.hlsl) ──

		/// <summary>The shore's clock, in seconds: what the swash and the surf train both run on.</summary>
		public double SurfClock => clock;

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
			/* No shore keeping time any more: the sea's surf train goes back to its own clock. Left
			 * set, it would run on a shore clock that no longer advances, and the surf would freeze. */
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
			lastRealtime = Time.realtimeSinceStartupAsDouble;
			overridden = true;
		}

		private bool overridden;

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
			if (!overridden)
			{
				// The world's motion, so the surf stops when the world's time does.
				clock = (clock + WorldMotion.Scale(Mathf.Clamp((float)(now - lastRealtime), 0f, 0.25f))) % 10000.0;
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
					Mathf.Exp(-(float)elapsed / Mathf.Max(0.1f, FoamLingers)), 1f, 0f, 0f));

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
				SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R16_SFloat
				: SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R32_SFloat
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
