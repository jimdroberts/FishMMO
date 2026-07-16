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
			/// </summary>
			public static readonly string APIHost = "https://api.fishmmo.com/";

			/// <summary>
			/// NGINX game WebSocket hostname for Bayou/WebGL clients.
			/// WebGL clients connect via wss://GameHost/ws/{port} instead of direct IP:port.
			/// NGINX dynamically routes /ws/{port} to the correct backend game server.
			/// </summary>
			public static readonly string GameHost = "game.fishmmo.com";

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