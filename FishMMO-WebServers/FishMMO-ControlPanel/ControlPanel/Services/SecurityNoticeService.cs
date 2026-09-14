using System.Globalization;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Queues the security notices an account holder is owed: a sign-in lock, and every step of a
	/// self-service two-factor reset.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every notice is <see cref="EmailKind.SecurityNotice"/>. That is load-bearing: the drain stamps
	/// <c>verification_email_sent_at</c> only for a verification mail, and a notice sent to an
	/// unverified account must not end its grace period.
	/// </para>
	/// <para>
	/// Best effort. The action the notice describes has already happened; a notice that could not be
	/// queued is logged, never turned into a failure of the action.
	/// </para>
	/// <para>
	/// Nothing here says which address or number is on file, and none of it is ever returned to an
	/// anonymous caller: the lockout notice is queued from a path whose HTTP answer is identical
	/// whether or not anything was sent.
	/// </para>
	/// </remarks>
	public sealed class SecurityNoticeService
	{
		private readonly IAccountService accounts;
		private readonly IEmailQueueService emailQueue;
		private readonly ILogger<SecurityNoticeService> log;

		public SecurityNoticeService(IAccountService accounts, IEmailQueueService emailQueue, ILogger<SecurityNoticeService> log)
		{
			this.accounts = accounts;
			this.emailQueue = emailQueue;
			this.log = log;
		}

		/// <summary>A sign-in step has just been locked after repeated failures.</summary>
		public Task SignInLockedAsync(string username, AuthFailureKind kind, DateTime lockedUntilUtc, CancellationToken cancellationToken = default)
		{
			string step = kind == AuthFailureKind.Password ? "password" : "two-factor code";
			return SendAsync(username, "FishMMO - Sign-in temporarily locked",
				$"Hello {username},\n\n" +
				$"There were repeated failed attempts to sign in to your FishMMO account with the wrong {step}, " +
				$"so that step is locked until {Format(lockedUntilUtc)}.\n\n" +
				"If this was you, wait until then and try again.\n\n" +
				(kind == AuthFailureKind.Password
					? "If it was not you, somebody may be guessing your password. Nothing has been changed and nobody has signed in. " +
					  "Consider choosing a new password once the lock lifts.\n"
					: "If it was not you, somebody already knows your PASSWORD and is guessing your authenticator code. " +
					  "Change your password as soon as the lock lifts.\n"),
				cancellationToken);
		}

		/// <summary>A delayed two-factor reset was requested.</summary>
		public Task ResetRequestedAsync(string username, DateTime effectiveUtc, string requestedIp, CancellationToken cancellationToken = default)
		{
			return SendAsync(username, "FishMMO - Two-factor reset requested",
				$"Hello {username},\n\n" +
				"Somebody signed in to your FishMMO account with your password and asked to reset its two-factor " +
				"authentication, saying the authenticator and every recovery code are lost.\n\n" +
				$"The reset takes effect on {Format(effectiveUtc)}. After that, whoever holds the password can " +
				"remove your authenticator and enrol a new one.\n\n" +
				(string.IsNullOrWhiteSpace(requestedIp) ? "" : $"The request came from {requestedIp}.\n\n") +
				"If this was not you: sign in with your authenticator or one of your recovery codes before then. " +
				"That cancels the reset immediately. Then change your password, because the person who asked knows it. " +
				"You can also contact support.\n",
				cancellationToken);
		}

		/// <summary>A pending two-factor reset was cancelled, by the owner signing in or by staff.</summary>
		public Task ResetCancelledAsync(string username, bool byStaff, CancellationToken cancellationToken = default)
		{
			return SendAsync(username, "FishMMO - Two-factor reset cancelled",
				$"Hello {username},\n\n" +
				(byStaff
					? "Support staff cancelled the pending two-factor reset on your FishMMO account.\n\n"
					: "The pending two-factor reset on your FishMMO account was cancelled, because the account signed in " +
					  "with its authenticator or a recovery code.\n\n") +
				"Your two-factor authentication is unchanged.\n\n" +
				"If you did not ask for the reset in the first place, somebody knows your password. Change it.\n",
				cancellationToken);
		}

		/// <summary>Staff brought a pending reset's effective time forward.</summary>
		public Task ResetShortenedAsync(string username, DateTime effectiveUtc, CancellationToken cancellationToken = default)
		{
			return SendAsync(username, "FishMMO - Two-factor reset brought forward",
				$"Hello {username},\n\n" +
				$"Support staff brought the pending two-factor reset on your FishMMO account forward. It now takes effect on {Format(effectiveUtc)}.\n\n" +
				"If you did not ask for this, sign in with your authenticator or a recovery code now to cancel it, and contact support.\n",
				cancellationToken);
		}

		/// <summary>A two-factor reset was completed: the old authenticator and recovery codes no longer work.</summary>
		public Task ResetCompletedAsync(string username, CancellationToken cancellationToken = default)
		{
			return SendAsync(username, "FishMMO - Two-factor reset completed",
				$"Hello {username},\n\n" +
				"The two-factor reset on your FishMMO account was completed. Your previous authenticator and every " +
				"previous recovery code no longer work, a new authenticator is being enrolled, and every other browser " +
				"and game client was signed out.\n\n" +
				"If this was not you, your account has been taken over. Contact support immediately.\n",
				cancellationToken);
		}

		private async Task SendAsync(string username, string subject, string body, CancellationToken cancellationToken)
		{
			try
			{
				var account = await accounts.FetchForLoginAsync(username, false, cancellationToken);
				if (!account.IsSuccess || string.IsNullOrWhiteSpace(account.Data.Email))
				{
					log.LogInformation("No security notice for '{User}' ('{Subject}'): no eligible account or no address on file.", username, subject);
					return;
				}

				var queued = await emailQueue.EnqueueAsync(
					account.Data.Email, account.Data.Name, subject, body, EmailKind.SecurityNotice, cancellationToken);
				if (!queued.IsSuccess)
				{
					log.LogWarning("Could not queue the security notice '{Subject}' for '{User}': [{Code}] {Message}",
						subject, username, queued.ErrorCode, queued.ErrorMessage);
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				log.LogError(ex, "Security notice '{Subject}' for '{User}' threw.", subject, username);
			}
		}

		/// <summary>A UTC instant, written so nobody has to guess the zone.</summary>
		private static string Format(DateTime utc) =>
			DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("dddd d MMMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);
	}
}
