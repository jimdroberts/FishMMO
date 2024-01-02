using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	public static class Constants
	{
		public static class Configuration
		{
			public static readonly string ProjectName = "FishMMO";
			public static readonly string SetupDirectory = "FishMMO-Setup";

			public static readonly string DatabaseDirectory = "FishMMO-Database";
			public static readonly string DatabaseProjectDirectory = "FishMMO-DB";
			public static readonly string DatabaseMigratorProjectDirectory = "FishMMO-DB-Migrator";
			public static readonly string ProjectPath = "." + Path.DirectorySeparatorChar + "FishMMO-Database" + Path.DirectorySeparatorChar + "FishMMO-DB" + Path.DirectorySeparatorChar + "FishMMO-DB.csproj";
			public static readonly string StartupProject = "." + Path.DirectorySeparatorChar + "FishMMO-Database" + Path.DirectorySeparatorChar + "FishMMO-DB-Migrator" + Path.DirectorySeparatorChar + "FishMMO-DB-Migrator.csproj";

			public static readonly string BootstrapScenePath = "Assets" + Path.DirectorySeparatorChar + "Scenes" + Path.DirectorySeparatorChar + "Bootstraps" + Path.DirectorySeparatorChar;
			public static readonly string WorldScenePath = "Assets" + Path.DirectorySeparatorChar + "Scenes" + Path.DirectorySeparatorChar + "WorldScene";
		}

		public static class Layers
		{
			public static readonly LayerMask Default = LayerMask.NameToLayer("Default");
			public static readonly LayerMask LocalEntity = LayerMask.NameToLayer("LocalEntity");
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
	}
}