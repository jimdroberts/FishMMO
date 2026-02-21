using System;
using System.Text.RegularExpressions;

namespace FishMMO.Database
{
	/// <summary>
	/// Centralized authentication and naming rules shared across database services.
	/// This class contains validated length constraints and helper functions for common
	/// account/character/guild identifier inputs.
	/// </summary>
	/// <remarks>
	/// Keep this class focused on cross-cutting validation rules (usernames, passwords, names).
	/// Prefer domain-specific helpers over adding unrelated constants.
	/// </remarks>
	public static class Authentication
	{
		/// <summary>
		/// Standard validation error message for an invalid account username.
		/// </summary>
		public static readonly string InvalidUsernameError =
			$"Invalid username. Username must be between {AccountNameMinLength} and {AccountNameMaxLength} characters.";

		/// <summary>
		/// Standard validation error message for an invalid account password.
		/// </summary>
		public static readonly string InvalidPasswordError =
			$"Invalid password. Password must be between {AccountPasswordMinLength} and {AccountPasswordMaxLength} characters.";

		/// <summary>
		/// Standard validation error message for an invalid character name.
		/// </summary>
		public static readonly string InvalidCharacterNameError =
			$"Invalid character name. Character name must be between {CharacterNameMinLength} and {CharacterNameMaxLength} characters.";

		/// <summary>
		/// Standard validation error message for an invalid guild name.
		/// </summary>
		public static readonly string InvalidGuildNameError =
			$"Invalid guild name. Guild name must be at most {MaxGuildNameLength} characters.";

		/// <summary>
		/// Minimum length for an account name.
		/// </summary>
		public const int AccountNameMinLength = 3;

		/// <summary>
		/// Maximum length for an account name.
		/// </summary>
		public const int AccountNameMaxLength = 32;

		/// <summary>
		/// Minimum length for an account password.
		/// </summary>
		public const int AccountPasswordMinLength = 3;

		/// <summary>
		/// Maximum length for an account password.
		/// </summary>
		public const int AccountPasswordMaxLength = 32;

		/// <summary>
		/// Minimum length for a character name.
		/// </summary>
		public const int CharacterNameMinLength = 3;

		/// <summary>
		/// Maximum length for a character name.
		/// </summary>
		public const int CharacterNameMaxLength = 32;

		/// <summary>
		/// Maximum length for a guild name.
		/// </summary>
		public const int MaxGuildNameLength = 64;

		private static readonly Regex EmailUsernameRegex = new Regex(
			@"^(?=.{3,320}$)[a-zA-Z0-9](?:[a-zA-Z0-9._-]*[a-zA-Z0-9])?@[a-zA-Z0-9][a-zA-Z0-9._-]*[a-zA-Z0-9]$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex UsernameRegex = new Regex(
			@"^[a-zA-Z0-9_]+$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex PasswordRegex = new Regex(
			@"^[a-zA-Z0-9_]+$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex CharacterNameRegex = new Regex(
			@"^[A-Za-z]+(?: [A-Za-z]+){0,2}$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly Regex GuildNameRegex = new Regex(
			@"^[A-Za-z]+(?: [A-Za-z]+){0,4}$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>
		/// Validates whether an email-style username is allowed.
		/// </summary>
		/// <param name="email">The email username to validate.</param>
		/// <returns>True if the email meets format and length constraints; otherwise false.</returns>
		public static bool IsAllowedEmailUsername(string email)
		{
			return !string.IsNullOrWhiteSpace(email)
				&& EmailUsernameRegex.IsMatch(email);
		}

		/// <summary>
		/// Validates whether an account username is allowed.
		/// </summary>
		/// <param name="accountName">The account name to validate.</param>
		/// <returns>True if the name meets format and length constraints; otherwise false.</returns>
		public static bool IsAllowedUsername(string accountName)
		{
			return !string.IsNullOrWhiteSpace(accountName)
				&& accountName.Length >= AccountNameMinLength
				&& accountName.Length <= AccountNameMaxLength
				&& UsernameRegex.IsMatch(accountName);
		}

		/// <summary>
		/// Validates whether an account password string is allowed by policy.
		/// </summary>
		/// <param name="accountPassword">The password to validate.</param>
		/// <returns>True if the password meets format and length constraints; otherwise false.</returns>
		public static bool IsAllowedPassword(string accountPassword)
		{
			return !string.IsNullOrWhiteSpace(accountPassword)
				&& accountPassword.Length >= AccountPasswordMinLength
				&& accountPassword.Length <= AccountPasswordMaxLength
				&& PasswordRegex.IsMatch(accountPassword);
		}

		/// <summary>
		/// Validates whether a character name is allowed.
		/// </summary>
		/// <param name="characterName">The character name to validate.</param>
		/// <returns>True if the name meets format and length constraints; otherwise false.</returns>
		public static bool IsAllowedCharacterName(string characterName)
		{
			return !string.IsNullOrWhiteSpace(characterName)
				&& characterName.Length >= CharacterNameMinLength
				&& characterName.Length <= CharacterNameMaxLength
				&& CharacterNameRegex.IsMatch(characterName);
		}

		/// <summary>
		/// Validates whether a guild name is allowed.
		/// </summary>
		/// <param name="guildName">The guild name to validate.</param>
		/// <returns>True if the name meets format and length constraints; otherwise false.</returns>
		public static bool IsAllowedGuildName(string guildName)
		{
			return !string.IsNullOrWhiteSpace(guildName)
				&& guildName.Length <= MaxGuildNameLength
				&& GuildNameRegex.IsMatch(guildName);
		}
	}
}