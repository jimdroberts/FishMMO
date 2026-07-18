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
			/// </summary>
			public static readonly string ProjectName = "FishMMO";

			/// <summary>
			/// File name of the client executable. Platform-conditional:
			/// Windows uses ".exe" suffix; Linux and macOS omit it.
			/// </summary>
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
			/// </summary>
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
			/// </summary>
			public static readonly string SetupDirectory = "FishMMO-Setup";

			/// <summary>
			/// Unified API Host URL. NGINX routes to the correct backend by path.
			/// Change this constant when deploying to a different domain.
			/// </summary>
			public const string APIHost = "https://api.fishmmo.com/";

			/// <summary>
			/// Game server hostname. All clients (standalone + WebGL) connect via
			/// WebTransport (QUIC/HTTP3) to https://GameHost:{port}. NGINX forwards
			/// raw UDP to the correct backend game server on loopback.
			/// Change this constant when deploying to a different domain.
			/// </summary>
			public const string GameHost = "game.fishmmo.com";

			/// <summary>
			/// Root path for Unity scene assets.
			/// </summary>
			public static readonly string ScenePath = "Assets/Scenes/";

			/// <summary>
			/// Root path for bootstrap scene assets.
			/// </summary>
			public static readonly string BootstrapScenePath = "Assets/Scenes/";

			/// <summary>
			/// Path for client-specific bootstrap scenes.
			/// </summary>
			public static readonly string ClientBootstrapScenePath = "Assets/Scenes/Client/";

			/// <summary>
			/// Path for server-specific bootstrap scenes.
			/// </summary>
			public static readonly string ServerBootstrapScenePath = "Assets/Scenes/Server/";

			/// <summary>
			/// Path template for world scene assets.
			/// </summary>
			public static readonly string WorldScenePath = "Assets/Scenes/WorldScene";

			/// <summary>
			/// Path for local development scene assets (not source-controlled).
			/// </summary>
			public static readonly string LocalScenePath = "Assets/LOCAL/Scenes/";

			/// <summary>
			/// Maximum number of configurable player hotkeys.
			/// </summary>
			public const int MaximumPlayerHotkeys = 12;
		}

		public static class Layers
		{
			/// <summary>
			/// Default layer (Layer 0), used for most GameObjects.
			/// </summary>
			public static readonly LayerMask Default = LayerMask.NameToLayer("Default");

			/// <summary>
			/// Ignore Raycast layer, used for UI and non-interactive objects.
			/// </summary>
			public static readonly LayerMask IgnoreRaycast = LayerMask.NameToLayer("Ignore Raycast");

			/// <summary>
			/// Ground layer, used for terrain and walkable surfaces.
			/// </summary>
			public static readonly LayerMask Ground = LayerMask.NameToLayer("Ground");

			/// <summary>
			/// Obstruction layer mask combining Default and Ground layers,
			/// used for line-of-sight and occlusion checks.
			/// </summary>
			public static readonly LayerMask Obstruction = LayerMask.GetMask("Default", "Ground");

			/// <summary>
			/// Player layer, used for player characters.
			/// </summary>
			public static readonly LayerMask Player = LayerMask.NameToLayer("Player");

			/// <summary>
			/// Validates that all required layers exist in the project's Tag Manager.
			/// Returns a list of missing layer names, or an empty list if all are present.
			/// Call during bootstrap to catch misconfigured projects early.
			/// </summary>
			public static System.Collections.Generic.List<string> Validate()
			{
				var missing = new System.Collections.Generic.List<string>();
				void Check(int layer, string name) { if (layer < 0) missing.Add(name); }

				Check(Default, "Default");
				Check(IgnoreRaycast, "Ignore Raycast");
				Check(Ground, "Ground");
				Check(Player, "Player");

				// Obstruction is a combined mask; verify its bitmask is non-zero.
				if (Obstruction.value == 0) missing.Add("Obstruction");

				return missing;
			}
		}

		public static class Character
		{
			/// <summary>
			/// Movement speed while walking.
			/// </summary>
			public const float WalkSpeed = 1.5f;

			/// <summary>
			/// Movement speed while running.
			/// </summary>
			public const float RunSpeed = 4.0f;

			/// <summary>
			/// Movement speed while sprinting.
			/// </summary>
			public const float SprintSpeed = 6.0f;

			/// <summary>
			/// Stamina cost per second while sprinting.
			/// </summary>
			public const float SprintStaminaCost = 5.0f;

			/// <summary>
			/// Movement speed while crouching.
			/// </summary>
			public const float CrouchSpeed = 2.0f;

			/// <summary>
			/// Upward velocity applied when jumping.
			/// </summary>
			public const float JumpUpSpeed = 6.5f;

			/// <summary>
			/// Stamina cost per jump.
			/// </summary>
			public const float JumpStaminaCost = 5.0f;

			/// <summary>
			/// Gravity vector applied to characters.
			/// </summary>
			public static readonly Vector3 Gravity = new Vector3(0, -14.0f, 0);
		}
	}
}
