using UnityEngine;
using UnityEngine.Rendering;

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

		private WaterSurface surface;
		private MeshRenderer meshRenderer;
		private MeshFilter meshFilter;
		private Mesh mesh;
		private double clock;
		private double lastRealtime = -1.0;

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
				clock = (clock + Mathf.Clamp((float)(now - lastRealtime), 0f, 0.25f)) % 10000.0;
			}
			overridden = false;
			lastRealtime = now;

			float period = Period;
			float reach = Reach;
			if (DriveFromSea && surface != null)
			{
				/* Bigger seas throw the water further up and arrive less often, because both
				 * follow the wavelength — and wavelength follows the wind. A breeze laps at the
				 * sand every few seconds; a gale sends a wall up the beach twice a minute. */
				float wind = Mathf.Clamp(surface.WindSpeed, 0f, 30f);
				period = Mathf.Lerp(5f, 14f, Mathf.InverseLerp(2f, 22f, wind));
				reach = Reach * Mathf.Lerp(0.35f, 2.2f, Mathf.InverseLerp(2f, 22f, wind));
			}

			Shader.SetGlobalFloat(PeriodId, Mathf.Max(0.5f, period));
			Shader.SetGlobalFloat(ReachId, Mathf.Max(0.5f, reach));
			Shader.SetGlobalFloat(SkewId, Mathf.Clamp01(Skew));
			Shader.SetGlobalFloat(TimeId, (float)clock);
		}
	}
}
