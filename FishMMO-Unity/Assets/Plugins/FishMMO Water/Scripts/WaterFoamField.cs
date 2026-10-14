using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FishMMO.Shared.Celestial;

namespace FishMMO.Water
{
	/// <summary>
	/// The simulated foam: how much foam floats on the water round the camera, made where the water breaks and carried
	/// by it — off a rapid in lines, gathered where currents meet, drawn into streaks down the wind at sea, ridden in on
	/// the bores and left on the sand. Read by every water surface (FishWaterFoamField.hlsl).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> Every foam before this was worked out afresh each frame from where water breaks now (a crest's white,
	/// an envelope laid below a fall, a memory that faded in place), so it never moved as foam does. Real foam is carried.
	/// </para>
	/// <para>
	/// <b>How.</b> Two windows round the camera, a fine one (<see cref="NearTexelMetres"/>) and a coarse one
	/// (<see cref="FarTexelMetres"/>), each <see cref="Resolution"/> texels a side and world-aligned: a texel is a fixed
	/// piece of the world, and the window moves by whole texels. Each step, at <see cref="StepRate"/> on the world's motion
	/// clock:
	/// </para>
	/// <list type="number">
	/// <item>the inland water is drawn from straight above into the window's current map (its FoamSource pass): the
	/// current, a water mask and the foam the water makes there;</item>
	/// <item>a pass (FishWaterFoamField.shader) carries the foam along the current (back along it to where it was a step
	/// ago, Catmull-Rom so it does not blur), gathers it where the water converges, lets it decay, and adds what the
	/// water makes: the inland sources, the sea's white caps from the FFT's folding, the surf's bores, the swash's lip.
	/// The sea's current and the shore's are worked out there from what the sea and the shore already publish.</item>
	/// </list>
	/// <para>
	/// <b>Fragment passes, not compute.</b> They read the sea's and the shore's global textures as every surface does
	/// (compute kernels do not see render globals), and they run where compute does not (the FFT's own fallback).
	/// </para>
	/// <para>
	/// <b>Half float.</b> A decay is a multiply by a factor just under one; at eight bits it rounds back to itself (the
	/// shore's foam memory found the same).
	/// </para>
	/// </remarks>
	public static class WaterFoamField
	{
		/// <summary>The step's shader, found by name when no component offered one.</summary>
		public const string ShaderName = "Hidden/FishMMO/Water/Foam Field";

		/// <summary>The inland water's pass that draws its current and foam sources into a window (FishInlandWater.shader).</summary>
		public const string SourcePassName = "FoamSource";

		/// <summary>Texels a side of each window.</summary>
		public const int Resolution = 1024;

		/// <summary>The fine window's texel: 256 m round the camera, fine enough for the lines off a rapid.</summary>
		public const float NearTexelMetres = 0.25f;

		/// <summary>The coarse window's texel: a kilometre round the camera, for the sea's streaks and the far rivers.</summary>
		public const float FarTexelMetres = 1f;

		/// <summary>Steps a second of the world's motion clock: fixed, so the decay and the carry do not depend on the frame rate.</summary>
		public const float StepRate = 30f;

		/// <summary>At most this many steps in one frame; a longer gap is caught up in one longer step.</summary>
		private const int MaxStepsPerFrame = 2;

		/// <summary>A gap in the clock longer than this, or a jump, starts the foam afresh.</summary>
		private const double ResetSeconds = 1.0;

		/// <summary>Seconds foam on still inland water takes to fade to about a third: rafts in an eddy or on a pool last.</summary>
		public static float InlandLingers = 10f;

		/// <summary>Seconds foam on broken inland water takes (a rapid, a fall's boil): churned, its bubbles burst within seconds.</summary>
		public static float InlandBrokenLingers = 3f;

		/// <summary>Seconds the sea's foam takes to fade to about a third: a white cap's patch breaks up within a few wave periods.</summary>
		public static float SeaLingers = 8f;

		/// <summary>Seconds the surf's foam takes: bubbles on churned water go fastest (WaterShore.WhitewaterLingers).</summary>
		public static float SurfLingers = 3.5f;

