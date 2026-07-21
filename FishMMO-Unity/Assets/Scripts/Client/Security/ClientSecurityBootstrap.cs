using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Logging;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// Initialises <see cref="ClientCertificatePinning"/> before any scene loads
	/// or <c>UnityWebRequest</c> is dispatched. Pin sources, in order of
	/// precedence:
	///
	///   1. <c>StreamingAssets/client-security.json</c>
	///      <c>{ "pins": ["...", "..."], "allowOnEmpty": false }</c>
	///   2. Compile-time defaults in <see cref="defaultPins"/>.
	///
	/// On WebGL the OS / browser performs TLS validation already, but the cert
	/// handler is still attached to <c>UnityWebRequest</c> so the pinning code
	/// path runs there too — leave <c>allowOnEmpty=true</c> for WebGL until pins
	/// are provisioned.
	/// </summary>
	public static class ClientSecurityBootstrap
	{
		private const string logChannel = "ClientSecurityBootstrap";
		private const string configFileName = "client-security.json";

		/// <summary>
		/// File name expected under StreamingAssets for client security configuration.
		/// </summary>
		public const string StreamingAssetsConfigFileName = configFileName;

		/// <summary>
		/// Compile-time fallback pins (base64 SHA-256(SPKI)). Populate these
		/// with the SPKI hashes of <c>api.fishmmo.com</c> + at least one backup
		/// key before shipping a release build.
		/// </summary>
		private static readonly string[] defaultPins = Array.Empty<string>();

		/// <summary>
		/// Number of compile-time fallback certificate pins configured for release-build validation.
		/// </summary>
		public static int DefaultPinCount => defaultPins.Length;

		/// <summary>
		/// Whether to fall back to "temporal validity only" when no pins are
		/// configured. Defaults to <c>true</c> in editor / development builds
		/// so unconfigured workstations still function, and <c>false</c> in
		/// release builds (fail-closed).
		/// </summary>
		private static bool DefaultAllowOnEmpty
		{
			get
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				return true;
#else
				return false;
#endif
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Initialize()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			// On Android, streaming assets are inside the APK/AAB and must be loaded
			// via UnityWebRequest, which requires main-thread yielding.  A blocking
			// spin-wait here would cause an ANR (Android) or browser-fetch deadlock
			// (WebGL).  We defer to a coroutine-driven non-blocking load instead, and
			// fall through to compile-time defaults synchronously only if the coroutine
			// helper is unavailable.
			if (CoroutineHelper.IsAvailable)
			{
				CoroutineHelper.Start(LoadFromStreamingAssetsCoroutine());
				// Fall through to synchronous defaults below — the coroutine will
				// call ClientCertificatePinning.Configure() a second time with the
				// loaded pins once the UnityWebRequest completes.
			}
#else
			try
			{
				if (TryLoadFromStreamingAssets(out var pins, out var allowOnEmpty))
				{
					ClientCertificatePinning.Configure(pins, allowOnEmpty);
					Log.Debug(logChannel,
						$"Loaded {pins.Count} pin(s) from StreamingAssets/{configFileName}.");

					// Warn if pins are empty and allowOnEmpty is false in a release build.
					if (pins.Count == 0 && !allowOnEmpty && !UnityEngine.Debug.isDebugBuild)
					{
						Log.Error(logChannel,
							"StreamingAssets/" + configFileName + " has allowOnEmpty=false but no pins configured. " +
							"Every HTTPS API call will be rejected. Add at least one SPKI pin to the config.");
					}
					return;
				}
			}
			catch (Exception ex)
			{
				Log.Warning(logChannel,
					$"Failed to load {configFileName} from StreamingAssets: {ex.Message}. " +
					"Falling back to compile-time defaults.");
			}
#endif

			ClientCertificatePinning.Configure(defaultPins, DefaultAllowOnEmpty);
			if (defaultPins.Length == 0)
			{
				if (DefaultAllowOnEmpty)
				{
					// Editor/dev builds: loud, repeated warning. The actual MITM-vulnerable
					// configuration is the intended behavior here (developers need to talk to
					// localhost / unsigned staging endpoints) but it must never be the
					// posture of a shipped client.
					Log.Warning(logChannel,
						"================================================================\n" +
						"  TLS CERTIFICATE PINNING IS DISABLED (development build).\n" +
						"  Any HTTPS endpoint with a temporally-valid certificate will be\n" +
						"  accepted. DO NOT ship a build in this state. Configure pins in\n" +
						"  StreamingAssets/" + configFileName + " or defaultPins[] before release.\n" +
						"================================================================");
				}
				else
				{
					// Release build with no pins: fail closed. Every HTTPS request below
					// will reject; the louder error here exists so QA notices in logs
					// rather than seeing only opaque "request failed" messages later.
					Log.Error(logChannel,
						"RELEASE BUILD SHIPPED WITHOUT TLS PINS. allowOnEmpty=false will reject " +
						"every HTTPS API call. Populate defaultPins[] (recommended: 2+ keys for " +
						"rotation) or ship a valid StreamingAssets/" + configFileName + ".");
				}
			}
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		/// <summary>
		/// Non-blocking coroutine that loads client-security.json via UnityWebRequest
		/// on platforms where streaming assets are not directly filesystem-accessible.
		/// On completion, calls <see cref="ClientCertificatePinning.Configure"/> with
		/// the loaded pins, or falls through to the compile-time defaults on failure.
		/// </summary>
		private static System.Collections.IEnumerator LoadFromStreamingAssetsCoroutine()
		{
			string path = System.IO.Path.Combine(Application.streamingAssetsPath, configFileName);
			using (var request = UnityEngine.Networking.UnityWebRequest.Get(path))
			{
				var op = request.SendWebRequest();
				yield return op;
				if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
				{
					string json = request.downloadHandler.text;
					var parsed = JsonUtility.FromJson<PinConfigPayload>(json);
					if (parsed != null)
					{
						var pins = new List<string>();
						if (parsed.pins != null)
							pins.AddRange(parsed.pins);
						ClientCertificatePinning.Configure(pins, parsed.allowOnEmpty);
						Log.Debug(logChannel,
							$"Loaded {pins.Count} pin(s) from StreamingAssets/{configFileName}.");
						yield break;
					}
				}
			}
			// Fall through — configure with compile-time defaults.
			ClientCertificatePinning.Configure(defaultPins, DefaultAllowOnEmpty);
			Log.Warning(logChannel,
				$"Failed to load {configFileName} from StreamingAssets via UnityWebRequest; using compile-time defaults.");
		}
#endif

		/// <summary>
		/// Synchronously loads client-security.json from the filesystem.  Only
		/// called on platforms where <see cref="Application.streamingAssetsPath"/>
		/// is directly filesystem-accessible (Editor, standalone desktop builds).
		/// On Android, the async coroutine path in <see cref="LoadFromStreamingAssetsCoroutine"/>
		/// is used instead to avoid main-thread blocking.
		/// </summary>
		private static bool TryLoadFromStreamingAssets(out List<string> pins, out bool allowOnEmpty)
		{
			pins = null;
			allowOnEmpty = DefaultAllowOnEmpty;

			string path = Path.Combine(Application.streamingAssetsPath, configFileName);
			if (!File.Exists(path))
			{
				return false;
			}

			string json = File.ReadAllText(path);
			var parsed = JsonUtility.FromJson<PinConfigPayload>(json);
			if (parsed == null)
			{
				return false;
			}

			pins = new List<string>();
			if (parsed.pins != null)
			{
				pins.AddRange(parsed.pins);
			}
			allowOnEmpty = parsed.allowOnEmpty;
			return true;
		}

		[Serializable]
		private class PinConfigPayload
		{
			public string[] pins;
			public bool allowOnEmpty;
		}

		/// <summary>
		/// Minimal MonoBehaviour for running coroutines from a static context.
		/// Used on platforms where <see cref="Application.streamingAssetsPath"/> is
		/// not directly filesystem-accessible (Android, WebGL) to load pin config
		/// asynchronously via UnityWebRequest without blocking the main thread.
		/// </summary>
		private class CoroutineHelper : MonoBehaviour
		{
			private static CoroutineHelper instance;

			/// <summary>
			/// Returns true if the coroutine helper can be created (always true on
			/// Unity standalone / mobile / WebGL player builds).
			/// </summary>
			public static bool IsAvailable => true;

			/// <summary>
			/// Starts a coroutine on a persistent hidden GameObject.
			/// </summary>
			public static void Start(System.Collections.IEnumerator routine)
			{
				if (instance == null)
				{
					var go = new GameObject("ClientSecurityBootstrap-CoroutineHelper")
					{
						hideFlags = HideFlags.HideAndDontSave
					};
					GameObject.DontDestroyOnLoad(go);
					instance = go.AddComponent<CoroutineHelper>();
				}
				instance.StartCoroutine(routine);
			}
		}
	}
}
