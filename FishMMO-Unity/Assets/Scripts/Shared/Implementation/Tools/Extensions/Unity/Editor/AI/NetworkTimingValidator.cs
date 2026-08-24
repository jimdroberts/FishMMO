using System.Collections.Generic;
using System.Text;
using FishNet.Managing;
using FishNet.Managing.Timing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Checks that every scene's <see cref="NetworkManager"/> agrees on network timing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Tick rate is not synchronised by FishNet.</b> <see cref="TimeManager.SetTickRate"/> says
	/// so explicitly: it is a per-scene serialized value that must be set identically on the client
	/// and on every server independently. Nothing at runtime checks that they match.
	/// </para>
	/// <para>
	/// A mismatch does not throw, log, or refuse the connection. The client simply simulates on a
	/// different timeline to the server, so every predicted input reconciles against a tick that
	/// means something else — movement rubber-bands, abilities fire at the wrong moment, and the
	/// symptoms look like network latency rather than a one-character config error. Editing the
	/// tick rate in one scene and forgetting the other three is an easy mistake to make and a very
	/// hard one to diagnose, so it is worth catching at edit time.
	/// </para>
	/// <para>
	/// Frame rate is deliberately <em>not</em> checked across scenes: client and server frame rates
	/// are independent by design and are expected to differ. The only frame-rate rule that matters
	/// is per-machine — it must be at least the tick rate — and that is checked here too.
	/// </para>
	/// </remarks>
	public static class NetworkTimingValidator
	{
		/// <summary>Log category.</summary>
		private const string LOG = "NetworkTimingValidator";

		/// <summary>
		/// One scene's network timing configuration.
		/// </summary>
		private struct TimingConfig
		{
			/// <summary>Scene asset path.</summary>
			public string ScenePath;

			/// <summary>The scene's configured tick rate.</summary>
			public ushort TickRate;

			/// <summary>Whether the client manager overwrites the render frame rate.</summary>
			public bool ClientChangesFrameRate;

			/// <summary>The client manager's frame rate cap.</summary>
			public ushort ClientFrameRate;

			/// <summary>Whether the server manager overwrites the frame rate.</summary>
			public bool ServerChangesFrameRate;

			/// <summary>The server manager's frame rate cap.</summary>
			public ushort ServerFrameRate;
		}

		/// <summary>
		/// Reports any disagreement in network timing across the project's scenes.
		/// </summary>
		[MenuItem("FishMMO/Validate Network Timing", priority = 210)]
		public static void ValidateNetworkTiming()
		{
			List<TimingConfig> configs = CollectConfigs();

			if (configs.Count == 0)
			{
				Debug.Log($"[{LOG}] No scenes with a NetworkManager were found.");
				return;
			}

			StringBuilder report = new StringBuilder();
			int problems = 0;

			// --- Every scene must agree on the tick rate. ---
			ushort reference = configs[0].TickRate;
			for (int i = 1; i < configs.Count; ++i)
			{
				if (configs[i].TickRate != reference)
				{
					problems++;
					report.AppendLine(
						$"  Tick rate mismatch: '{configs[0].ScenePath}' is {reference} Hz but " +
						$"'{configs[i].ScenePath}' is {configs[i].TickRate} Hz. FishNet does not " +
						"synchronise this — the client and server will simulate on different " +
						"timelines and prediction will silently misbehave.");
				}
			}

			// --- Each scene's own frame rate must clear its own tick rate. ---
			foreach (TimingConfig config in configs)
			{
				if (config.ClientChangesFrameRate && config.ClientFrameRate < config.TickRate)
				{
					problems++;
					report.AppendLine(
						$"  '{config.ScenePath}': ClientManager caps the frame rate at " +
						$"{config.ClientFrameRate} but the tick rate is {config.TickRate}. Ticks " +
						"cannot keep up with a frame rate below the tick rate.");
				}

				if (config.ServerChangesFrameRate && config.ServerFrameRate < config.TickRate)
				{
					problems++;
					report.AppendLine(
						$"  '{config.ScenePath}': ServerManager caps the frame rate at " +
						$"{config.ServerFrameRate} but the tick rate is {config.TickRate}.");
				}
			}

			StringBuilder summary = new StringBuilder();
			summary.AppendLine($"[{LOG}] Checked {configs.Count} scene(s). Tick rate: {reference} Hz.");
			foreach (TimingConfig config in configs)
			{
				summary.AppendLine(
					$"  {config.ScenePath}: tick {config.TickRate} Hz | " +
					$"client fps {(config.ClientChangesFrameRate ? config.ClientFrameRate.ToString() : "unmanaged")} | " +
					$"server fps {(config.ServerChangesFrameRate ? config.ServerFrameRate.ToString() : "unmanaged")}");
			}

			if (problems == 0)
			{
				Debug.Log(summary.ToString());
				return;
			}

			Debug.LogWarning(summary + "\n" + problems + " problem(s):\n" + report);
		}

		/// <summary>
		/// Loads every scene in the build-relevant set and reads its network timing.
		/// </summary>
		/// <returns>One entry per scene that has a NetworkManager.</returns>
		private static List<TimingConfig> CollectConfigs()
		{
			List<TimingConfig> configs = new List<TimingConfig>();

			foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);

				// Only project scenes; skip anything vendored under Plugins or Packages.
				if (!path.StartsWith("Assets/Scenes/", System.StringComparison.Ordinal))
				{
					continue;
				}

				Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
				try
				{
					if (TryReadConfig(scene, path, out TimingConfig config))
					{
						configs.Add(config);
					}
				}
				finally
				{
					EditorSceneManager.CloseScene(scene, removeScene: true);
				}
			}

			return configs;
		}

		/// <summary>
		/// Reads the network timing from a loaded scene.
		/// </summary>
		/// <param name="scene">The open scene.</param>
		/// <param name="path">The scene's asset path.</param>
		/// <param name="config">The configuration read.</param>
		/// <returns>True if the scene contains a NetworkManager.</returns>
		private static bool TryReadConfig(Scene scene, string path, out TimingConfig config)
		{
			config = default;

			if (!scene.IsValid())
			{
				return false;
			}

			foreach (GameObject root in scene.GetRootGameObjects())
			{
				NetworkManager manager = root.GetComponentInChildren<NetworkManager>(includeInactive: true);
				if (manager == null)
				{
					continue;
				}

				TimeManager timeManager = manager.GetComponentInChildren<TimeManager>(includeInactive: true);
				if (timeManager == null)
				{
					continue;
				}

				config.ScenePath = path;
				config.TickRate = timeManager.TickRate;

				ReadFrameRate(manager, "ClientManager", out config.ClientChangesFrameRate, out config.ClientFrameRate);
				ReadFrameRate(manager, "ServerManager", out config.ServerChangesFrameRate, out config.ServerFrameRate);

				return true;
			}

			return false;
		}

		/// <summary>
		/// Reads a manager's frame-rate settings through serialized properties.
		/// </summary>
		/// <remarks>
		/// The fields are private and the public accessors are <c>internal</c> to FishNet, so
		/// SerializedObject is the only way to read them from outside the assembly without editing
		/// vendored code — which would be lost on the next FishNet upgrade.
		/// </remarks>
		/// <param name="manager">The network manager to read from.</param>
		/// <param name="componentName">"ClientManager" or "ServerManager".</param>
		/// <param name="changesFrameRate">Whether the manager overwrites the frame rate.</param>
		/// <param name="frameRate">The configured cap.</param>
		private static void ReadFrameRate(NetworkManager manager, string componentName,
			out bool changesFrameRate, out ushort frameRate)
		{
			changesFrameRate = false;
			frameRate = 0;

			Component component = null;
			foreach (Component candidate in manager.GetComponentsInChildren<Component>(includeInactive: true))
			{
				if (candidate != null && candidate.GetType().Name == componentName)
				{
					component = candidate;
					break;
				}
			}

			if (component == null)
			{
				return;
			}

			SerializedObject serialized = new SerializedObject(component);

			SerializedProperty changeProperty = serialized.FindProperty("_changeFrameRate");
			SerializedProperty rateProperty = serialized.FindProperty("_frameRate");

			if (changeProperty != null) changesFrameRate = changeProperty.boolValue;
			if (rateProperty != null) frameRate = (ushort)rateProperty.intValue;
		}
	}
}