		/// <summary>Seconds foam stranded on the sand takes (WaterShore.FoamLingers).</summary>
		public static float SandLingers = 8f;

		/// <summary>Seconds new foam takes to age from froth toward lace.</summary>
		public static float FreshLingers = 1.5f;

		/// <summary>False to stop simulating: every surface falls back to its own stateless foam.</summary>
		public static bool Enabled = true;

		private static readonly int FieldNearId = Shader.PropertyToID("_FishFoamField0");
		private static readonly int FieldFarId = Shader.PropertyToID("_FishFoamField1");
		private static readonly int RectNearId = Shader.PropertyToID("_FishFoamFieldRect0");
		private static readonly int CurrentNearId = Shader.PropertyToID("_FishFoamFieldCurrent0");
		private static readonly int LaceNearId = Shader.PropertyToID("_FishFoamLace0");
		private static readonly int FieldTimeId = Shader.PropertyToID("_FishFoamFieldTime");
		private static readonly int LacePreviousId = Shader.PropertyToID("_LacePrevious");
		private static readonly int RectFarId = Shader.PropertyToID("_FishFoamFieldRect1");
		private static readonly int RasterId = Shader.PropertyToID("_FishFoamRaster");
		private static readonly int PreviousId = Shader.PropertyToID("_FoamPrevious");
		private static readonly int CurrentId = Shader.PropertyToID("_FoamCurrent");
		private static readonly int RectId = Shader.PropertyToID("_FoamRect");
		private static readonly int PreviousRectId = Shader.PropertyToID("_FoamPreviousRect");
		private static readonly int SizeId = Shader.PropertyToID("_FoamSize");
		private static readonly int StepId = Shader.PropertyToID("_FoamStep");
		private static readonly int StepBId = Shader.PropertyToID("_FoamStepB");
		private static readonly int StepCId = Shader.PropertyToID("_FoamStepC");
		private static readonly int SeaId = Shader.PropertyToID("_FoamSea");

		/// <summary>One window: the foam twice (read one, write the other) and the current drawn into it this step.</summary>
		private sealed class Window
		{
			public readonly float Texel;
			public RenderTexture A;
			public RenderTexture B;
			public RenderTexture Current;
			/// <summary>The near window's lace offsets twice (read one, write the other); null on the far window, which keeps none.</summary>
			public RenderTexture LaceA;
			public RenderTexture LaceB;
			public readonly bool KeepsLace;
			public bool InA = true;
			/// <summary>The window's corner in whole texels of the world.</summary>
			public Vector2Int Origin;
			/// <summary>Whether the foam texture being read holds a step's worth (false right after a reset).</summary>
			public bool Holding;
			public Vector4 Rect;
			public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
			public readonly MaterialPropertyBlock LaceProperties = new MaterialPropertyBlock();

			public Window(float texel, bool keepsLace)
			{
				Texel = texel;
				KeepsLace = keepsLace;
			}

			public RenderTexture Read => InA ? A : B;
			public RenderTexture Written => InA ? B : A;
			// The lace flips with the foam: written in the same step, read back with it.
			public RenderTexture LaceRead => InA ? LaceA : LaceB;
			public RenderTexture LaceWritten => InA ? LaceB : LaceA;
		}

		private static readonly Window[] windows = { new Window(NearTexelMetres, true), new Window(FarTexelMetres, false) };
		private static readonly List<Renderer> inland = new List<Renderer>();
		private static readonly Dictionary<Material, int> sourcePasses = new Dictionary<Material, int>();
		private static readonly HashSet<object> seas = new HashSet<object>();
		private static Vector4 seaFoam = new Vector4(0.7f, 0.28f, 0.6f, 0f);
		private static Shader shader;
		private static Material material;
		private static CommandBuffer commands;
		private static bool hooked;
		private static int frame = -1;
		private static double lastClock = double.NaN;
		private static double owed;
		private static uint seenJumps;
		private static bool published;
		/// <summary>The clock the last step ran at (the lace's renewal phases are read from it).</summary>
		private static float fieldTime;
		/// <summary>How much the sea and the shore make (1; 0 while <see cref="Simulate"/> warms the field with them frozen).</summary>
		private static float seaMaking = 1f;

