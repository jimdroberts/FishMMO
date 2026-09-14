using FishMMO.Auth.Implementation;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// The self-service two-factor reset, for an account holder who has lost the authenticator AND
	/// every recovery code.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The rules this exists to keep.</b>
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// Only a session that has already proven the password may ask, and asking starts a waiting
	/// period (<see cref="TwoFactorResetOptions.DelayDays"/>). The holder is emailed immediately; any
	/// successful sign-in with the real factor during the wait cancels it
	/// (<see cref="TwoFactorService.VerifyForSignInAsync"/>). A stolen password alone therefore buys a
	/// week's notice to the real owner, not an account.
	/// </description></item>
	/// <item><description>
	/// Asking again returns the same request. The clock is never restarted (that would let an attacker
	/// keep the owner's cancellation window open forever) and never shortened.
	/// </description></item>
	/// <item><description>
	/// Completing a reset never satisfies two-factor. It replaces the factor and hands the new one
	/// over; the session is promoted only after a code from the NEW authenticator is typed.
	/// </description></item>
	/// <item><description>
	/// The account is never left with two-factor cleared. The new secret overwrites the old one while
	/// <c>totp_enabled</c> stays on, and the recovery codes are replaced in the same step, so the game
	/// login demands the new authenticator from the moment the reset completes. Clearing first and
	/// enrolling second would leave a window — or, on a failed write, an account — with the password as
	/// the only thing in front of it.
	/// </description></item>
	/// <item><description>
	/// A password reset never satisfies two-factor and a recovery code never resets a password: nothing
	/// here touches the SRP credentials, and <see cref="PasswordResetService"/> touches nothing here.
	/// </description></item>
	/// </list>
	/// </remarks>
	public sealed class TwoFactorResetFlow
	{
		private readonly ITwoFactorResetRequestService requests;
		private readonly IAccountService accounts;
		private readonly IAuthTokenService authTokens;
		private readonly IWebSessionService webSessions;
		private readonly SelfServiceService selfService;
		private readonly SecurityNoticeService notices;
		private readonly TotpKeyProvider totpKeys;
		private readonly TwoFactorResetOptions resetOptions;
		private readonly ILogger<TwoFactorResetFlow> log;

		public TwoFactorResetFlow(
			ITwoFactorResetRequestService requests,
			IAccountService accounts,
			IAuthTokenService authTokens,
			IWebSessionService webSessions,
			SelfServiceService selfService,
			SecurityNoticeService notices,
			TotpKeyProvider totpKeys,
			TwoFactorResetOptions resetOptions,
			ILogger<TwoFactorResetFlow> log)
		{
			this.requests = requests;
			this.accounts = accounts;
			this.authTokens = authTokens;
			this.webSessions = webSessions;
			this.selfService = selfService;
			this.notices = notices;
			this.totpKeys = totpKeys;
			this.resetOptions = resetOptions;
			this.log = log;
		}

		/// <summary>How a completion attempt ended.</summary>
		public enum CompletionStatus
		{
			/// <summary>Replaced: the handover material is in the outcome.</summary>
			Completed,
			/// <summary>No pending request.</summary>
			NoRequest,
			/// <summary>Pending, but its waiting period has not ended.</summary>
			NotYetEffective,
			/// <summary>Something failed; the message says what the player still has.</summary>
			Failed,
		}

		/// <summary>The outcome of a completion, with the new factor when there is one.</summary>
		public sealed record Completion(
			CompletionStatus Status,
			string Error,
			DateTime? EffectiveUtc,
			string OtpauthUri,
			IReadOnlyList<string> RecoveryCodes);

		/// <summary>The pending request, or null. Failures read as null and are logged.</summary>
		public async Task<TwoFactorResetRequestData> PendingAsync(string username, CancellationToken cancellationToken = default)
		{
			var pending = await requests.FetchPendingAsync(username, cancellationToken);
			if (!pending.IsSuccess)
			{
				log.LogWarning("Could not read the pending two-factor reset for '{User}': [{Code}] {Message}",
					username, pending.ErrorCode, pending.ErrorMessage);
				return null;
			}
			return pending.Data;
		}

		/// <summary>
		/// Opens a request, or returns the one already pending unchanged. Emails the holder only when a
		/// new request was opened.
		/// </summary>
		public async Task<(TwoFactorResetRequestData Request, string Error)> RequestAsync(
			string username, string requestedIp, CancellationToken cancellationToken = default)
		{
			TwoFactorResetRequestData existing = await PendingAsync(username, cancellationToken);
			if (existing != null)
			{
				return (existing, null);
			}

			var opened = await requests.RequestAsync(username, resetOptions.Delay, Truncate(requestedIp, 64), cancellationToken);
			if (!opened.IsSuccess)
			{
				log.LogError("Could not open a two-factor reset for '{User}': [{Code}] {Message}",
					username, opened.ErrorCode, opened.ErrorMessage);
				return (null, "The reset could not be requested. Try again shortly.");
			}

			log.LogWarning("Two-factor reset requested for '{User}' from {Ip}; effective {Effective:o}.",
				username, requestedIp, opened.Data.EffectiveUtc);
			await notices.ResetRequestedAsync(username, opened.Data.EffectiveUtc, requestedIp, cancellationToken);
			return (opened.Data, null);
		}

		/// <summary>
		/// Uses an effective request: replaces the authenticator and recovery codes, signs everything
		/// else out, and returns the new factor for the handover screen. Does NOT satisfy two-factor.
		/// </summary>
		/// <remarks>
		/// Every panel session for the account is revoked, the caller's included; the controller issues
		/// the caller a fresh PENDING session, which is what keeps "completing is not passing" true even
		/// at the level of the session row.
		/// </remarks>
		public async Task<Completion> CompleteAsync(string username, CancellationToken cancellationToken = default)
		{
			TwoFactorResetRequestData pending = await PendingAsync(username, cancellationToken);
			if (pending == null)
			{
				return new Completion(CompletionStatus.NoRequest, "There is no pending two-factor reset on this account.", null, null, null);
			}
			if (pending.EffectiveUtc > DateTime.UtcNow)
			{
				return new Completion(CompletionStatus.NotYetEffective, "This reset has not taken effect yet.", pending.EffectiveUtc, null, null);
			}

			/* Checked before the request is spent. Spending it and then finding no key to encrypt a
			 * new secret under would use up the player's week for nothing. */
			byte[] kek = totpKeys.MasterKek;
			if (kek == null || kek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("Two-factor reset for '{User}' refused: the TOTP master KEK is unavailable.", username);
				return new Completion(CompletionStatus.Failed, "Two-factor is unavailable on this server right now. Your reset is still pending; try again later.", pending.EffectiveUtc, null, null);
			}

			// One conditional UPDATE: a request a normal sign-in just cancelled cannot be completed.
			var completed = await requests.CompleteAsync(pending.ID, username, cancellationToken);
			if (!completed.IsSuccess || !completed.Data)
			{
				return new Completion(CompletionStatus.NoRequest, "This reset is no longer pending.", null, null, null);
			}

			/* The enrolment path the account page uses: a new secret overwrites the stored one and the
			 * recovery codes are replaced. totp_enabled is left ON, so the old factor stops working and
			 * a factor is still demanded — by the game as much as by this panel. */
			var setup = await selfService.BeginTwoFactorSetupAsync(username, cancellationToken);
			if (!setup.Ok)
			{
				log.LogError("Two-factor reset for '{User}' was COMPLETED but a new authenticator could not be issued: {Error}. The previous factor is still in place.",
					username, setup.Error);
				return new Completion(CompletionStatus.Failed,
					"The reset was used but a new authenticator could not be issued, so your previous authenticator is unchanged. Contact support.",
					null, null, null);
			}

			var enabled = await accounts.PersistTotpEnabledAsync(username, true, cancellationToken);
			if (!enabled.IsSuccess)
			{
				log.LogError("Two-factor reset for '{User}': could not re-assert totp_enabled: [{Code}] {Message}",
					username, enabled.ErrorCode, enabled.ErrorMessage);
			}

			var tokens = await authTokens.RevokeAllForAccountAsync(username, cancellationToken);
			if (!tokens.IsSuccess)
			{
				log.LogError("Two-factor reset for '{User}' completed but game tokens were NOT revoked: [{Code}] {Message}",
					username, tokens.ErrorCode, tokens.ErrorMessage);
			}

			var sessions = await webSessions.RevokeAllForAccountAsync(username, cancellationToken);
			if (!sessions.IsSuccess)
			{
				log.LogError("Two-factor reset for '{User}' completed but panel sessions were NOT revoked: [{Code}] {Message}",
					username, sessions.ErrorCode, sessions.ErrorMessage);
			}

			log.LogWarning("Two-factor reset COMPLETED for '{User}': new authenticator issued, tokens and sessions revoked.", username);
			await notices.ResetCompletedAsync(username, cancellationToken);

			return new Completion(CompletionStatus.Completed, null, pending.EffectiveUtc, setup.OtpauthUri, setup.RecoveryCodes);
		}

		/// <summary>The request as the browser sees it. Never the IP of whoever asked.</summary>
		public static object Describe(TwoFactorResetRequestData request, TwoFactorResetOptions options) => request == null
			? new { pending = false, delayDays = options.DelayDays }
			: new
			{
				pending = request.Status == TwoFactorResetStatus.Pending,
				delayDays = options.DelayDays,
				requestedUtc = request.RequestedUtc,
				effectiveUtc = request.EffectiveUtc,
				isEffective = request.EffectiveUtc <= DateTime.UtcNow,
			};

		private static string Truncate(string value, int max)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return value.Length <= max ? value : value.Substring(0, max);
		}
	}
}
