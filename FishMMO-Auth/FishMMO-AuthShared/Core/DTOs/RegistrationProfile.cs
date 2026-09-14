using System;
using System.Text;
using FishMMO.Auth.Implementation;

namespace FishMMO.Auth.Core
{
	/// <summary>
	/// Channels a player may choose to verify a new account with. Bit-for-bit the same values as the
	/// database's <c>AccountVerificationChannels</c>, which this assembly does not reference.
	/// </summary>
	/// <remarks>
	/// A code goes out on every chosen channel the server verifies, and any one of them verifies the account.
	/// </remarks>
	[Flags]
	public enum RegistrationVerificationChannels : byte
	{
		/// <summary>Nothing chosen. The server treats it as <see cref="Email"/>.</summary>
		None = 0,
		/// <summary>A code sent by email.</summary>
		Email = 1,
		/// <summary>A code sent by SMS. Requires a phone number.</summary>
		Sms = 2,
		/// <summary>A code sent once, as a direct message from the game's Discord bot. Requires a Discord username.</summary>
		Discord = 4,
	}

	/// <summary>
	/// The optional details a player gives at in-game registration, carried to the login server as
	/// ONE additional AES-GCM field of the create-account message.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why one field and not seven.</b> Every field of the create-account message is encrypted
	/// under its own sequence number, and the server decrypts them by counting back from the
	/// message's last sequence. Seven new fields would have been seven new offsets to keep in step
	/// on both sides; one field holding a compact serialisation adds one, and the next detail to be
	/// added changes <see cref="FormatVersion"/> rather than the wire arithmetic.
	/// </para>
	/// <para>
	/// <b>Format.</b> One version byte, one channels byte, then strings in a fixed order — phone, beta
	/// code, country, real name, address, referral account, and from version 2 the Discord username —
	/// each as a two-byte big-endian length followed by that many bytes of UTF-8. An empty string means
	/// "not given". Version 1 (six strings) is still read, so a client one release behind can register.
	/// Decoding refuses an unknown version, trailing bytes, invalid UTF-8 and any field over its length,
	/// so the size of what reaches the server's validator is bounded before it is parsed.
	/// </para>
	/// <para>
	/// <b>Validation.</b> The database's <c>AccountProfileRules</c> is the authority and the server
	/// applies it before the account row is created. <see cref="TryValidate"/> is a client-side
	/// mirror of the parts a form needs for immediate feedback; the client does not reference the
	/// database assembly. A unit test pins the shared limits to the database's.
	/// </para>
	/// </remarks>
	public sealed class RegistrationProfile
	{
		/// <summary>The serialisation version written by <see cref="Serialize"/>.</summary>
		public const byte FormatVersion = 2;

		/// <summary>The oldest serialisation version <see cref="TryDeserialize"/> still reads.</summary>
		public const byte OldestFormatVersion = 1;

		/// <summary>Longest phone number accepted as typed, before normalisation.</summary>
		public const int MaxPhoneLength = 32;
		/// <summary>Longest beta code accepted as typed (the canonical form is 14 characters).</summary>
		public const int MaxBetaCodeLength = 32;
		/// <summary>Longest country or region. Mirrors <c>AccountProfileRules.MaxCountryLength</c>.</summary>
		public const int MaxCountryLength = 64;
		/// <summary>Longest real name. Mirrors <c>AccountProfileRules.MaxRealNameLength</c>.</summary>
		public const int MaxRealNameLength = 128;
		/// <summary>Longest address. Mirrors <c>AccountProfileRules.MaxAddressLength</c>.</summary>
		public const int MaxAddressLength = 512;
		/// <summary>Longest referral account name (an account name is at most 32 characters).</summary>
		public const int MaxReferralLength = 32;
		/// <summary>
		/// Longest Discord username accepted as typed: a 32-character name, <c>#</c> and a four-digit
		/// discriminator, with room for a leading <c>@</c> and stray spaces before normalisation.
		/// </summary>
		public const int MaxDiscordUsernameLength = 40;

		/// <summary>Shortest Discord username. Mirrors <c>AccountProfileRules.MinDiscordUsernameLength</c>.</summary>
		public const int MinDiscordNameLength = 2;
		/// <summary>Longest Discord username, not counting a discriminator. Mirrors <c>AccountProfileRules.MaxDiscordUsernameLength</c>.</summary>
		public const int MaxDiscordNameLength = 32;

		/// <summary>Largest serialisation any valid profile can produce: every field full of 4-byte UTF-8.</summary>
		public const int MaxSerializedBytes = 2 + (7 * 2) +
			(4 * (MaxPhoneLength + MaxBetaCodeLength + MaxCountryLength + MaxRealNameLength + MaxAddressLength + MaxReferralLength + MaxDiscordUsernameLength));

