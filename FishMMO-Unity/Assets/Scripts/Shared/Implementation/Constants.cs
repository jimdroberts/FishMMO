using UnityEngine;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public static class Constants
	{
		public const string SharedStaticLabel = "Shared_Static_Permanent";

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetTemporaryPath()
		{
			return Path.Combine(GetWorkingDirectory(), "Temp");
		}

		public static class Configuration
		{
			public static readonly string ProjectName = "FishMMO";
			public static readonly string ClientExecutable = ProjectName + ".exe";
			public static readonly string UpdaterExecutable = "Updater.exe";
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

			public static readonly string ScenePath = "Assets/Scenes/";
			public static readonly string BootstrapScenePath = "Assets/Scenes/";
			public static readonly string ClientBootstrapScenePath = "Assets/Scenes/Client/";
			public static readonly string ServerBootstrapScenePath = "Assets/Scenes/Server/";
			public static readonly string WorldScenePath = "Assets/Scenes/WorldScene";
			public static readonly string LocalScenePath = "Assets/LOCAL/Scenes/";

			public const int MaximumPlayerHotkeys = 12;
		}

		public static class Layers
		{
			public static readonly LayerMask Default = LayerMask.NameToLayer("Default");
			public static readonly LayerMask IgnoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
			public static readonly LayerMask Ground = LayerMask.NameToLayer("Ground");
			public static readonly LayerMask Obstruction = LayerMask.GetMask("Default", "Ground");
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

				return missing;
			}
		}

		public static class Character
		{
			public const float WalkSpeed = 1.5f;
			public const float RunSpeed = 4.0f;
			public const float SprintSpeed = 6.0f;
			public const float SprintStaminaCost = 5.0f;
			public const float CrouchSpeed = 2.0f;
			public const float JumpUpSpeed = 6.5f;
			public const float JumpStaminaCost = 5.0f;
			public static readonly Vector3 Gravity = new Vector3(0, -14.0f, 0);
		}
	}
}