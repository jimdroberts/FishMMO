using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	public static class AuthenticationHelper
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