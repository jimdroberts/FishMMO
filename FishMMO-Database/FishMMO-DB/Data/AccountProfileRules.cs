using System;
using System.Text;
using FishMMO.Database.Data.Enums;
using FishMMO.Shared;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The rules for the optional contact and identity details on an account.
	/// </summary>
	/// <remarks>
	/// One copy, here, because three surfaces accept these fields — the game's account creation, the
	/// Control Panel's registration, and the panel's self-service — and the database is the one layer
	/// all three write through. A rule held in each would be three rules by the second release.
	/// </remarks>
	public static class AccountProfileRules
	{
		/// <summary>Longest real name accepted.</summary>
		public const int MaxRealNameLength = 128;

		/// <summary>Longest country or region accepted.</summary>
		public const int MaxCountryLength = 64;

		/// <summary>Longest postal address accepted.</summary>
		public const int MaxAddressLength = 512;

		/// <summary>Shortest Discord username Discord allows.</summary>
		public const int MinDiscordUsernameLength = 2;

		/// <summary>Longest Discord username Discord allows, not counting a discriminator.</summary>
		public const int MaxDiscordUsernameLength = 32;

		/// <summary>Longest stored Discord name: a username, <c>#</c>, and a four-digit discriminator.</summary>
		public const int MaxDiscordTagLength = MaxDiscordUsernameLength + 5;

		/// <summary>The one refusal for a Discord username that is not one.</summary>
		public const string InvalidDiscordUsernameError =
			"Enter your Discord username as it appears on your profile, for example fishfan or FishFan#1234.";

		/// <summary>
		/// Normalises a Discord username in either form Discord uses: a unique username (<c>fishfan</c>), or
		/// a name with its four-digit discriminator (<c>FishFan#1234</c>).
		/// </summary>
		/// <remarks>
		/// <para>
		/// Trimmed, a leading <c>@</c> dropped, and lowercased, because Discord matches both forms without
		/// regard to case.
		/// </para>
		/// <para>
		/// A unique username is 2 to 32 lowercase letters, digits, underscores and periods, never two periods
		/// together. A name with a discriminator is the older form, whose names were freer — capitals, spaces,
		/// most of Unicode — so its name part is held only to its length and to what Discord never allowed in
		/// one: <c>@</c>, <c>#</c>, <c>:</c>, a code fence, control characters. The discriminator is exactly
		/// four digits.
		/// </para>
		/// <para>
		/// Anything in neither form is refused rather than guessed at: the bot looks the name up exactly, and
		/// a DM sent to the wrong person is a verification code in a stranger's hands.
		/// </para>
		/// </remarks>
		/// <returns>The normalised username, or null when it is not one.</returns>
		public static string? NormalizeDiscordUsername(string? input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return null;
			}

			string name = input.Trim();
			if (name.StartsWith("@", StringComparison.Ordinal))
			{
				name = name.Substring(1);
			}

			int hash = name.LastIndexOf('#');
			if (hash < 0)
			{
				name = name.ToLowerInvariant();
				if (name.Length < MinDiscordUsernameLength || name.Length > MaxDiscordUsernameLength ||
					name.IndexOf("..", StringComparison.Ordinal) >= 0)
				{
					return null;
				}
				foreach (char c in name)
				{
					bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
					if (!ok)
					{
						return null;
					}
				}
				return name;
			}

			string legacyName = name.Substring(0, hash).Trim();
			string discriminator = name.Substring(hash + 1);
			if (discriminator.Length != 4)
			{
				return null;
			}
			foreach (char c in discriminator)
			{
				if (c < '0' || c > '9')
				{
					return null;
				}
			}
			if (legacyName.Length < MinDiscordUsernameLength || legacyName.Length > MaxDiscordUsernameLength ||
				legacyName.IndexOf("```", StringComparison.Ordinal) >= 0)
			{
				return null;
			}
			foreach (char c in legacyName)
			{
				if (char.IsControl(c) || c == '@' || c == '#' || c == ':')
				{
					return null;
				}
			}
			return legacyName.ToLowerInvariant() + "#" + discriminator;
		}

		/// <summary>
		/// Normalises a phone number to E.164: a leading <c>+</c> and 8 to 15 digits.
		/// </summary>
		/// <remarks>
		/// Spaces, hyphens, dots and parentheses are dropped, because that is how people write numbers.
		/// A leading <c>00</c> becomes <c>+</c>. A number without a country code is refused rather than
		/// guessed at: an SMS sent to the wrong country's number is a verification code in a stranger's
		/// hands.
		/// </remarks>
		/// <returns>The normalised number, or null when it is not one.</returns>
		public static string? NormalizePhone(string? input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return null;
			}

			var digits = new StringBuilder(16);
			string trimmed = input.Trim();
			int start = 0;
			if (trimmed.StartsWith("+", StringComparison.Ordinal))
			{
				start = 1;
			}
			else if (trimmed.StartsWith("00", StringComparison.Ordinal))
			{
				start = 2;
			}
			else
			{
				return null;
			}

			for (int i = start; i < trimmed.Length; ++i)
			{
				char c = trimmed[i];
				if (c >= '0' && c <= '9')
				{
					digits.Append(c);
				}
				else if (c != ' ' && c != '-' && c != '.' && c != '(' && c != ')')
				{
					return null;
				}
			}

			if (digits.Length < 8 || digits.Length > 15 || digits[0] == '0')
			{
				return null;
			}
			return "+" + digits;
		}

		/// <summary>
		/// Validates and normalises a profile. Blank optional fields become null.
		/// </summary>
		/// <param name="input">What was submitted.</param>
		/// <param name="clean">The profile to store.</param>
		/// <param name="error">Why it was refused, written for the person who filled the form in.</param>
		public static bool TryValidate(AccountProfileData input, out AccountProfileData clean, out string error)
		{
			clean = new AccountProfileData();
			error = null;

			if (input == null)
			{
				error = "A profile is required.";
				return false;
			}

			if (!string.IsNullOrWhiteSpace(input.Phone))
			{
				clean.Phone = NormalizePhone(input.Phone);
				if (clean.Phone == null)
				{
					error = "Enter the phone number with its country code, for example +44 7700 900123.";
					return false;
				}
			}

			if (!TryText(input.RealName, MaxRealNameLength, "Real name", out string realName, out error) ||
				!TryText(input.Country, MaxCountryLength, "Country or region", out string country, out error) ||
				!TryText(input.Address, MaxAddressLength, "Address", out string address, out error, allowLineBreaks: true))
			{
				return false;
			}
			clean.RealName = realName;
			clean.Country = country;
			clean.Address = address;

			if (!string.IsNullOrWhiteSpace(input.ReferralAccount))
			{
				string referral = input.ReferralAccount.Trim();
				if (!Authentication.IsAllowedUsername(referral))
				{
					error = "The referring account name is not a valid account name.";
					return false;
				}
				clean.ReferralAccount = referral.ToLowerInvariant();
			}

			if (!string.IsNullOrWhiteSpace(input.DiscordUsername))
			{
				clean.DiscordUsername = NormalizeDiscordUsername(input.DiscordUsername);
				if (clean.DiscordUsername == null)
				{
					error = InvalidDiscordUsernameError;
					return false;
				}
			}

			AccountVerificationChannels channels = input.VerificationChannels & AccountVerificationRules.All;
			if ((channels & AccountVerificationChannels.Sms) != 0 && clean.Phone == null)
			{
				error = "Verifying by SMS needs a phone number.";
				return false;
			}
			if ((channels & AccountVerificationChannels.Discord) != 0 && clean.DiscordUsername == null)
			{
				error = "Verifying by Discord needs your Discord username.";
				return false;
			}
			clean.VerificationChannels = channels;
			return true;
		}

		/// <summary>Trims optional free text, refusing control characters and anything over length.</summary>
		private static bool TryText(string? input, int max, string label, out string? value, out string? error, bool allowLineBreaks = false)
		{
			value = null;
			error = null;
			if (string.IsNullOrWhiteSpace(input))
			{
				return true;
			}

			string trimmed = input.Trim();
			if (trimmed.Length > max)
			{
				error = $"{label} must be {max} characters or fewer.";
				return false;
			}
			foreach (char c in trimmed)
			{
				if (char.IsControl(c) && !(allowLineBreaks && (c == '\n' || c == '\r')))
				{
					error = $"{label} contains characters that cannot be stored.";
					return false;
				}
			}
			value = trimmed;
			return true;
		}
	}
}
