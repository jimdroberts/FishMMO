#if !UNITY_SERVER
using System.Collections.Generic;
using System.Diagnostics;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FishMMO.Client
{
	/// <summary>
	/// Creates, behind the first loading screen, every pipeline state a recording says the client draws with
	/// (<see cref="GraphicsStateTrace"/>), so no frame of play stops to build one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> On the web a pipeline is built by the browser's GPU process the first time it is drawn with, about
	/// 110 ms each on a fast PC, and the game's main thread waits behind it: the multi-second frames of the boot and the
	/// freeze entering character select.
	/// </para>
	/// <para>
	/// <b>The recording.</b> A <see cref="GraphicsStateCollection"/> asset per platform, graphics API and quality level,
	/// addressable at <see cref="AddressFor"/> in the resident client group beside the shaders it names (put there by the
	/// editor's FishMMO → Graphics States → Add Recording…). Loading it brings its shaders with it, so one pass creates every
	/// state it holds. None for this platform, API and level: nothing is warmed, and draws build their states as they come.
	/// </para>
	/// <para>
	/// <b>When.</b> During the boot: the recording is loaded as soon as the first scene after the bootstrap has loaded
	/// (Addressables are set up by then; the asset arrives with the resident client bundle) and warmed straight away, before
	/// the login backdrop has drawn a frame, so its draws find their states made. The boot's loading screen is up throughout,
	/// and the warm-up holds it (<see cref="SceneContentLoads.AddWork"/>) until done. After the boot only behind a loading
	/// screen (<see cref="SceneContentLoads.Covered"/>): a new quality level loads its own recording and warms at the next one.
	/// Slices of <see cref="SliceMilliseconds"/> a frame keep the bar moving; on the web each creation is synchronous (no
	/// parallel creation there) and the browser builds the pipeline after the call returns, its GPU process working through
	/// them behind the same screen.
	/// </para>
	/// <para>
	/// <b>Checking a recording.</b> A development build traces the states drawn that the warm-up had not created and logs
	/// them every <see cref="MissReportSeconds"/> s ("[Graphics warm-up] … drawn that the warm-up had not created"): what to
	/// record next. <c>fishMMOSaveGraphicsStateMisses()</c> in the browser console downloads them as a recording, which
	/// Add Recording… merges in like any other.
	/// </para>
	/// </remarks>
	public sealed class GraphicsStateWarmup : MonoBehaviour
	{
		/// <summary>Time spent creating states each frame of the warm-up.</summary>
		public const float SliceMilliseconds = 25f;

		/// <summary>How often a development build reports the states drawn that the warm-up had not created.</summary>
		public const float MissReportSeconds = 30f;

		/// <summary>States asked for in one call.</summary>
		private const int Step = 2;

		/// <summary>The address prefix of the recordings.</summary>
		public const string AddressPrefix = "GraphicsStates/";

		/// <summary>A recording's name from its platform, graphics API and quality level: e.g. <c>WebGLPlayer-WebGPU-Performant</c>.</summary>
		public static string NameFor(string platform, string api, string quality) => $"{platform}-{api}-{quality}";

		/// <summary>The address of the recording named <paramref name="name"/>.</summary>
		public static string AddressFor(string name) => AddressPrefix + name;

		/// <summary>
		/// This run's recording: the platform's, the graphics API's and the current quality level's. The level is part of it
		/// because its settings (anti-aliasing, the colour and depth formats) are part of every state.
		/// </summary>
		public static string CurrentName => NameFor(Application.platform.ToString(), SystemInfo.graphicsDeviceType.ToString(), QualitySettings.names[QualitySettings.GetQualityLevel()]);

		private static GraphicsStateWarmup instance;
		private static readonly List<GraphicsStateCollection.ShaderVariant> variants = new List<GraphicsStateCollection.ShaderVariant>();

		private SceneContentLoads.Work work;
		private int quality = -1;
		private bool loading;
		private AsyncOperationHandle<GraphicsStateCollection> handle;
		private GraphicsStateCollection collection;
		private bool warmed;
		private int idleCalls;
		private float started = -1f;
		private float nextMissReport;
		private int missesReported;
		private bool bootDone;
		private bool wasCovered;
		private bool sceneSeen;

		/// <summary>The host object's name: the page calls <c>SendMessage(HostName, "SaveMisses")</c>.</summary>
		public const string HostName = "Graphics State Warm-up";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			instance = null;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (instance != null || !Application.isPlaying || GraphicsStateTrace.Requested)
			{
				// A recording run must see every state at its first use, not created ahead of it.
				return;
			}
			var host = new GameObject(HostName) { hideFlags = HideFlags.DontSave };
			DontDestroyOnLoad(host);
			instance = host.AddComponent<GraphicsStateWarmup>();
		}

		private void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		/// <summary>The first scene after the bootstrap: Addressables are set up, and the recording can load.</summary>
		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			sceneSeen = true;
		}

		private void Start()
		{
			work = SceneContentLoads.AddWork("graphics", () => loading || (collection != null && !warmed), Progress);
		}

		/// <summary>The share of the recording's states created, or 1.</summary>
		private float Progress()
		{
			if (collection == null || warmed)
			{
				return loading ? 0f : 1f;
			}
			int total = collection.totalGraphicsStateCount;
			return total > 0 ? Mathf.Clamp01((float)collection.completedWarmupCount / total) : 1f;
		}

		private void Update()
		{
			bool covered = SceneContentLoads.Covered;
			if (wasCovered && !covered)
			{
				// The first loading screen has lifted: the boot is over, and from here only a loading screen hides the work.
				bootDone = true;
			}
			wasCovered = covered;
			bool hidden = covered || !bootDone;
			int level = QualitySettings.GetQualityLevel();
			// Once Addressables are set up (the boot), and at the next loading screen after the player picks another quality
			// level: its states are other states.
			if (level != quality && sceneSeen && hidden)
			{
				quality = level;
				Load(CurrentName);
				return;
			}
			ReportMisses();
			if (collection == null || warmed)
			{
				return;
			}
			// During the boot or behind a loading screen, never over play.
			if (!hidden)
			{
				return;
			}
			if (started < 0f)
			{
				started = Time.realtimeSinceStartup;
			}
			var clock = Stopwatch.StartNew();
			while (!collection.isWarmedUp && clock.Elapsed.TotalMilliseconds < SliceMilliseconds)
			{
				int before = collection.completedWarmupCount;
				// A development build traces what is drawn that the warm-up did not create (ReportMisses).
				collection.WarmUpProgressively(Step, default, Debug.isDebugBuild && !collection.isTracingCacheMisses).Complete();
				if (collection.completedWarmupCount == before)
				{
					idleCalls++;
					break;
				}
			}
			if (collection.isWarmedUp || idleCalls >= 3)
			{
				Finish();
			}
		}

		/// <summary>Loads the recording named <paramref name="name"/>, if this build has one.</summary>
		private void Load(string name)
		{
			Unload();
			loading = true;
			int level = quality;
			string address = AddressFor(name);
			Addressables.LoadResourceLocationsAsync(address, typeof(GraphicsStateCollection)).Completed += found =>
			{
				IList<IResourceLocation> locations = found.Status == AsyncOperationStatus.Succeeded ? found.Result : null;
				bool none = locations == null || locations.Count == 0;
				if (none || level != quality)
				{
					if (none)
					{
						Debug.Log($"[Graphics warm-up] no recording {name}; nothing to warm.");
					}
					Addressables.Release(found);
					if (level == quality)
					{
						loading = false;
					}
					return;
				}
				AsyncOperationHandle<GraphicsStateCollection> load = Addressables.LoadAssetAsync<GraphicsStateCollection>(locations[0]);
				Addressables.Release(found);
				handle = load;
				load.Completed += done =>
				{
					if (level != quality)
					{
						// Superseded by another quality level's recording (Unload released this one).
						return;
					}
					loading = false;
					if (done.Status != AsyncOperationStatus.Succeeded || done.Result == null)
					{
						Debug.LogWarning($"[Graphics warm-up] {address} would not load; nothing warmed.");
						return;
					}
					collection = done.Result;
					warmed = collection.isWarmedUp;
					idleCalls = 0;
					started = -1f;
					Debug.Log($"[Graphics warm-up] recording {name}: {collection.variantCount} shader variants, {collection.totalGraphicsStateCount} states.");
				};
			};
		}

		private void Finish()
		{
			warmed = true;
			variants.Clear();
			collection.GetVariants(variants);
			int resolved = 0;
			foreach (GraphicsStateCollection.ShaderVariant variant in variants)
			{
				if (variant.shader != null)
				{
					resolved++;
				}
			}
			Debug.Log($"[Graphics warm-up] {collection.completedWarmupCount}/{collection.totalGraphicsStateCount} states of {collection.variantCount} variants ({resolved} with their shader) created in {Time.realtimeSinceStartup - started:0.0} s.");
			nextMissReport = Time.realtimeSinceStartup + MissReportSeconds;
			missesReported = 0;
		}

		/// <summary>
		/// A development build's report of the states drawn since the warm-up that it had not created: what the recording is
		/// missing (or, when everything drawn shows here, a warm-up that did nothing).
		/// </summary>
		private void ReportMisses()
		{
			if (!warmed || collection == null || Time.realtimeSinceStartup < nextMissReport)
			{
				return;
			}
			nextMissReport = Time.realtimeSinceStartup + MissReportSeconds;
			GraphicsStateCollection misses = collection.cacheMissCollection;
			int count = misses != null ? misses.totalGraphicsStateCount : 0;
			if (count == missesReported)
			{
				return;
			}
			missesReported = count;
			variants.Clear();
			misses.GetVariants(variants);
			var names = new List<string>();
			foreach (GraphicsStateCollection.ShaderVariant variant in variants)
			{
				string name = variant.shader != null ? variant.shader.name : "?";
				if (!names.Contains(name) && names.Count < 16)
				{
					names.Add(name);
				}
			}
			Debug.Log($"[Graphics warm-up] {count} states of {misses.variantCount} variants drawn that the warm-up had not created: {string.Join(", ", names)}.");
		}

		/// <summary>
		/// Development builds: downloads the states drawn that the warm-up had not created, as a recording (the page's
		/// <c>fishMMOSaveGraphicsStateMisses()</c>).
		/// </summary>
		public void SaveMisses()
		{
			GraphicsStateCollection misses = collection != null ? collection.cacheMissCollection : null;
			if (!Debug.isDebugBuild || misses == null)
			{
				Debug.Log("[Graphics warm-up] no misses to save (a development build traces them once the warm-up has run).");
				return;
			}
			string name = CurrentName + "-misses.json";
			bool saved = misses.SaveToFile(Application.platform == RuntimePlatform.WebGLPlayer ? name : System.IO.Path.Combine(Application.persistentDataPath, name));
			Debug.Log($"[Graphics warm-up] {(saved ? "saved" : "could not save")} {misses.totalGraphicsStateCount} missed states as {name}.");
		}

		/// <summary>Drops the loaded recording (the states it created stay in Unity's cache).</summary>
		private void Unload()
		{
			if (collection != null && collection.isTracingCacheMisses)
			{
				collection.cacheMissCollection?.EndTrace();
			}
			collection = null;
			warmed = false;
			if (handle.IsValid())
			{
				Addressables.Release(handle);
			}
			handle = default;
			loading = false;
		}

		private void OnDestroy()
		{
			Unload();
		}
	}
}
#endif
