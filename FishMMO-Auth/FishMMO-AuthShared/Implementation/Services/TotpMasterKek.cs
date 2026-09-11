using System;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// The deployment-shared 32-byte master KEK that wraps every account's TOTP secret at rest
	/// (see <see cref="CryptoHelper.TwoFactor.EncryptTotpSecret"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> The TOTP master KEK used to be derived from the LoginServer's HMAC
	/// token-signing key via <c>LocalDeriveKmsProvider.DeriveKey("fishmmo-totp-master-key-v1")</c>.
	/// That signing key is generated fresh with <c>CryptoHelper.GenerateKey</c> on every process
	/// start and then upserted over the stored row, and it is never read back. The consequences
	/// were:
	/// </para>
	/// <list type="bullet">
	///   <item><description>Every account's TOTP secret became undecryptable the moment the
	///   LoginServer restarted — and 2FA enrolment is mandatory at account creation, so this
	///   locked players out of their own authenticators.</description></item>
	///   <item><description>A second LoginServer derived a different key, so an account enrolled
	///   on one could not complete 2FA on another.</description></item>
	///   <item><description>Signing-key rotation orphaned every existing secret.</description></item>
	///   <item><description>Nothing outside the enrolling LoginServer process — the Control Panel
	///   included — could ever verify a code.</description></item>
	/// </list>
	/// <para>
	/// The KEK is therefore a deployment secret, provisioned once and shared, exactly as
	/// <c>signing_key_kek</c> already is. It is stored in the <c>deployment_secrets</c> table
	/// under <see cref="DatabaseKey"/> as Base64 of 32 random bytes, and it must never be
	/// rotated without re-wrapping every stored envelope, because rotation invalidates them all.
	/// </para>
	/// <para>
	/// This type deliberately performs no I/O and has no database dependency, so the Unity server
	/// and the ASP.NET Control Panel can each fetch the row through their own service layer and
	/// share one validation and one set of rules.
	/// </para>
	/// </remarks>
	public static class TotpMasterKek
	{
		/// <summary>Row key in the <c>deployment_secrets</c> table.</summary>
		public const string DatabaseKey = "totp_master_kek";

		/// <summary>Required length in bytes. AES-256, matching <c>EncryptTotpSecret</c>'s contract.</summary>
		public const int KeyLength = 32;

		/// <summary>
		/// Validates and decodes a Base64 KEK fetched from <c>deployment_secrets</c>.
		/// </summary>
		/// <param name="base64Value">The stored value, or null/empty when the row is absent.</param>
		/// <param name="key">The decoded 32-byte key on success; otherwise null.</param>
		/// <param name="error">A human-readable reason on failure; otherwise null.</param>
		/// <returns><c>true</c> when a valid key was decoded.</returns>
		public static bool TryDecode(string? base64Value, out byte[]? key, out string? error)
		{
			key = null;
			error = null;

			if (string.IsNullOrWhiteSpace(base64Value))
			{
				error = $"The '{DatabaseKey}' row is missing from deployment_secrets. " +
						"Run the installer's Configure Server Keys step to provision it.";
				return false;
			}

			byte[] decoded;
			try
			{
				decoded = Convert.FromBase64String(base64Value);
			}
			catch (FormatException)
			{
				error = $"The '{DatabaseKey}' value in deployment_secrets is not valid Base64.";
				return false;
			}

			if (decoded.Length != KeyLength)
			{
				error = $"The '{DatabaseKey}' value must decode to exactly {KeyLength} bytes (got {decoded.Length}).";
				return false;
			}

			key = decoded;
			return true;
		}

		/// <summary>
		/// Generates a fresh KEK as a Base64 string, for provisioning tools.
		/// </summary>
		/// <remarks>
		/// Generating a second one for a deployment that already has accounts enrolled makes every
		/// stored TOTP secret undecryptable. Provisioning must be create-if-absent, never
		/// overwrite.
		/// </remarks>
		public static string Generate()
		{
			return Convert.ToBase64String(CryptoHelper.GenerateKey(KeyLength));
		}
	}
}