		/// <summary>Largest encrypted profile field a server should accept (serialisation plus the GCM tag, with headroom).</summary>
		public const int MaxEncryptedBytes = MaxSerializedBytes + 64;

		/// <summary>Phone number as typed, or null.</summary>
		public string? Phone { get; set; }
		/// <summary>Beta code as typed, or null.</summary>
		public string? BetaCode { get; set; }
		/// <summary>Country or region, or null.</summary>
		public string? Country { get; set; }
		/// <summary>Real name, or null.</summary>
		public string? RealName { get; set; }
		/// <summary>Postal address, or null.</summary>
		public string? Address { get; set; }
		/// <summary>The account that referred this player, or null.</summary>
		public string? ReferralAccount { get; set; }
		/// <summary>The Discord username the verification DM goes to, as typed, or null.</summary>
		public string? DiscordUsername { get; set; }
		/// <summary>The channels the player chose to verify with.</summary>
		public RegistrationVerificationChannels VerificationChannels { get; set; } = RegistrationVerificationChannels.Email;

		/// <summary>
		/// Serialises the profile (format version <see cref="FormatVersion"/>).
		/// </summary>
		/// <exception cref="ArgumentException">A field is over its length limit.</exception>
		public byte[] Serialize()
		{
			string[] fields = Fields();
			int[] limits = Limits(FormatVersion);
			byte[][] encoded = new byte[fields.Length][];
			int total = 2;
			for (int i = 0; i < fields.Length; ++i)
			{
				string value = fields[i] ?? string.Empty;
				if (value.Length > limits[i])
				{
					throw new ArgumentException($"Registration profile field {i} is over {limits[i]} characters.");
				}
				encoded[i] = Encoding.UTF8.GetBytes(value);
				total += 2 + encoded[i].Length;
			}

			byte[] buffer = new byte[total];
			buffer[0] = FormatVersion;
			buffer[1] = (byte)VerificationChannels;
			int offset = 2;
			for (int i = 0; i < encoded.Length; ++i)
			{
				int length = encoded[i].Length;
				buffer[offset++] = (byte)(length >> 8);
				buffer[offset++] = (byte)length;
				Buffer.BlockCopy(encoded[i], 0, buffer, offset, length);
				offset += length;
			}
			return buffer;
		}

		/// <summary>
		/// Decodes a serialised profile, refusing anything <see cref="Serialize"/> could not have produced.
		/// </summary>
		/// <param name="data">The decrypted bytes.</param>
		/// <param name="profile">The profile; blank fields are null.</param>
		/// <returns>False for an unknown version, a truncated or over-long buffer, invalid UTF-8, or a field over its limit.</returns>
		public static bool TryDeserialize(byte[]? data, out RegistrationProfile profile)
		{
			profile = new RegistrationProfile();
			if (data == null || data.Length < 2 || data.Length > MaxSerializedBytes ||
				data[0] < OldestFormatVersion || data[0] > FormatVersion)
			{
				return false;
			}

			int[] limits = Limits(data[0]);
			if (data.Length < 2 + (limits.Length * 2))
			{
				return false;
			}

			profile.VerificationChannels = (RegistrationVerificationChannels)data[1];
			string?[] values = new string?[limits.Length];
			int offset = 2;
			for (int i = 0; i < limits.Length; ++i)
			{
				if (offset + 2 > data.Length)
				{
					return false;
				}
				int length = (data[offset] << 8) | data[offset + 1];
				offset += 2;
				if (length > limits[i] * 4 || offset + length > data.Length)
				{
					return false;
				}
				string text;
				try
				{
					text = CryptoHelper.StrictUtf8.GetString(data, offset, length);
				}
				catch (DecoderFallbackException)
				{
					return false;
				}
				if (text.Length > limits[i])
				{
					return false;
				}
				values[i] = text.Length == 0 ? null : text;
				offset += length;
			}
			if (offset != data.Length)
			{
				return false;
			}

			profile.Phone = values[0];
			profile.BetaCode = values[1];
			profile.Country = values[2];
			profile.RealName = values[3];
			profile.Address = values[4];
			profile.ReferralAccount = values[5];
			profile.DiscordUsername = values.Length > 6 ? values[6] : null;
			return true;
		}

