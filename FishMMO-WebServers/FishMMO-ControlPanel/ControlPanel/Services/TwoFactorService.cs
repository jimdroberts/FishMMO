using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Verifies TOTP codes and recovery codes, mirroring
	/// <c>ServerAuthenticator.VerifyTotpCodeCoreAsync</c> exactly.
	/// </summary>
	/// <remarks>
	/// Identical rules to the game path, deliberately: the recovery-code shape test, the replay
	/// window persistence, the first-verification stamp, and single-use consumption. A code that
	/// works in the client works here, and one consumed here is gone there too.
	/// </remarks>
	public sealed class TwoFactorService
	{
		private readonly IAccountService accounts;
		private readonly ITwoFactorRecoveryCodeService recoveryCodes;
		private readonly TotpKeyProvider totpKeys;
		private readonly ILogger<TwoFactorService> log;

		public TwoFactorService(
			IAccountService accounts,
			ITwoFactorRecoveryCodeService recoveryCodes,
			TotpKeyProvider totpKeys,
			ILogger<TwoFactorService> log)
		{
			this.accounts = accounts;
			this.recoveryCodes = recoveryCodes;
			this.totpKeys = totpKeys;
			this.log = log;
		}

		/// <summary>
		/// Verifies a TOTP code or a recovery code for an account.
		/// </summary>
		/// <returns><c>true</c> when the code was valid and any state it consumes was persisted.</returns>
		public async Task<bool> VerifyAsync(string username, string code, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
			{
				return false;
			}

			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("TOTP master KEK unavailable; no code can be verified. {Error}", totpKeys.LoadError);
				return false;
			}

			var accountResult = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			if (!accountResult.IsSuccess || string.IsNullOrEmpty(accountResult.Data.TotpSecret))
			{
				return false;
			}

			code = code.Trim();

			if (CryptoHelper.TwoFactor.LooksLikeRecoveryCode(code))
			{
				var codesResult = await recoveryCodes.FetchUnusedByAccountAsync(username, cancellationToken);
				if (!codesResult.IsSuccess || codesResult.Data == null || codesResult.Data.Count == 0)
				{
					return false;
				}

				string matchedHash = null;
				foreach (var stored in codesResult.Data)
				{
					if (CryptoHelper.TwoFactor.VerifyRecoveryCode(username, code, stored.CodeHash))
					{
						matchedHash = stored.CodeHash;
						break;
					}
				}
				if (matchedHash == null)
				{
					return false;
				}

				// Single use: the consume is what makes it so, and a failed consume must not
				// count as a successful verification.
				var consume = await recoveryCodes.ConsumeCodeAsync(username, matchedHash, cancellationToken);
				return consume.IsSuccess;
			}

			byte[] plaintextSecret = null;
			try
			{
				plaintextSecret = CryptoHelper.TwoFactor.DecryptTotpSecret(masterKek, username, accountResult.Data.TotpSecret);
				var (valid, windowUsed) = CryptoHelper.TwoFactor.VerifyTotpCode(
					plaintextSecret, code, accountResult.Data.LastTotpWindow);
				if (!valid)
				{
					return false;
				}

				// Persisting the window is the replay guard, so a failure to persist is a failed
				// verification rather than a successful one with a hole in it.
				var persist = accountResult.Data.TotpVerifiedAt == null
					? await accounts.PersistTotpVerifiedAtAsync(username, windowUsed, cancellationToken)
					: await accounts.PersistLastTotpWindowAsync(username, windowUsed, cancellationToken);
				return persist.IsSuccess;
			}
			catch (CryptographicException ex)
			{
				// A stored envelope that will not open under the deployment KEK. Before the KEK
				// became a deployment secret this was the normal state of every account after a
				// LoginServer restart.
				log.LogError(ex, "Could not decrypt the stored TOTP secret for '{User}'.", username);
				return false;
			}
			finally
			{
				if (plaintextSecret != null)
				{
					CryptographicOperations.ZeroMemory(plaintextSecret);
				}
			}
		}
	}
}