		/// <summary>A component's reference to the step's shader, so a build includes it (found by name otherwise).</summary>
		public static void Offer(Shader stepShader)
		{
			if (stepShader != null && shader == null)
			{
				shader = stepShader;
			}
			Hook();
		}

		/// <summary>An inland water surface (a lake's or a river's renderer) to draw into the current maps.</summary>
		public static void AddInland(Renderer renderer)
		{
			if (renderer != null && !inland.Contains(renderer))
			{
				inland.Add(renderer);
			}
			Hook();
		}

		public static void RemoveInland(Renderer renderer)
		{
			inland.Remove(renderer);
		}

		/// <summary>A sea is in the scene (WaterSurface): the open water, the surf and the sand are simulated too.</summary>
		public static void AddSea(object sea)
		{
			if (sea != null)
			{
				seas.Add(sea);
			}
			Hook();
		}

		public static void RemoveSea(object sea)
		{
			seas.Remove(sea);
		}

		/// <summary>
		/// How the sea draws its white caps, so the foam is made where they are: x the white-cap threshold, y the crest
		/// curve's top, z how far the shallows lower the threshold, w the strong-wind share (FishWaterShading.hlsl).
		/// </summary>
		public static void SetSeaFoam(Vector4 foam)
		{
			seaFoam = foam;
		}

		private static void Hook()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			Application.quitting += Release;
#if UNITY_EDITOR
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
#endif
		}

