#if !UNITY_SERVER
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// Records every pipeline state the client draws with (<see cref="GraphicsStateCollection.BeginTrace"/>), so the release
	/// build can create them all behind a loading screen (<see cref="GraphicsStateWarmup"/>) instead of on the frame that first
	/// draws them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> On the web a pipeline is built by the browser's GPU process the first time it is drawn with, about 110 ms
	/// each on a fast PC (a Chrome trace of the boot: 72 render pipelines, 8 s), and the game's main thread waits behind it.
	/// Those were the multi-second frames of the boot and the freeze entering character select.
	/// </para>
	/// <para>
	/// <b>Recording.</b> Unity traces in development builds only. Open a development web build with <c>?psotrace</c> in the URL
	/// (a player: <c>-psotrace</c>), play through everything worth warming (login, character select, every world, fights,
	/// weather), then save: in the browser console <c>fishMMOSaveGraphicsStates()</c>, which downloads the file; a desktop
	/// player saves on quit too, into its persistent data folder. Saving again downloads everything recorded so far. Then
	/// FishMMO → Graphics States → Add Recording… merges it into the asset the build ships (one per platform, graphics
	/// API and quality level, addressable beside the shaders it names; see <see cref="GraphicsStateWarmup"/>).
	/// </para>
	/// </remarks>
	public sealed class GraphicsStateTrace : MonoBehaviour
	{
		/// <summary>The host object's name: the page calls <c>SendMessage(HostName, "Save")</c>.</summary>
		public const string HostName = "Graphics State Trace";

		private static GraphicsStateCollection collection;
		private static GraphicsStateTrace instance;

		/// <summary>True when this run records (a development build asked by URL or command line).</summary>
		public static bool Active => collection != null;

		/// <summary>True when this run was asked to record: <c>?psotrace</c> on the page, <c>-psotrace</c> on a player.</summary>
		public static bool Requested
		{
			get
			{
				foreach (string arg in Environment.GetCommandLineArgs())
				{
					if (string.Equals(arg, "-psotrace", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				string url = Application.absoluteURL;
				return !string.IsNullOrEmpty(url) && url.IndexOf("psotrace", StringComparison.OrdinalIgnoreCase) >= 0;
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			collection = null;
			instance = null;
		}

		/// <summary>
		/// Development builds: <c>?psoquality=High%20Fidelity</c> on the page (a player: <c>-psoquality "High Fidelity"</c>)
		/// starts at that quality level, as a fresh profile with nothing saved would not, to record or check that level's
		/// states. A level the player saved still wins (ClientDisplaySettings.ApplySaved runs after this).
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void ApplyRequestedQuality()
		{
			if (!Debug.isDebugBuild || !Application.isPlaying)
			{
				return;
			}
			string wanted = null;
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (string.Equals(args[i], "-psoquality", StringComparison.OrdinalIgnoreCase))
				{
					wanted = args[i + 1];
				}
			}
			string url = Application.absoluteURL;
			int at = string.IsNullOrEmpty(url) ? -1 : url.IndexOf("psoquality=", StringComparison.OrdinalIgnoreCase);
			if (at >= 0)
			{
				string value = url.Substring(at + "psoquality=".Length);
				int end = value.IndexOfAny(new[] { '&', '#' });
				wanted = Uri.UnescapeDataString(end >= 0 ? value.Substring(0, end) : value).Replace('+', ' ');
			}
			if (string.IsNullOrEmpty(wanted))
			{
				return;
			}
			string[] names = QualitySettings.names;
			for (int i = 0; i < names.Length; i++)
			{
				if (string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase))
				{
					ClientDisplaySettings.ApplyQualityLevel(i, false);
					Debug.Log($"[Graphics states] quality level {names[i]} (psoquality).");
					return;
				}
			}
			Debug.LogWarning($"[Graphics states] psoquality: no quality level '{wanted}'.");
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Hook()
		{
			if (instance != null || !Application.isPlaying || !Requested)
			{
				return;
			}
			if (!Debug.isDebugBuild)
			{
				Debug.LogWarning("[Graphics states] ?psotrace asks for a recording, but Unity records pipeline states only in development builds.");
				return;
			}
			var traced = new GraphicsStateCollection();
			if (!traced.BeginTrace())
			{
				Debug.LogWarning($"[Graphics states] Unity would not start a trace on {SystemInfo.graphicsDeviceType}.");
				return;
			}
			collection = traced;
			var host = new GameObject(HostName);
			DontDestroyOnLoad(host);
			instance = host.AddComponent<GraphicsStateTrace>();
			Debug.Log($"[Graphics states] recording on {SystemInfo.graphicsDeviceType} ({Application.platform}); save with fishMMOSaveGraphicsStates() in the browser console, or quit a player.");
		}

		/// <summary>
		/// Saves everything recorded so far and keeps recording. The web downloads the file (named after
		/// <see cref="GraphicsStateWarmup.CurrentName"/>); a player writes it to its persistent data folder.
		/// </summary>
		public void Save()
		{
			if (collection == null)
			{
				return;
			}
			try
			{
				collection.EndTrace();
				string name = GraphicsStateWarmup.CurrentName + ".json";
				string path = Application.platform == RuntimePlatform.WebGLPlayer ? name : Path.Combine(Application.persistentDataPath, name);
				bool saved = collection.SaveToFile(path);
				Debug.Log(saved
					? $"[Graphics states] saved {collection.variantCount} shader variants, {collection.totalGraphicsStateCount} states to {path}."
					: $"[Graphics states] saving to {path} failed.");
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				collection.BeginTrace();
			}
		}

		private void OnApplicationQuit()
		{
			if (Application.platform != RuntimePlatform.WebGLPlayer)
			{
				Save();
			}
		}
	}
}
#endif