		/// <summary>
		/// Client-side mirror of the database's profile rules, for immediate feedback on a form.
		/// </summary>
		/// <remarks>
		/// Not the authority — the login server re-validates with <c>AccountProfileRules</c> — so it
		/// errs towards the same answers rather than towards being complete. The messages match the
		/// database's word for word where the rule is the same.
		/// </remarks>
		/// <param name="error">Why the profile was refused, written for the player.</param>
		public bool TryValidate(out string? error)
		{
			error = null;

			if (!string.IsNullOrWhiteSpace(Phone) && NormalizePhone(Phone) == null)
			{
				error = "Enter the phone number with its country code, for example +44 7700 900123.";
				return false;
			}

			if (!TryText(RealName, MaxRealNameLength, "Real name", false, out error) ||
				!TryText(Country, MaxCountryLength, "Country or region", false, out error) ||
				!TryText(Address, MaxAddressLength, "Address", true, out error))
			{
				return false;
			}

			if (!string.IsNullOrWhiteSpace(ReferralAccount) &&
				!FishMMO.Shared.Authentication.IsAllowedUsername(ReferralAccount!.Trim()))
			{
				error = "The referring account name is not a valid account name.";
				return false;
			}

			if (!string.IsNullOrWhiteSpace(DiscordUsername) &&
				(DiscordUsername!.Length > MaxDiscordUsernameLength || NormalizeDiscordUsername(DiscordUsername) == null))
			{
				error = "Enter your Discord username as it appears on your profile, for example fishfan or FishFan#1234.";
				return false;
			}

			if (!string.IsNullOrWhiteSpace(BetaCode))
			{
				string code = BetaCode!.Trim();
				if (code.Length > MaxBetaCodeLength)
				{
					error = "That beta code is not valid.";
					return false;
				}
				foreach (char c in code)
				{
					bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == ' ';
					if (!ok)
					{
						error = "That beta code is not valid.";
						return false;
					}
				}
			}

			RegistrationVerificationChannels channels = VerificationChannels &
				(RegistrationVerificationChannels.Email | RegistrationVerificationChannels.Sms | RegistrationVerificationChannels.Discord);
			if (channels == RegistrationVerificationChannels.None)
			{
				error = "Choose how to verify your account: by email, by SMS, or by Discord.";
				return false;
			}
			if ((channels & RegistrationVerificationChannels.Sms) != 0 && string.IsNullOrWhiteSpace(Phone))
			{
				error = "Verifying by SMS needs a phone number.";
				return false;
			}
			if ((channels & RegistrationVerificationChannels.Discord) != 0 && string.IsNullOrWhiteSpace(DiscordUsername))
			{
				error = "Verifying by Discord needs your Discord username.";
				return false;
			}
			return true;
		}

		/// <summary>
		/// Mirror of <c>AccountProfileRules.NormalizePhone</c>: E.164, a leading <c>+</c> (or <c>00</c>)
		/// and 8 to 15 digits, with spaces, hyphens, dots and parentheses dropped.
		/// </summary>
		/// <returns>The normalised number, or null when it is not one.</returns>
		public static string? NormalizePhone(string? input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return null;
			}

			string trimmed = input!.Trim();
			int start;
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

			StringBuilder digits = new StringBuilder(16);
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
		/// Mirror of <c>AccountProfileRules.NormalizeDiscordUsername</c>: a unique username (<c>fishfan</c>)
		/// or a name with its four-digit discriminator (<c>FishFan#1234</c>), trimmed, without a leading
		/// <c>@</c>, lowercased.
		/// </summary>
		/// <returns>The normalised username, or null when it is not one.</returns>
		public static string? NormalizeDiscordUsername(string? input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return null;
			}

			string name = input!.Trim();
			if (name.StartsWith("@", StringComparison.Ordinal))
			{
				name = name.Substring(1);
			}

			int hash = name.LastIndexOf('#');
			if (hash < 0)
			{
				name = name.ToLowerInvariant();
				if (name.Length < MinDiscordNameLength || name.Length > MaxDiscordNameLength ||
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
			if (legacyName.Length < MinDiscordNameLength || legacyName.Length > MaxDiscordNameLength ||
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

		private string?[] FieldsNullable() => new[] { Phone, BetaCode, Country, RealName, Address, ReferralAccount, DiscordUsername };

		private string[] Fields()
		{
			string?[] raw = FieldsNullable();
			string[] trimmed = new string[raw.Length];
			for (int i = 0; i < raw.Length; ++i)
			{
				trimmed[i] = string.IsNullOrWhiteSpace(raw[i]) ? string.Empty : raw[i]!.Trim();
			}
			return trimmed;
		}

		/// <summary>The per-field limits of one format version, in field order.</summary>
		private static int[] Limits(byte version) => version >= 2
			? new[] { MaxPhoneLength, MaxBetaCodeLength, MaxCountryLength, MaxRealNameLength, MaxAddressLength, MaxReferralLength, MaxDiscordUsernameLength }
			: new[] { MaxPhoneLength, MaxBetaCodeLength, MaxCountryLength, MaxRealNameLength, MaxAddressLength, MaxReferralLength };

		private static bool TryText(string? input, int max, string label, bool allowLineBreaks, out string? error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(input))
			{
				return true;
			}
			string trimmed = input!.Trim();
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
			return true;
		}
	}
}