		private static void Release()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			hooked = false;
			Unpublish();
			foreach (Window window in windows)
			{
				Free(ref window.A);
				Free(ref window.B);
				Free(ref window.Current);
				Free(ref window.LaceA);
				Free(ref window.LaceB);
				window.Holding = false;
			}
			if (material != null)
			{
				DestroySafely(material);
				material = null;
			}
			commands?.Release();
			commands = null;
			sourcePasses.Clear();
			lastClock = double.NaN;
		}

		private static void Unpublish()
		{
			published = false;
			Shader.SetGlobalVector(RectNearId, Vector4.zero);
			Shader.SetGlobalVector(RectFarId, Vector4.zero);
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			if (frame == Time.frameCount)
			{
				return;
			}
			frame = Time.frameCount;
			inland.RemoveAll(r => r == null);
			seas.RemoveWhere(s => s == null || (s is Object o && o == null));
			Camera eye = Eye(cameras);
			if (!Enabled || eye == null || (inland.Count == 0 && seas.Count == 0) || !EnsureResources())
			{
				if (published)
				{
					Unpublish();
				}
				return;
			}

			// The world's motion clock: the foam stops with the world, and starts afresh after a jump or a long gap.
			double now = WorldMotion.Seconds;
			uint jumps = WorldClock.Shared.Jumps;
			bool reset = double.IsNaN(lastClock) || jumps != seenJumps || now - lastClock > ResetSeconds || now < lastClock;
			seenJumps = jumps;
			if (reset)
			{
				owed = 0.0;
				foreach (Window window in windows)
				{
					window.Holding = false;
				}
			}
			else
			{
				owed += now - lastClock;
			}
			lastClock = now;

			Vector3 eyePosition = eye.transform.position;
			double stepSeconds = 1.0 / StepRate;
			int steps = reset ? 1 : Mathf.Min(MaxStepsPerFrame, (int)(owed / stepSeconds));
			if (steps > 0)
			{
				// What is owed past the steps this frame takes is caught up in the last of them, not dropped.
				float last = reset ? (float)stepSeconds : (float)(owed - (steps - 1) * stepSeconds);
				owed = 0.0;
				commands.Clear();
				for (int s = 0; s < steps; s++)
				{
					float dt = s == steps - 1 ? Mathf.Min(last, 0.25f) : (float)stepSeconds;
					foreach (Window window in windows)
					{
						Step(window, eyePosition, dt);
					}
				}
				Graphics.ExecuteCommandBuffer(commands);
			}
			Publish();
		}

		/// <summary>
		/// Runs the foam for <paramref name="seconds"/> of the world's time at once, centred on <paramref name="eye"/>, from
		/// nothing: for a probe or a test that draws single frames, where the foam would otherwise have had one step to
		/// build. The water stands still meanwhile (its sources as they are now); the foam is carried as it would be.
		/// Returns false when there is nothing to simulate or no shader for it.
		/// </summary>
		public static bool Simulate(Vector3 eye, float seconds)
		{
			inland.RemoveAll(r => r == null);
			seas.RemoveWhere(s => s == null || (s is Object o && o == null));
			if ((inland.Count == 0 && seas.Count == 0) || !EnsureResources())
			{
				Debug.LogWarning($"[Foam field] nothing to simulate: {inland.Count} inland surface(s), {seas.Count} sea(s), shader {(shader != null ? shader.name : "missing")}.");
				return false;
			}
			foreach (Window window in windows)
			{
				window.Holding = false;
			}
			int steps = Mathf.Max(1, Mathf.CeilToInt(seconds * StepRate));
			float dt = 1f / StepRate;
			for (int done = 0; done < steps;)
			{
				// In batches, so one command buffer never holds thousands of draws.
				commands.Clear();
				for (int k = 0; k < 30 && done < steps; k++, done++)
				{
					/* The sea's spectrum and the shore's swash stand still while this runs: a crest or a lip held in one
					 * place for the whole warm-up would pile its foam into a flat sheet there. They make foam for the
					 * last second only; the inland water's sources (standing waves, stones, falls) stand still anyway. */
					seaMaking = steps - done <= StepRate ? 1f : 0f;
					foreach (Window window in windows)
					{
						Step(window, eye, dt);
					}
				}
				Graphics.ExecuteCommandBuffer(commands);
			}
			seaMaking = 1f;
			Publish();
			Debug.Log($"[Foam field] simulated {seconds:0.#} s ({steps} steps) round ({eye.x:0}, {eye.z:0}): {inland.Count} inland surface(s), {seas.Count} sea(s). " +
				$"Near window: {Stats(windows[0].Current, 3, "made/s")}, {Stats(windows[0].Current, 2, "water")}, {Stats(windows[0].Read, 0, "foam")}; " +
				$"far: {Stats(windows[1].Read, 0, "foam")}.");
			// The frame's own step is not to start it afresh.
			frame = Time.frameCount;
			lastClock = WorldMotion.Seconds;
			seenJumps = WorldClock.Shared.Jumps;
			owed = 0.0;
			return true;
		}

		/// <summary>
		/// One channel of a window's texture read back (slow; for <see cref="Simulate"/>'s log only): the texels above
		/// zero, their mean and the largest.
		/// </summary>
		private static string Stats(RenderTexture texture, int channel, string what)
		{
			var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
			RenderTexture.active = previous;
			Color[] texels = copy.GetPixels();
			DestroySafely(copy);
			int above = 0;
			double sum = 0.0;
			float most = 0f;
			foreach (Color c in texels)
			{
				float v = c[channel];
				if (v > 1e-4f)
				{
					above++;
					sum += v;
					most = Mathf.Max(most, v);
				}
			}
			return $"{what} on {above} texels (mean {(above > 0 ? sum / above : 0.0):0.000}, most {most:0.000})";
		}

		/// <summary>The camera the windows centre on: the game's main camera, else the first that is not a reflection or a preview.</summary>
		private static Camera Eye(List<Camera> cameras)
		{
			// The game's camera whenever there is one, even in a frame the Scene view draws first.
			Camera main = Camera.main;
			if (main != null && main.isActiveAndEnabled)
			{
				return main;
			}
			foreach (Camera camera in cameras)
			{
				if (camera != null && camera.cameraType != CameraType.Reflection && camera.cameraType != CameraType.Preview)
				{
					return camera;
				}
			}
			return main;
		}

		private static void Step(Window window, Vector3 eye, float dt)
		{
			Vector4 previousRect = window.Rect;
			bool holding = window.Holding;
			window.Origin = new Vector2Int(
				Mathf.FloorToInt(eye.x / window.Texel) - Resolution / 2,
				Mathf.FloorToInt(eye.z / window.Texel) - Resolution / 2);
			float side = Resolution * window.Texel;
			window.Rect = new Vector4(window.Origin.x * window.Texel, window.Origin.y * window.Texel, side, window.Texel);
			var corner = new Vector2(window.Rect.x, window.Rect.y);

			// ── The inland water's current and sources, drawn from above ──
			commands.SetRenderTarget(window.Current);
			commands.ClearRenderTarget(false, true, Color.clear);
			commands.SetGlobalVector(RasterId, new Vector4(corner.x, corner.y, side, 0f));
			var area = new Bounds(new Vector3(corner.x + side * 0.5f, 0f, corner.y + side * 0.5f), new Vector3(side, 1e6f, side));
			foreach (Renderer renderer in inland)
			{
				if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.bounds.Intersects(area))
				{
					continue;
				}
				Material surface = renderer.sharedMaterial;
				int pass = SourcePass(surface);
				if (pass >= 0)
				{
					commands.DrawRenderer(renderer, surface, 0, pass);
				}
			}

			// ── The step ──
			MaterialPropertyBlock properties = window.Properties;
			properties.Clear();
			properties.SetTexture(PreviousId, window.Read);
			properties.SetTexture(CurrentId, window.Current);
			properties.SetVector(RectId, window.Rect);
			properties.SetVector(PreviousRectId, new Vector4(previousRect.x, previousRect.y, previousRect.z, holding ? 1f : 0f));
			properties.SetVector(SizeId, new Vector4(Resolution, Resolution, 1f / Resolution, 1f / Resolution));
			properties.SetVector(StepId, new Vector4(dt, Keep(dt, InlandLingers), Keep(dt, SeaLingers), Keep(dt, SandLingers)));
			float clockNow = WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds);
			properties.SetVector(StepBId, new Vector4(Keep(dt, FreshLingers), seas.Count > 0 ? 1f : 0f, Keep(dt, SurfLingers), clockNow));
			fieldTime = clockNow;
			properties.SetVector(StepCId, new Vector4(Keep(dt, InlandBrokenLingers), seaMaking, 0f, 0f));
			properties.SetVector(SeaId, seaFoam);
			commands.SetRenderTarget(window.Written);
			commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, properties);
			// The lace's offsets, carried by the same current (pass Lace): the near window only.
			if (window.KeepsLace)
			{
				MaterialPropertyBlock lace = window.LaceProperties;
				lace.Clear();
				lace.SetTexture(PreviousId, window.Read);
				lace.SetTexture(LacePreviousId, window.LaceRead);
				lace.SetTexture(CurrentId, window.Current);
				lace.SetVector(RectId, window.Rect);
				lace.SetVector(PreviousRectId, new Vector4(previousRect.x, previousRect.y, previousRect.z, holding ? 1f : 0f));
				lace.SetVector(SizeId, new Vector4(Resolution, Resolution, 1f / Resolution, 1f / Resolution));
				lace.SetVector(StepId, new Vector4(dt, 0f, 0f, 0f));
				lace.SetVector(StepBId, new Vector4(0f, seas.Count > 0 ? 1f : 0f, 0f, clockNow));
				lace.SetVector(StepCId, new Vector4(0f, seaMaking, 0f, 0f));
				lace.SetVector(SeaId, seaFoam);
				commands.SetRenderTarget(window.LaceWritten);
				commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, lace);
			}
			window.InA = !window.InA;
			window.Holding = true;
		}

		private static float Keep(float dt, float lingers) => Mathf.Exp(-dt / Mathf.Max(0.05f, lingers));

		private static void Publish()
		{
			Window near = windows[0], far = windows[1];
			Shader.SetGlobalTexture(FieldNearId, near.Read);
			// For the debug views: what the inland water drew into the near window this step.
			Shader.SetGlobalTexture(CurrentNearId, near.Current);
			Shader.SetGlobalTexture(FieldFarId, far.Read);
			Shader.SetGlobalTexture(LaceNearId, near.LaceRead);
			Shader.SetGlobalFloat(FieldTimeId, fieldTime);
			float live = near.Holding && far.Holding ? 1f : 0f;
			Shader.SetGlobalVector(RectNearId, new Vector4(near.Rect.x, near.Rect.y, near.Rect.z, live));
			Shader.SetGlobalVector(RectFarId, new Vector4(far.Rect.x, far.Rect.y, far.Rect.z, live));
			published = live > 0f;
		}

		/// <summary>The FoamSource pass of a surface's material, cached; −1 for a material without one.</summary>
		private static int SourcePass(Material surface)
		{
			if (surface == null)
			{
				return -1;
			}
			if (!sourcePasses.TryGetValue(surface, out int pass))
			{
				pass = surface.FindPass(SourcePassName);
				sourcePasses[surface] = pass;
			}
			return pass;
		}

		private static bool EnsureResources()
		{
			if (material == null)
			{
				if (shader == null)
				{
					shader = Shader.Find(ShaderName);
				}
				if (shader == null || !shader.isSupported)
				{
					return false;
				}
				material = new Material(shader) { name = "Foam field", hideFlags = HideFlags.HideAndDontSave };
			}
			commands ??= new CommandBuffer { name = "Foam field" };
			foreach (Window window in windows)
			{
				bool laceLost = window.KeepsLace && (window.LaceA == null || !window.LaceA.IsCreated() || !window.LaceB.IsCreated());
				if (window.A == null || !window.A.IsCreated() || !window.B.IsCreated() || !window.Current.IsCreated() || laceLost)
				{
					Free(ref window.A);
					Free(ref window.B);
					Free(ref window.Current);
					Free(ref window.LaceA);
					Free(ref window.LaceB);
					window.A = Create(FoamFormat(), FilterMode.Bilinear, "Foam field");
					window.B = Create(FoamFormat(), FilterMode.Bilinear, "Foam field");
					window.Current = Create(CurrentFormat(), FilterMode.Point, "Foam field current");
					if (window.KeepsLace)
					{
						// Offsets in metres up to tens of them: half float keeps a centimetre or so.
						window.LaceA = Create(CurrentFormat(), FilterMode.Bilinear, "Foam field lace");
						window.LaceB = Create(CurrentFormat(), FilterMode.Bilinear, "Foam field lace");
					}
					window.InA = true;
					window.Holding = false;
				}
			}
			return true;
		}

		private static GraphicsFormat FoamFormat()
		{
			return SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R16G16_SFloat
				: SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R32G32_SFloat
				: GraphicsFormat.R16G16B16A16_SFloat;
		}

		private static GraphicsFormat CurrentFormat()
		{
			return SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render) ? GraphicsFormat.R16G16B16A16_SFloat
				: GraphicsFormat.R32G32B32A32_SFloat;
		}

		private static RenderTexture Create(GraphicsFormat format, FilterMode filter, string name)
		{
			var texture = new RenderTexture(Resolution, Resolution, 0, format)
			{
				name = name,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = filter,
				useMipMap = false,
				hideFlags = HideFlags.HideAndDontSave,
			};
			texture.Create();
			// A new render texture's contents are undefined: the foam starts clean.
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			GL.Clear(false, true, Color.clear);
			RenderTexture.active = previous;
			return texture;
		}

		private static void Free(ref RenderTexture texture)
		{
			if (texture != null)
			{
				texture.Release();
				DestroySafely(texture);
				texture = null;
			}
		}

		private static void DestroySafely(Object o)
		{
			if (Application.isPlaying)
			{
				Object.Destroy(o);
			}
			else
			{
				Object.DestroyImmediate(o);
			}
		}
	}
}
