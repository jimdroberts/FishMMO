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
	/// Completion is one transaction: the request is spent together with the new factor, or not at all.
	/// A failure part-way leaves the request pending and the old factor in place.
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
		private readonly IAuthTokenService authTokens;
		private readonly IWebSessionService webSessions;
		private readonly SelfServiceService selfService;
		private readonly SecurityNoticeService notices;
		private readonly IUnitOfWorkService unitOfWork;
		private readonly TotpKeyProvider totpKeys;
		private readonly TwoFactorResetOptions resetOptions;
		private readonly ILogger<TwoFactorResetFlow> log;

		public TwoFactorResetFlow(
			ITwoFactorResetRequestService requests,
			IAuthTokenService authTokens,
			IWebSessionService webSessions,
			SelfServiceService selfService,
			SecurityNoticeService notices,
			IUnitOfWorkService unitOfWork,
			TotpKeyProvider totpKeys,
			TwoFactorResetOptions resetOptions,
			ILogger<TwoFactorResetFlow> log)
		{
			this.requests = requests;
			this.authTokens = authTokens;
			this.webSessions = webSessions;
			this.selfService = selfService;
			this.notices = notices;
			this.unitOfWork = unitOfWork;
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
		/// <param name="SignedOutElsewhere">
		/// Whether every game token and every other panel session was revoked. The reset stands either
		/// way; the player is told plainly when something may still be signed in.
		/// </param>
		public sealed record Completion(
			CompletionStatus Status,
			string Error,
			DateTime? EffectiveUtc,
			string OtpauthUri,
			IReadOnlyList<string> RecoveryCodes,
			bool SignedOutElsewhere = false);

		/// <summary>
		/// The pending request: <c>Read</c> is false when the database could not be asked, which is not
		/// the same answer as a successful read that found no request.
		/// </summary>
		/// <remarks>
		/// This used to return a bare null for both, and every caller took a failed read for "there is
		/// no reset" — telling the player their request had gone, and letting completion report
		/// NoRequest over a database outage.
		/// </remarks>
		public async Task<(bool Read, TwoFactorResetRequestData Request)> PendingAsync(string username, CancellationToken cancellationToken = default)
		{
			var pending = await requests.FetchPendingAsync(username, cancellationToken);
			if (!pending.IsSuccess)
			{
				log.LogWarning("Could not read the pending two-factor reset for '{User}': [{Code}] {Message}",
					username, pending.ErrorCode, pending.ErrorMessage);
				return (false, null);
			}
			return (true, pending.Data);
		}

		/// <summary>
		/// Opens a request, or returns the one already pending unchanged. Emails the holder only when a
		/// new request was opened.
		/// </summary>
		public async Task<(TwoFactorResetRequestData Request, string Error)> RequestAsync(
			string username, string requestedIp, CancellationToken cancellationToken = default)
		{
			var (read, existing) = await PendingAsync(username, cancellationToken);
			if (!read)
			{
				/* Not a guess that there is none. Opening one anyway would return an existing request
				 * unchanged, but this method would then email the holder as though it were new. */
				return (null, "The reset could not be requested. Try again shortly.");
			}
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
			var (read, pending) = await PendingAsync(username, cancellationToken);
			if (!read)
			{
				return new Completion(CompletionStatus.Failed, "Your reset could not be checked right now. Nothing was changed; try again shortly.", null, null, null);
			}
			if (pending == null)
			{
				return new Completion(CompletionStatus.NoRequest, "There is no pending two-factor reset on this account.", null, null, null);
			}
			if (pending.EffectiveUtc > DateTime.UtcNow)
			{
				return new Completion(CompletionStatus.NotYetEffective, "This reset has not taken effect yet.", pending.EffectiveUtc, null, null);
			}

			/* Checked before a transaction is opened, so the refusal is immediate. The enrolment checks
			 * the key again inside, and a failure there rolls the request's completion back with it. */
			byte[] kek = totpKeys.MasterKek;
			if (kek == null || kek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("Two-factor reset for '{User}' refused: the TOTP master KEK is unavailable.", username);
				return new Completion(CompletionStatus.Failed, "Two-factor is unavailable on this server right now. Your reset is still pending; try again later.", pending.EffectiveUtc, null, null);
			}

			/* ONE transaction: the request is spent, the new secret and recovery codes are written and
			 * two-factor is asserted on, or none of it happens. ITwoFactorResetRequestService says the
			 * factor change belongs in the same unit of work as CompleteAsync, and this is why: done as
			 * separate writes, an enrolment that failed after the request was spent used up the
			 * player's week for nothing and left them to contact support. Now a failure anywhere leaves
			 * the request pending and the old factor untouched, and the player simply tries again. */
			const string NothingChanged = "The reset could not be completed right now. Nothing was changed and your reset is still pending; try again shortly.";
			var begun = await unitOfWork.BeginAsync(cancellationToken);
			if (!begun.IsSuccess)
			{
				log.LogError("Two-factor reset for '{User}' could not begin its transaction: [{Code}] {Message}",
					username, begun.ErrorCode, begun.ErrorMessage);
				return new Completion(CompletionStatus.Failed, NothingChanged, pending.EffectiveUtc, null, null);
			}

			SelfServiceService.TwoFactorSetup setup;
			await using (IUnitOfWork uow = begun.Data)
			{
				// One conditional UPDATE: a request a normal sign-in just cancelled cannot be completed.
				var completed = await requests.CompleteAsync(pending.ID, username, cancellationToken);
				if (!completed.IsSuccess)
				{
					log.LogError("Two-factor reset for '{User}': the request could not be completed: [{Code}] {Message}",
						username, completed.ErrorCode, completed.ErrorMessage);
					return new Completion(CompletionStatus.Failed, NothingChanged, pending.EffectiveUtc, null, null);
				}
				if (!completed.Data)
				{
					return new Completion(CompletionStatus.NoRequest, "This reset is no longer pending.", null, null, null);
				}

				/* The enrolment path the account page uses, with two-factor asserted on in the same
				 * transaction: a new secret overwrites the stored one and the recovery codes are
				 * replaced, so the old factor stops working and a factor is still demanded — by the
				 * game as much as by this panel. */
				setup = await selfService.WriteEnrolmentAsync(username, enable: true, cancellationToken);
				if (!setup.Ok)
				{
					log.LogError("Two-factor reset for '{User}' could not issue a new authenticator ({Error}); the request was left pending.",
						username, setup.Error);
					return new Completion(CompletionStatus.Failed, NothingChanged, pending.EffectiveUtc, null, null);
				}

				var committed = await uow.CommitAsync(cancellationToken);
				if (!committed.IsSuccess)
				{
					log.LogError("Two-factor reset for '{User}' could not be committed; the request was left pending: [{Code}] {Message}",
						username, committed.ErrorCode, committed.ErrorMessage);
					return new Completion(CompletionStatus.Failed, NothingChanged, pending.EffectiveUtc, null, null);
				}
			}

			/* After the commit, deliberately: the new factor is in place and handed over whatever
			 * happens below. A revocation that fails is logged and REPORTED — the handover screen and
			 * the notice both say something may still be signed in — rather than claimed. */
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

			bool signedOut = tokens.IsSuccess && sessions.IsSuccess;
			log.LogWarning("Two-factor reset COMPLETED for '{User}': new authenticator issued; tokens and sessions revoked: {SignedOut}.",
				username, signedOut);
			await notices.ResetCompletedAsync(username, signedOut, cancellationToken);

			return new Completion(CompletionStatus.Completed, null, pending.EffectiveUtc, setup.OtpauthUri, setup.RecoveryCodes, signedOut);
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
