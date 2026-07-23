using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	/// <summary>
	/// Centralized authentication and naming rules shared across all projects.
	/// </summary>
	public static class Authentication
	{
		#region Error Messages
		public static readonly string InvalidUsernameError =
			$"Invalid username. Username must be between {AccountNameMinLength} and {AccountNameMaxLength} characters and contains only letters, numbers, and underscores.";

		public static readonly string InvalidPasswordError =
			$"Invalid password. Password must be between {AccountPasswordMinLength} and {AccountPasswordMaxLength} characters.";

		public static readonly string InvalidCharacterNameError =
			$"Invalid character name. Names must be between {CharacterNameMinLength} and {CharacterNameMaxLength} characters (Letters and single spaces only).";

		public static readonly string InvalidGuildNameError =
			$"Invalid guild name. Guild name must be between {GuildNameMinLength} and {GuildNameMaxLength} characters.";
		#endregion

		#region Constraints
		public const int AccountNameMinLength = 3;
		public const int AccountNameMaxLength = 32;
		public const int AccountPasswordMinLength = 8;
		public const int AccountPasswordMaxLength = 32;
		public const int CharacterNameMinLength = 3;
		public const int CharacterNameMaxLength = 24; // Reduced slightly for UI/Label consistency
		public const int GuildNameMinLength = 3;
		public const int GuildNameMaxLength = 32; // Standard MMO guild length
		#endregion

		#region Regular Expressions
		// Static fields use PascalCase per .NET naming conventions (no `this.` prefix);
		// usage follows the ClassName.FieldName pattern.
		// Emails: Standard RFC-adjacent validation (supports + aliases)
		private static readonly Regex EmailUsernameRegex = new Regex(
			@"^(?=.{3,320}$)[a-zA-Z0-9](?:[a-zA-Z0-9._+-]*[a-zA-Z0-9])?@[a-zA-Z0-9][a-zA-Z0-9._-]*[a-zA-Z0-9]$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		// Usernames: Alphanumeric and underscores
		private static readonly Regex UsernameRegex = new Regex(
			@"^[a-zA-Z0-9_]+$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		// Passwords: Allow special characters (!@#$%^ etc) for better security
		// Removed the restricted alphanumeric-only rule to encourage stronger passwords
		private static readonly Regex PasswordRegex = new Regex(
			@"^[a-zA-Z0-9!@#$%^&*()_+=\-\[\]{}|;:',.<>?~`]+$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		// Character Names: Starts with letter, allows single spaces between names (e.g. "Aragorn of Arnor")
		private static readonly Regex CharacterNameRegex = new Regex(
			@"^[A-Za-z]+(?: [A-Za-z]+)*$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		// Guild Names: Similar to character names but allows more segments
		private static readonly Regex GuildNameRegex = new Regex(
			@"^[A-Za-z0-9]+(?: [A-Za-z0-9]+)*$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);
		#endregion

		/// <summary>Validates an email address against the email regex pattern. Returns false for null/empty/whitespace input.</summary>
		public static bool IsAllowedEmailUsername(string email) =>
			!string.IsNullOrWhiteSpace(email) && EmailUsernameRegex.IsMatch(email);

		/// <summary>Validates an account username against length constraints and the alphanumeric+underscore regex pattern.</summary>
		public static bool IsAllowedUsername(string accountName) =>
			!string.IsNullOrWhiteSpace(accountName) &&
			accountName.Length >= AccountNameMinLength &&
			accountName.Length <= AccountNameMaxLength &&
			UsernameRegex.IsMatch(accountName);

		/// <summary>Validates an account password against length constraints and the allowed-special-characters regex pattern.</summary>
		public static bool IsAllowedPassword(string accountPassword) =>
			!string.IsNullOrWhiteSpace(accountPassword) &&
			accountPassword.Length >= AccountPasswordMinLength &&
			accountPassword.Length <= AccountPasswordMaxLength &&
			PasswordRegex.IsMatch(accountPassword);

		/// <summary>Validates a character name against length constraints and the letters-and-single-spaces regex pattern.</summary>
		public static bool IsAllowedCharacterName(string characterName) =>
			!string.IsNullOrWhiteSpace(characterName) &&
			characterName.Length >= CharacterNameMinLength &&
			characterName.Length <= CharacterNameMaxLength &&
			CharacterNameRegex.IsMatch(characterName);

		/// <summary>Validates a guild name against length constraints and the alphanumeric-with-spaces regex pattern.</summary>
		public static bool IsAllowedGuildName(string guildName) =>
			!string.IsNullOrWhiteSpace(guildName) &&
			guildName.Length >= GuildNameMinLength &&
			guildName.Length <= GuildNameMaxLength &&
			GuildNameRegex.IsMatch(guildName);

		/// <summary>
		/// Produces the canonical lookup form of an account name for case-insensitive comparisons
		/// against the persisted <c>name_lowercase</c> column and any in-memory rate-limit /
		/// lockout dictionaries keyed by username. Trims leading/trailing whitespace, applies
		/// Unicode NFKC normalisation so visually-equivalent confusables collapse, and
		/// lower-cases under the invariant culture so e.g. a Turkish locale cannot bypass a
		/// lockout via <c>I</c> vs <c>i</c> mapping. Returns the empty string for null input so
		/// callers do not need null-guards.
		/// </summary>
		public static string NormalizeAccountLookup(string accountName)
		{
			if (string.IsNullOrEmpty(accountName))
			{
				return string.Empty;
			}
			return accountName.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
		}

		/// <summary>
		/// Validates an IP Address, Hostname, or URI.
		/// Handles port stripping (e.g. 127.0.0.1:7770) before validation.
		/// </summary>
		public static bool IsAddressValid(string address)
		{
			if (string.IsNullOrWhiteSpace(address)) return false;

			string host = address.Trim();

			// Strip Protocol (e.g. ws://)
			if (host.Contains("://"))
			{
				if (Uri.TryCreate(host, UriKind.Absolute, out Uri uri))
				{
					host = uri.Host;
				}
			}

			// Strip Path (e.g. /ws/7770)
			int slashIndex = host.IndexOf('/');
			if (slashIndex >= 0) host = host.Substring(0, slashIndex);

			// Strip Port (e.g. :7770) - Handle IPv6 carefully (they use colons)
			int colonIndex = host.LastIndexOf(':');
			if (colonIndex >= 0)
			{
				// For bracketed IPv6 addresses (e.g. [::1]:7770), the colon
				// after the closing bracket is the port separator, while
				// colons inside the brackets are part of the address.
				if (host.StartsWith("[") && colonIndex > 0)
				{
					int closingBracket = host.LastIndexOf(']');
					if (closingBracket >= 0 && closingBracket < colonIndex)
					{
						// Colon is after the closing bracket -- strip the port
						host = host.Substring(0, colonIndex);
					}
				}
				else if (!IPAddress.TryParse(host, out _))
				{
					host = host.Substring(0, colonIndex);
				}
			}

			UriHostNameType hostNameType = Uri.CheckHostName(host);
			return hostNameType != UriHostNameType.Unknown;
		}
	}
}
