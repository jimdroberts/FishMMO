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
		/// To generate pins for production:
		///   1. Get the server's TLS certificate (PEM format)
		///   2. Call ClientCertificatePinning.ComputeSpkiSha256Base64(certDerBytes)
		///   3. Add at least 2 pins (active + backup key) for rotation support
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
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_WEBGL)
			// On Android and WebGL, streaming assets are inside the APK/AAB or
			// served via HTTP and must be loaded via UnityWebRequest, which requires
			// main-thread yielding.  A blocking spin-wait here would cause an ANR
			// (Android) or browser-fetch deadlock (WebGL).  We defer to a coroutine-driven
			// non-blocking load instead, and fall through to compile-time defaults
			// synchronously only if the coroutine helper is unavailable.
			// Configure with allowOnEmpty:true so requests aren't rejected while
			// the async load is in-flight. The coroutine handler will replace
			// this configuration with the real pins from StreamingAssets on
			// success, or fall through to compile-time defaults on failure.
			ClientCertificatePinning.Configure(defaultPins, DefaultAllowOnEmpty);
			try
			{
				CoroutineRunner.Start(LoadFromStreamingAssetsCoroutine());
			}
			catch (Exception ex)
			{
				Log.Error(logChannel,
					$"CoroutineRunner.Start failed: {ex.Message}. " +
					"TLS pinning remains in temporary allowOnEmpty=true state on this platform " +
					"because streaming assets are not directly filesystem-accessible for a synchronous fallback. " +
					"Verify that CoroutineRunner.cs is included in the build and not stripped.");
			}
			// The coroutine (if started) handles both success and failure paths
			// itself (falling through to compile-time defaults on failure).
			// Without this early return, the empty defaultPins would be
			// applied below, weakening pinning until the async load completes.
			return;
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

			if (defaultPins.Length == 0)
			{
				if (DefaultAllowOnEmpty)
				{
					// Editor/dev builds: allow empty pins so developers can talk to
					// localhost / unsigned staging endpoints without configuring pins.
					ClientCertificatePinning.Configure(defaultPins, allowOnEmpty: true);
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
					// Release build with no pins: use allowOnEmpty=true as a safety net to
					// prevent bricking the client. A CRITICAL-level log entry is emitted so
					// developers and testers see this immediately.
					ClientCertificatePinning.Configure(defaultPins, allowOnEmpty: true);
					Log.Critical(logChannel,
						"==============================================================\n" +
						"  CRITICAL: RELEASE BUILD SHIPPED WITHOUT TLS PINS.\n" +
						"  Falling back to allowOnEmpty=true to prevent client bricking.\n" +
						"  THIS IS A SECURITY RISK. Every HTTPS API call is accepted.\n" +
						"  Populate defaultPins[] (recommended: 2+ keys for rotation)\n" +
						"  or ship a valid StreamingAssets/" + configFileName + ".\n" +
						"==============================================================");
				}
			}
			else
			{
				ClientCertificatePinning.Configure(defaultPins, DefaultAllowOnEmpty);
			}
		}

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_WEBGL)
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
					if (TryParsePinConfig(json, out var pins, out var allowOnEmpty))
					{
						ClientCertificatePinning.Configure(pins, allowOnEmpty);
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
			if (!TryParsePinConfig(json, out pins, out allowOnEmpty))
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Parses <c>client-security.json</c>, drops placeholder / blank pins, and
		/// resolves <c>allowOnEmpty</c>. Editor / development builds force
		/// allow-on-empty when no real pins remain so Play Mode is not bricked by
		/// unreplaced <c>REPLACE_ME_*</c> templates.
		/// </summary>
		private static bool TryParsePinConfig(string json, out List<string> pins, out bool allowOnEmpty)
		{
			pins = null;
			allowOnEmpty = DefaultAllowOnEmpty;

			var parsed = JsonUtility.FromJson<PinConfigPayload>(json);
			if (parsed == null)
			{
				return false;
			}

			pins = new List<string>();
			int rawCount = 0;
			int droppedPlaceholders = 0;
			if (parsed.pins != null)
			{
				foreach (string raw in parsed.pins)
				{
					if (string.IsNullOrWhiteSpace(raw))
					{
						continue;
					}

					rawCount++;
					string pin = raw.Trim();
					if (IsPlaceholderPin(pin))
					{
						droppedPlaceholders++;
						continue;
					}

					pins.Add(pin);
				}
			}

			// JsonUtility maps exact field names only. Older templates used
			// allowOnEmptyPins; honour either so config cannot silently force-fail.
			allowOnEmpty = parsed.allowOnEmpty || parsed.allowOnEmptyPins;

			if (droppedPlaceholders > 0)
			{
				Log.Warning(logChannel,
					$"Ignored {droppedPlaceholders} placeholder pin(s) in {configFileName} " +
					$"(raw entries={rawCount}, usable={pins.Count}). " +
					"Replace REPLACE_ME_* values with real SPKI hashes (FishMMO > Security > Fetch Certificate Pins).");
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			// Unreplaced templates used to count as "configured pins" and reject every
			// HTTPS call in the Editor. If nothing usable remains, fall back to the
			// editor-friendly empty-pin policy instead of pin-mismatch fail-closed.
			if (pins.Count == 0)
			{
				allowOnEmpty = true;
			}
#endif
			return true;
		}

		/// <summary>
		/// True for intentionally invalid template pins shipped in the repo.
		/// </summary>
		private static bool IsPlaceholderPin(string pin)
		{
			return pin.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase);
		}

		[Serializable]
		private class PinConfigPayload
		{
			public string[] pins;
			/// <summary>Canonical field written by CertificatePinTool / docs.</summary>
			public bool allowOnEmpty;
			/// <summary>Legacy / docs field name; treated the same as <see cref="allowOnEmpty"/>.</summary>
			public bool allowOnEmptyPins;
		}

		/// <summary>
		/// Serializable data class for deserializing the client-security.json configuration file.
		/// Contains the list of SPKI pin hashes and the allow-on-empty fallback flag.
		/// This is not a MonoBehaviour; it is a plain data contract used by JsonUtility.
		/// </summary>
	}
}
