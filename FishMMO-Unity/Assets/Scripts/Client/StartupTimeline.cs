#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// What the client does between Play being pressed and the world running smoothly, as one report in the log: how long
	/// the freeze before the first frame lasted, when each scene arrived, how long the editor spent compiling shader
	/// variants (the cyan placeholders), every hitch with the scripts and loads that took its time, and where the whole
	/// start-up's time went. Editor only, off unless switched on (Dashboard → Maintenance → Diagnostics → Toggle start-up
	/// timeline, StartupTimelineToggle); never in batch mode (probes and tests).
	/// </summary>
	/// <remarks>
	/// It ends once the world has been quiet — no hitch, no scene arriving, no shader compiling — for
	/// <see cref="QuietSeconds"/>, or after <see cref="WindowSeconds"/> whatever happens. Timing comes from
	/// <see cref="ProfilerRecorder"/>s on every script, loading and rendering marker Unity knows of (picked up again as
	/// new ones appear), so a hitch names the behaviour or load that spent it without anything being instrumented.
	/// Search Editor.log for "[StartupTimeline]".
	/// </remarks>
	public sealed class StartupTimeline : MonoBehaviour
	{
		/// <summary>The longest the report watches for, seconds after the first frame.</summary>
		public const float WindowSeconds = 90f;
		/// <summary>How long the world must be quiet before the report ends.</summary>
		public const float QuietSeconds = 4f;
		/// <summary>A frame at least this long is a hitch.</summary>
		public const float HitchMilliseconds = 50f;
		/// <summary>Hitches listed one by one; the rest are only counted.</summary>
		private const int MaxHitchLines = 40;
		/// <summary>Markers named against each hitch, and in the whole start-up's summary.</summary>
		private const int TopPerHitch = 5;
		private const int TopOverall = 25;
		/// <summary>How often newly created markers are picked up.</summary>
		private const float RescanSeconds = 0.5f;

		/// <summary>EditorPrefs switch: true records a report on every Play. Off by default.</summary>
		public const string EnabledKey = "FishMMO.StartupTimeline.Enabled";

		/// <summary>Whether a report is recorded on Play (<see cref="EnabledKey"/>).</summary>
		public static bool Enabled
		{
			get => UnityEditor.EditorPrefs.GetBool(EnabledKey, false);
			set => UnityEditor.EditorPrefs.SetBool(EnabledKey, value);
		}

		private const string PressedKey = "FishMMO.StartupTimeline.Pressed";

		[UnityEditor.InitializeOnLoadMethod]
		private static void HookPlayButton()
		{
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
		}

		private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change == UnityEditor.PlayModeStateChange.ExitingEditMode && Enabled)
			{
				// Survives the domain reload that follows; the editor's clock runs straight through it.
				UnityEditor.SessionState.SetFloat(PressedKey, (float)UnityEditor.EditorApplication.timeSinceStartup);
			}
		}

		private struct Event
		{
			public double At;
			public string Text;
		}

		private struct Spent
		{
			public string Name;
			public double Milliseconds;
		}

		private readonly List<Event> events = new List<Event>();
		private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
		private readonly List<string> recorderNames = new List<string>();
		private readonly HashSet<string> watched = new HashSet<string>();
		private readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
		private double[] totals = new double[0];
		private readonly List<Spent> scratch = new List<Spent>();

		/// <summary>Seconds on <see cref="Clock"/> at which Play was pressed (the start of everything here).</summary>
		private double pressed;
		private double firstFrame = -1.0;
		private double lastNoise;
		private double nextRescan;
		private int frames;
		private int hitches;
		private int over33, over100;
		private double hitchSeconds;
		private int hitchLines;
		private bool compiling;
		private double compilingSince;
		private double compilingTotal;
		private int compilingFrames;
		private readonly List<float> lateFrames = new List<float>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Begin()
		{
			if (Application.isBatchMode || !Enabled)
			{
				return;
			}
			var host = new GameObject("Startup Timeline") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
			DontDestroyOnLoad(host);
			host.AddComponent<StartupTimeline>();
		}

		/// <summary>Seconds on the editor's clock, which runs straight through the domain reload.</summary>
		private static double Clock
		{
			get => UnityEditor.EditorApplication.timeSinceStartup;
		}

		private void Awake()
		{
			double now = Clock;
			pressed = now;
			float stored = UnityEditor.SessionState.GetFloat(PressedKey, -1f);
			if (stored >= 0f && stored <= now)
			{
				pressed = stored;
				Add(stored, "Play pressed");
			}
			UnityEditor.SessionState.EraseFloat(PressedKey);
			Add(now, "Domain loaded, before the first scene");
			SceneManager.sceneLoaded += OnSceneLoaded;
			Rescan();
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			for (int i = 0; i < recorders.Count; i++)
			{
				recorders[i].Dispose();
			}
			recorders.Clear();
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			double now = Clock;
			lastNoise = now;
			Add(now, $"Scene loaded: {scene.name} ({mode})");
		}

		private void Add(double at, string text)
		{
			events.Add(new Event { At = at, Text = text });
		}

		/// <summary>Starts a recorder on every timed script, loading and rendering marker not yet watched.</summary>
		private void Rescan()
		{
			handles.Clear();
			ProfilerRecorderHandle.GetAvailable(handles);
			for (int i = 0; i < handles.Count; i++)
			{
				ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(handles[i]);
				if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || watched.Contains(d.Name))
				{
					continue;
				}
				ProfilerCategory c = d.Category;
				if (c != ProfilerCategory.Scripts && c != ProfilerCategory.Loading && c != ProfilerCategory.Render
					&& c != ProfilerCategory.Memory && c != ProfilerCategory.Physics)
				{
					continue;
				}
				watched.Add(d.Name);
				recorders.Add(ProfilerRecorder.StartNew(c, d.Name, 1));
				recorderNames.Add(d.Name);
			}
			if (totals.Length < recorders.Count)
			{
				System.Array.Resize(ref totals, recorders.Count);
			}
		}

		/// <summary>
		/// Markers that only contain others (the player loop and its phases, the render loop round a camera): ranking by
		/// them names the frame, not what was in it.
		/// </summary>
		private static bool IsUmbrella(string name)
		{
			return name.StartsWith("PlayerLoop") || name.StartsWith("EditorLoop") || name.Contains("BehaviourUpdate")
				|| name.StartsWith("Update.Script") || name.StartsWith("PreLateUpdate.Script") || name.StartsWith("FixedUpdate.Script")
				|| name.StartsWith("PostLateUpdate.") || name.StartsWith("RenderPipelineManager.")
				|| name.StartsWith("UniversalRenderPipeline.Render") || name.StartsWith("Inl_UniversalRenderPipeline")
				|| name.StartsWith("Initialization.") || name.StartsWith("EarlyUpdate.") || name.StartsWith("PreUpdate.")
				|| name.StartsWith("Update.") || name.StartsWith("FixedUpdate.") || name == "Main Thread"
				|| name.StartsWith("Camera.Render") || name.StartsWith("Gfx.") || name.StartsWith("Semaphore.")
				|| name == "RenderLoop" || name.StartsWith("Inl_UniversalRenderTotal") || name.StartsWith("Inl_RenderPipeline.Begin")
				|| name.StartsWith("Inl_RenderCameraStack") || name == "ExecuteRenderGraph" || name.StartsWith("ReflectionProbes.")
				|| name.StartsWith("UIR.") || name.StartsWith("UIElementsUtility.") || name == "IMGUIContainer" || name == "OnGUI"
				|| name == "GameView.Render" || name.StartsWith("Inl_ScriptableRenderContext");
		}

		/// <summary>
		/// Frame-level counters the profiler files under these categories (whole-frame times, and resource counts whose
		/// unit is reported as time): not something a frame spent on one thing.
		/// </summary>
		private static bool IsCounter(string name)
		{
			return name.StartsWith("GfxResource.") || name.EndsWith("Frame Time") || name.StartsWith("CPU ") || name.StartsWith("GPU ");
		}

		private void Update()
		{
			double now = Clock;
			frames++;
			if (firstFrame < 0.0)
			{
				firstFrame = now;
				lastNoise = now;
				Add(now, "First frame");
			}

			// What the frame that just finished spent. Recorders hold the last finished frame's sample.
			float ms = Time.unscaledDeltaTime * 1000f;
			scratch.Clear();
			for (int i = 0; i < recorders.Count; i++)
			{
				ProfilerRecorder r = recorders[i];
				if (!r.Valid || r.Count == 0 || IsCounter(recorderNames[i]))
				{
					continue;
				}
				double spent = r.LastValue * 1e-6;
				if (spent <= 0.0)
				{
					continue;
				}
				totals[i] += spent;
				if (ms >= HitchMilliseconds && spent >= 1.0 && !IsUmbrella(recorderNames[i]))
				{
					scratch.Add(new Spent { Name = recorderNames[i], Milliseconds = spent });
				}
			}

			bool compilingNow = UnityEditor.ShaderUtil.anythingCompiling;
			if (compilingNow)
			{
				compilingFrames++;
				lastNoise = now;
			}
			if (compilingNow != compiling)
			{
				if (compilingNow)
				{
					compilingSince = now;
					Add(now, "Shaders compiling (placeholders on screen)");
				}
				else
				{
					compilingTotal += now - compilingSince;
					Add(now, $"Shaders compiled ({now - compilingSince:0.0} s)");
				}
				compiling = compilingNow;
			}

			if (ms >= 33.4f)
			{
				over33++;
			}
			if (ms >= 100f)
			{
				over100++;
			}
			if (ms >= HitchMilliseconds && frames > 1)
			{
				hitches++;
				hitchSeconds += ms * 0.001;
				lastNoise = now;
				if (hitchLines < MaxHitchLines)
				{
					hitchLines++;
					scratch.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
					var text = new StringBuilder();
					text.Append($"Hitch {ms:0} ms{(compilingNow ? " [compiling]" : string.Empty)}:");
					for (int i = 0; i < scratch.Count && i < TopPerHitch; i++)
					{
						text.Append($" {scratch[i].Name} {scratch[i].Milliseconds:0.0};");
					}
					Add(now, text.ToString());
				}
			}
			else if (now - firstFrame > 1.0)
			{
				lateFrames.Add(ms);
				if (lateFrames.Count > 600)
				{
					lateFrames.RemoveAt(0);
				}
			}

			if (now >= nextRescan)
			{
				nextRescan = now + RescanSeconds;
				Rescan();
			}

			bool quiet = now - lastNoise >= QuietSeconds;
			if (quiet || now - firstFrame >= WindowSeconds)
			{
				Report(now, quiet);
				Destroy(gameObject);
			}
		}

		private void Report(double now, bool quiet)
		{
			if (compiling)
			{
				compilingTotal += now - compilingSince;
			}
			var text = new StringBuilder();
			text.AppendLine("[StartupTimeline] Start-up report (seconds after Play was pressed)");
			text.AppendLine($"  Play → first frame: {firstFrame - pressed:0.00} s (domain reload, scene load, every Awake/Start).");
			double settled = (quiet ? lastNoise : now) - pressed;
			text.AppendLine(quiet
				? $"  Settled at {settled:0.0} s: the last hitch, scene or shader compile; quiet for {QuietSeconds:0} s after."
				: $"  NOT settled after {WindowSeconds:0} s of frames; the report stopped there.");
			text.AppendLine($"  Frames {frames}; ≥33 ms {over33}; ≥{HitchMilliseconds:0} ms {hitches} ({hitchSeconds:0.0} s in all); ≥100 ms {over100}.");
			text.AppendLine($"  Shader compiling: {compilingTotal:0.0} s over {compilingFrames} frames.");
			if (lateFrames.Count > 0)
			{
				var sorted = new List<float>(lateFrames);
				sorted.Sort();
				text.AppendLine($"  Frame time, non-hitch frames at the end: median {sorted[sorted.Count / 2]:0.0} ms, 90th {sorted[(int)(sorted.Count * 0.9f)]:0.0} ms.");
			}
			text.AppendLine("  Timeline:");
			for (int i = 0; i < events.Count; i++)
			{
				text.AppendLine($"    {events[i].At - pressed,7:0.00}  {events[i].Text}");
			}
			if (hitches > hitchLines)
			{
				text.AppendLine($"    … and {hitches - hitchLines} more hitches.");
			}
			text.AppendLine($"  Where the time went from the first frame on (main-thread markers, ms summed; nested markers overlap):");
			scratch.Clear();
			for (int i = 0; i < recorderNames.Count && i < totals.Length; i++)
			{
				if (totals[i] >= 1.0 && !IsUmbrella(recorderNames[i]))
				{
					scratch.Add(new Spent { Name = recorderNames[i], Milliseconds = totals[i] });
				}
			}
			scratch.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
			for (int i = 0; i < scratch.Count && i < TopOverall; i++)
			{
				text.AppendLine($"    {scratch[i].Milliseconds,9:0.0}  {scratch[i].Name}");
			}
			Debug.Log(text.ToString());
		}
	}
}
#endif
