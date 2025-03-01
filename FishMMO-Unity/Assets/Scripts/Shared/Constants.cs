using UnityEngine;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	public static class Constants
	{
		/// <summary>
		/// IPv4 Regex, can we get IPv6 support???
		/// </summary>
		public static bool IsAddressValid(string address)
		{
			const string ValidIpAddressRegex = "^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$";
			Match match = Regex.Match(address, ValidIpAddressRegex);
			return match.Success;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		public static class Configuration
		{
			public static readonly string Version = "1.0";
			public static readonly string ProjectName = "FishMMO";
			public static readonly string ClientExecutable = ProjectName + ".exe";
			public static readonly string UpdaterExecutable = "Updater.exe";
			public static readonly string SetupDirectory = "FishMMO-Setup";

			public static readonly string IPFetchHost = "https://127.0.0.1:8080/";

			public static readonly string DatabaseDirectory = "FishMMO-Database";
			public static readonly string DatabaseProjectDirectory = "FishMMO-DB";
			public static readonly string DatabaseMigratorProjectDirectory = "FishMMO-DB-Migrator";
			public static readonly string ProjectPath = "." + Path.DirectorySeparatorChar + "FishMMO-Database" + Path.DirectorySeparatorChar + "FishMMO-DB" + Path.DirectorySeparatorChar + "FishMMO-DB.csproj";
			public static readonly string StartupProject = "." + Path.DirectorySeparatorChar + "FishMMO-Database" + Path.DirectorySeparatorChar + "FishMMO-DB-Migrator" + Path.DirectorySeparatorChar + "FishMMO-DB-Migrator.csproj";

			public static readonly string InstallerPath = "Assets" + Path.DirectorySeparatorChar + "Scenes" + Path.DirectorySeparatorChar + "Installer.unity";
			public static readonly string ScenePath = "Assets/Scenes/";
			public static readonly string BootstrapScenePath = "Assets/Scenes/Bootstraps/";
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

		public static class Authentication
		{
			public const int AccountNameMinLength = 3;
			public const int AccountNameMaxLength = 32;

			public const int AccountPasswordMinLength = 3;
			public const int AccountPasswordMaxLength = 32;

			public const int CharacterNameMinLength = 3;
			public const int CharacterNameMaxLength = 32;

			public const int MaxGuildNameLength = 64;

			public static bool IsAllowedEmailUsername(string email)
			{
				return !string.IsNullOrWhiteSpace(email) &&
					   Regex.IsMatch(email, @"^(?=.{3,320}$)[a-zA-Z0-9](?:[a-zA-Z0-9._-]*[a-zA-Z0-9])?@[a-zA-Z0-9][a-zA-Z0-9._-]*[a-zA-Z0-9]$");
			}

			public static bool IsAllowedUsername(string accountName)
			{
				return !string.IsNullOrWhiteSpace(accountName) &&
					   accountName.Length >= AccountNameMinLength &&
					   accountName.Length <= AccountNameMaxLength &&
					   Regex.IsMatch(accountName, @"^[a-zA-Z0-9_]+$");
			}

			public static bool IsAllowedPassword(string accountPassword)
			{
				return !string.IsNullOrWhiteSpace(accountPassword) &&
					   accountPassword.Length >= AccountPasswordMinLength &&
					   accountPassword.Length <= AccountPasswordMaxLength &&
					   Regex.IsMatch(accountPassword, @"^[a-zA-Z0-9_]+$");
			}

			public static bool IsAllowedCharacterName(string characterName)
			{
				return !string.IsNullOrWhiteSpace(characterName) &&
					   characterName.Length >= CharacterNameMinLength &&
					   characterName.Length <= CharacterNameMaxLength &&
					   Regex.IsMatch(characterName, @"^[A-Za-z]+(?: [A-Za-z]+){0,2}$");
			}

			public static bool IsAllowedGuildName(string guildName)
			{
				return !string.IsNullOrWhiteSpace(guildName) &&
						guildName.Length <= MaxGuildNameLength &&
						Regex.IsMatch(guildName, @"^[A-Za-z]+(?: [A-Za-z]+){0,2}$");
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