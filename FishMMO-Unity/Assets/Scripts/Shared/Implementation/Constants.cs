using UnityEngine;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public static class Constants
	{
		/// <summary>
		/// Label used for shared static GameObjects that persist across scenes.
		/// </summary>
		public const string SharedStaticLabel = "Shared_Static_Permanent";

		/// <summary>
		/// Returns the working directory for the application.
		/// In the Unity Editor, this is the project root.
		/// In standalone builds, this is the base directory of the executable.
		/// On WebGL (non-Editor), returns <see cref="Application.persistentDataPath"/>
		/// since <c>AppDomain.CurrentDomain.BaseDirectory</c> is not available at runtime.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetWorkingDirectory()
		{
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			// Under IL2CPP, AppContext.BaseDirectory may be stripped if not preserved
			// in link.xml. The Addressables-generated link.xml (in Library/) currently
			// preserves the System namespace, so this is safe. If this throws
			// MissingMethodException, add System.AppContext to the manual link.xml.
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
#else
			// WebGL: AppDomain.CurrentDomain.BaseDirectory is not available.
			// Use persistentDataPath as a safe fallback for configuration reads.
			return Application.persistentDataPath;
#endif
		}

		/// <summary>
		/// Returns the path to the temporary directory used for patch downloads and other transient files.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetTemporaryPath()
		{
			return Path.Combine(GetWorkingDirectory(), "Temp");
		}

		public static class Configuration
		{
			/// <summary>
			/// Display name of the game project, used in UI labels and window titles.
			///
			/// <para>
			/// <c>static readonly</c> rather than <c>const</c> to allow the name to be
			/// overridden by a config file or branding patch without recompiling.
			/// </para>
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string ProjectName = "FishMMO";

			/// <summary>
			/// File name of the client executable. Platform-conditional:
			/// Windows uses ".exe" suffix; Linux and macOS omit it.
			/// <c>static readonly</c> required because of the <c>#if</c> conditional compilation.
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string ClientExecutable =
#if UNITY_STANDALONE_WIN
				ProjectName + ".exe";
#elif UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
				ProjectName;
#else
				ProjectName + ".exe";
#endif

			/// <summary>
			/// File name of the updater executable. Platform-conditional:
			/// Windows uses ".exe" suffix; Linux and macOS omit it.
			/// <c>static readonly</c> required because of the <c>#if</c> conditional compilation.
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string UpdaterExecutable =
#if UNITY_STANDALONE_WIN
				"Updater.exe";
#elif UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
				"Updater";
#else
				"Updater.exe";
#endif

			/// <summary>
			/// Relative path to the FishMMO-Setup directory containing deployment config files.
			///
			/// <para>
			/// <c>static readonly</c> rather than <c>const</c> to allow future runtime
			/// configuration (e.g. from a setup config file or environment variable).
			/// </para>
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string SetupDirectory = "FishMMO-Setup";

			/// <summary>
			/// Unified API Host URL. NGINX routes to the correct backend by path.
			///
			/// <para>
			/// Set at build time via <see cref="GeneratedHostConfig.ApiHost"/>,
			/// which CI substitutes from the FISHMMO_API_HOST environment variable.
			/// The committed source contains a sentinel placeholder — the build
			/// validator blocks release builds that still contain the sentinel.
			/// </para>
			/// <para>
			/// For development/testing against different servers, use the
			/// <see cref="GlobalSettings"/> override mechanism (see
			/// <c>ApiHostResolver</c>) instead of modifying this constant.
			/// </para>
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string APIHost = GeneratedHostConfig.ApiHost;

			/// <summary>
			/// Game server hostname. All clients (standalone + WebGL) connect via
			/// WebTransport (QUIC/HTTP3) to https://GameHost:{port}. NGINX forwards
			/// raw UDP to the correct backend game server on loopback.
			///
			/// <para>
			/// Set at build time via <see cref="GeneratedHostConfig.GameHost"/>,
			/// which CI substitutes from the FISHMMO_GAME_HOST environment variable.
			/// </para>
			/// <para>
			/// For development/testing against different servers, use the
			/// <see cref="GlobalSettings"/> override mechanism (see
			/// <c>ApiHostResolver</c>) instead of modifying this constant.
			/// </para>
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string GameHost = GeneratedHostConfig.GameHost;

			/// <summary>
			/// World server hostname for WebTransport (QUIC) hops.
			/// Defaults to <see cref="GameHost"/> so single-hostname deploys keep working;
			/// multi-host deployments can later specialize this independently.
			/// </summary>
			public static string WorldGameHost => GameHost;

			/// <summary>
			/// Scene server hostname for WebTransport (QUIC) hops.
			/// Defaults to <see cref="GameHost"/> so single-hostname deploys keep working.
			/// </summary>
			public static string SceneGameHost => GameHost;

			/// <summary>Default LoginServer listen / public QUIC port band start.</summary>
			public const int LoginServerPort = 7770;

			/// <summary>Default WorldServer listen / public QUIC port band start.</summary>
			public const int WorldServerPort = 7780;

			/// <summary>Default SceneServer listen / public QUIC port band start.</summary>
			public const int SceneServerPort = 7790;

			/// <summary>
			/// Picks the game hostname for a connection port using standard port bands:
			/// Login 7770–7779, World 7780–7789, Scene 7790+.
			/// Used by client Login→World→Scene hops after IPFetch token staging.
			/// </summary>
			public static string GetGameHostForPort(ushort port)
			{
				if (port >= LoginServerPort && port < WorldServerPort)
					return GameHost;
				if (port >= WorldServerPort && port < SceneServerPort)
					return WorldGameHost;
				return SceneGameHost;
			}

				/// <summary>
				/// Launcher HTML/news page URL.
				///
				/// <para>
				/// Set at build time via <see cref="GeneratedHostConfig.LauncherHtmlUrl"/>,
				/// which CI substitutes from the FISHMMO_ROOT_DOMAIN environment variable.
				/// </para>
				/// </summary>
				/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
				public static readonly string LauncherHtmlUrl = GeneratedHostConfig.LauncherHtmlUrl;

				/// <summary>
				/// SMTP from address for verification/notification emails.
				///
				/// <para>
				/// Set at build time via <see cref="GeneratedHostConfig.SmtpFromAddress"/>,
				/// which CI substitutes from the FISHMMO_ROOT_DOMAIN environment variable.
				/// </para>
				/// </summary>
				/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
				public static readonly string SmtpFromAddress = GeneratedHostConfig.SmtpFromAddress;

				/// <summary>
				/// SMTP from display name for outgoing emails.
				///
				/// <para>
				/// Set at build time via <see cref="GeneratedHostConfig.SmtpFromName"/>.
				/// </para>
				/// </summary>
				/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
				public static readonly string SmtpFromName = GeneratedHostConfig.SmtpFromName;

			/// <summary>
			/// Root path for Unity scene assets.
			///
			/// <para>
			/// <c>static readonly</c> (not <c>const</c>) so that downstream references
			/// resolve at runtime and can be replaced for specialized layouts.
			/// </para>
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string ScenePath = "Assets/Scenes/";

			/// <summary>
			/// Root path for bootstrap scene assets.
			///
			/// <para>
			/// Kept as a separate constant alongside the otherwise-identical <see cref="ScenePath"/>
			/// for API clarity (bootstrap vs. gameplay scenes are conceptually distinct) and so that
			/// bootstrap scenes can be moved to a different root without breaking every reference.
			/// Currently both resolve to <c>"Assets/Scenes/"</c> by design — all scenes share one
			/// directory tree. If bootstrap scenes are ever relocated, update this value independently
			/// of <see cref="ScenePath"/>; all references (dashboard, build tool, editor shortcuts)
			/// will pick it up automatically.
			/// </para>
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string BootstrapScenePath = "Assets/Scenes/";

			/// <summary>
			/// Path for client-specific bootstrap scenes.
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string ClientBootstrapScenePath = "Assets/Scenes/Client/";

			/// <summary>
			/// Path for server-specific bootstrap scenes.
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string ServerBootstrapScenePath = "Assets/Scenes/Server/";

			/// <summary>
			/// Path template for world scene assets.
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string WorldScenePath = "Assets/Scenes/WorldScene";

			/// <summary>
			/// Path for local development scene assets (not source-controlled).
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public static readonly string LocalScenePath = "Assets/LOCAL/Scenes/";

			/// <summary>
			/// Maximum number of configurable player hotkeys.
			/// </summary>
			/// <remarks>Configuration value baked at compile time; changing requires a rebuild.</remarks>
			public const int MaximumPlayerHotkeys = 12;
		}

		public static class Layers
		{
			/// <summary>
			/// DefaultLayer (Layer 0), used for most GameObjects.
			/// </summary>
			/// <remarks>
			/// NOTE: Does not throw on missing layers — only logs a warning. This allows
			/// the editor to partially function with missing layer configuration during development.
			/// </remarks>
			public static readonly LayerMask DefaultLayer = SafeGetLayerMask("Default");

			/// <summary>
			/// Ignore Raycast layer, used for UI and non-interactive objects.
			/// </summary>
			/// <remarks>
			/// NOTE: Does not throw on missing layers — only logs a warning. This allows
			/// the editor to partially function with missing layer configuration during development.
			/// </remarks>
			public static readonly LayerMask IgnoreRaycast = SafeGetLayerMask("Ignore Raycast");

			/// <summary>
			/// Ground layer, used for terrain and walkable surfaces.
			/// </summary>
			/// <remarks>
			/// NOTE: Does not throw on missing layers — only logs a warning. This allows
			/// the editor to partially function with missing layer configuration during development.
			/// </remarks>
			public static readonly LayerMask Ground = SafeGetLayerMask("Ground");

			/// <summary>
			/// Obstruction layer mask combining Default and Ground layers,
			/// used for line-of-sight and occlusion checks.
			/// </summary>
			/// <remarks>
			/// NOTE: Does not throw on missing layers — only logs a warning. This allows
			/// the editor to partially function with missing layer configuration during development.
			/// </remarks>
			public static readonly LayerMask Obstruction = LayerMask.GetMask("Default", "Ground");

			/// <summary>
			/// Player layer, used for player characters.
			/// </summary>
			/// <remarks>
			/// NOTE: Does not throw on missing layers — only logs a warning. This allows
			/// the editor to partially function with missing layer configuration during development.
			/// </remarks>
			public static readonly LayerMask Player = SafeGetLayerMask("Player");

			/// <summary>
			/// Calls <see cref="LayerMask.NameToLayer"/> and converts the result to a bit mask.
			/// Returns 0 (empty mask) instead of 0x80000000 when the layer name is not found.
			/// This prevents <c>1 &lt;&lt; -1</c> which produces an incorrect sign-extended bit.
			/// </summary>
			/// <param name="layerName">The name of the layer.</param>
			/// <returns>A bit mask with the layer bit set, or 0 if the layer was not found.</returns>
			private static int SafeGetLayerMask(string layerName)
			{
				int layer = LayerMask.NameToLayer(layerName);
				if (layer < 0) return 0;
				return 1 << layer;
			}
			
			static Layers()
			{
				var missing = Validate();
				if (missing.Count > 0)
				{
					UnityEngine.Debug.LogWarning("[Constants.Layers] Missing layer(s): " + string.Join(", ", missing) + ". "
					+ "Check Project Settings > Tags and Layers.");
				}
			}

			/// <summary>
			/// Validates that all required layers exist in the project's Tag Manager.
			/// Returns a list of missing layer names, or an empty list if all are present.
			/// Call during bootstrap to catch misconfigured projects early.
			/// </summary>
			/// <remarks>
			/// NOTE: Does not throw on missing layers — only logs a warning. This allows
			/// the editor to partially function with missing layer configuration during development.
			/// </remarks>
			public static System.Collections.Generic.List<string> Validate()
			{
				var missing = new System.Collections.Generic.List<string>();
				void Check(int layerIndex, string name) { if (layerIndex < 0) missing.Add(name); }

				Check(LayerMask.NameToLayer("Default"), "Default");
				Check(LayerMask.NameToLayer("Ignore Raycast"), "Ignore Raycast");
				Check(LayerMask.NameToLayer("Ground"), "Ground");
				Check(LayerMask.NameToLayer("Player"), "Player");

				// Obstruction is a combined mask of Default + Ground layers;
				// verify its bitmask is non-zero (both constituent layers must exist).
				if (Obstruction.value == 0) missing.Add("Default and/or Ground (Obstruction mask is empty)");

				return missing;
			}
		}

		public static class Character
		{
			/// <summary>
			/// Movement speed while walking.
			/// </summary>
			/// <remarks>Movement speed in meters per second.</remarks>
			public const float WalkSpeed = 1.5f;

			/// <summary>
			/// Movement speed while running.
			/// </summary>
			/// <remarks>Movement speed in meters per second.</remarks>
			public const float RunSpeed = 4.0f;

			/// <summary>
			/// Movement speed while sprinting.
			/// </summary>
			/// <remarks>Movement speed in meters per second.</remarks>
			public const float SprintSpeed = 6.0f;

			/// <summary>
			/// Stamina cost per second while sprinting.
			/// </summary>
			/// <remarks>Stamina units consumed per second while sprinting.</remarks>
			public const float SprintStaminaCost = 5.0f;

			/// <summary>
			/// Movement speed while crouching.
			/// </summary>
			/// <remarks>Movement speed in meters per second.</remarks>
			public const float CrouchSpeed = 2.0f;

			/// <summary>
			/// Upward velocity applied when jumping.
			/// </summary>
			/// <remarks>Upward velocity in meters per second.</remarks>
			public const float JumpUpSpeed = 6.5f;

			/// <summary>
			/// Stamina cost per jump.
			/// </summary>
			/// <remarks>Stamina units consumed per jump.</remarks>
			public const float JumpStaminaCost = 5.0f;

			/// <summary>
			/// Gravity vector applied to characters.
			/// <c>static readonly</c> because <see cref="Vector3"/> is a non-primitive type and cannot be <c>const</c>.
			/// </summary>
			/// <remarks>Standard gravity vector applied to character movement.</remarks>
			public static readonly Vector3 Gravity = new Vector3(0, -14.0f, 0);
		}
	}
}