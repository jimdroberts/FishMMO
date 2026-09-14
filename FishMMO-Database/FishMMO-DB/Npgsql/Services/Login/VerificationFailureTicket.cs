using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Counts an incorrect verification code, and opens a support ticket for the account when the count
	/// reaches <see cref="AccountVerificationRules.FailedCodesBeforeTicket"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Shared by the login server and the Control Panel, which both accept codes, and counted in the
	/// database, so three wrong codes open a ticket wherever they were typed.
	/// </para>
	/// <para>
	/// The ticket is filed as the account itself, so the player can follow it from <c>/tickets</c> and the
	/// panel, and the player can read everything in it. The body therefore says what state delivery is in
	/// without repeating the email address, the phone number or the code.
	/// </para>
	/// <para>
	/// It is opened when the count reaches the threshold exactly, so a player who keeps guessing does not
	/// open a ticket per guess. An account unknown to the database, or already verified, counts nothing.
	/// </para>
	/// </remarks>
	public static class VerificationFailureTicket
	{
		/// <summary>The ticket's subject line.</summary>
		public const string Subject = "Account verification: three incorrect codes";

		/// <summary>What recording one incorrect code did.</summary>
		public readonly struct Outcome
		{
			/// <summary>Incorrect codes since the last correct one; 0 when nothing was counted.</summary>
			public readonly int Failures;

			/// <summary>The ticket opened by this failure, or 0.</summary>
			public readonly long TicketId;

			/// <summary>Why a ticket that was due could not be opened, or null.</summary>
			public readonly string? TicketError;

			/// <summary>Creates an outcome.</summary>
			public Outcome(int failures, long ticketId, string? ticketError)
			{
				Failures = failures;
				TicketId = ticketId;
				TicketError = ticketError;
			}

			/// <summary>Whether this failure opened a ticket.</summary>
			public bool TicketOpened => TicketId > 0;
		}

		/// <summary>
		/// Counts one incorrect code against the account, and opens the ticket when it is due.
		/// </summary>
		/// <param name="accounts">The account service.</param>
		/// <param name="tickets">The support ticket service, or null to count without ticketing.</param>
		/// <param name="accountName">The account the code was entered for.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		public static async Task<Outcome> RecordAsync(
			IAccountService accounts,
			ISupportTicketService? tickets,
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (accounts == null)
			{
				return new Outcome(0, 0, "No account service.");
			}

			DatabaseResult<int> counted = await accounts.RecordVerificationFailureAsync(accountName, cancellationToken).ConfigureAwait(false);
			if (!counted.IsSuccess)
			{
				return new Outcome(0, 0, null);
			}
			if (counted.Data != AccountVerificationRules.FailedCodesBeforeTicket)
			{
				return new Outcome(counted.Data, 0, null);
			}
			if (tickets == null)
			{
				return new Outcome(counted.Data, 0, "No support ticket service.");
			}

			DatabaseResult<AccountAdminData> account = await accounts.FetchAdminAsync(accountName, cancellationToken).ConfigureAwait(false);
			DatabaseResult<long> created = await tickets.CreateAsync(new SupportTicketCreate
			{
				ReporterAccount = account.IsSuccess ? account.Data.Name : accountName,
				Category = SupportTicketCategory.Help,
				Subject = Subject,
				Body = BuildBody(account.IsSuccess ? account.Data : null),
			}, cancellationToken).ConfigureAwait(false);

			return created.IsSuccess
				? new Outcome(counted.Data, created.Data, null)
				: new Outcome(counted.Data, 0, $"[{created.ErrorCode}] {created.ErrorMessage}");
		}

		/// <summary>
		/// The ticket body: what happened, and where each chosen channel's delivery stands. Readable by the player.
		/// </summary>
		public static string BuildBody(AccountAdminData? account)
		{
			var body = new StringBuilder();
			body.Append("This ticket was opened automatically because ")
				.Append(AccountVerificationRules.FailedCodesBeforeTicket)
				.Append(" incorrect verification codes were entered for this account, which cannot be signed in to until it is verified. ")
				.Append("Nothing is needed from you yet: staff will look into why the codes are not working and reply here.");

			if (account == null)
			{
				return body.ToString();
			}

			var chosen = (AccountVerificationChannels)account.VerificationChannels;
			body.Append("\n\nVerification methods chosen: ").Append(DescribeChannels(chosen));
			body.Append("\nA verification email or text was last delivered: ").Append(When(account.VerificationEmailSentAt) ?? "never");
			if ((chosen & AccountVerificationChannels.Sms) != 0)
			{
				body.Append("\nPhone number on the account: ").Append(string.IsNullOrEmpty(account.Phone) ? "no" : "yes");
			}
			if ((chosen & AccountVerificationChannels.Discord) != 0)
			{
				body.Append("\nDiscord message: ").Append(DescribeDiscord(account));
			}
			body.Append("\nAccount created: ").Append(When(account.Created));
			return body.ToString();
		}

		private static string DescribeChannels(AccountVerificationChannels channels)
		{
			var names = new List<string>(3);
			if ((channels & AccountVerificationChannels.Email) != 0) names.Add("email");
			if ((channels & AccountVerificationChannels.Sms) != 0) names.Add("SMS");
			if ((channels & AccountVerificationChannels.Discord) != 0) names.Add("Discord");
			return names.Count == 0 ? "email" : string.Join(", ", names);
		}

		private static string DescribeDiscord(AccountAdminData account)
		{
			if (string.IsNullOrEmpty(account.DiscordUsername))
			{
				return "no Discord username on the account";
			}
			if (account.DiscordDmSentAt.HasValue)
			{
				return $"delivered to {account.DiscordUsername} at {When(account.DiscordDmSentAt)}";
			}
			if (account.DiscordDmClaimedAt.HasValue)
			{
				return $"the bot began sending it to {account.DiscordUsername} at {When(account.DiscordDmClaimedAt)} and never confirmed it";
			}
			return string.IsNullOrEmpty(account.DiscordDmLastError)
				? $"not sent to {account.DiscordUsername} yet"
				: $"not sent to {account.DiscordUsername} yet: {account.DiscordDmLastError}";
		}

		private static string? When(DateTime? utc) =>
			utc.HasValue ? utc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : null;
	}
}
