using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The Discord verification DM, and the constants its sender and its issuers share.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One DM per account, ever.</b> The code is issued once and never replaced, the bot claims the
	/// delivery before it sends, and a claim is never taken back automatically: a bot that crashed
	/// between the claim and the confirmation leaves the delivery claimed and unsent for staff to see,
	/// rather than risk a second DM.
	/// </para>
	/// <para>
	/// <b>No polling.</b> Issuing a code raises a PostgreSQL notification on
	/// <see cref="NotifyChannel"/>, which the bot listens for. A player who is not in the Discord server
	/// yet is sent the DM when they join, from the bot's member-join event.
	/// </para>
	/// </remarks>
	public static class DiscordVerification
	{
		/// <summary>The PostgreSQL notification channel raised when a Discord code is issued. No payload.</summary>
		public const string NotifyChannel = "fishmmo_discord_verification";

		/// <summary>
		/// Sends Discord refused (DMs closed, the user blocked the bot, the account is linked elsewhere)
		/// after which the bot stops trying. Waiting for a player to join the server is not an attempt.
		/// </summary>
		public const int MaxSendAttempts = 5;

		/// <summary>Longest reason recorded for a delivery that has not gone out.</summary>
		public const int MaxErrorLength = 256;
	}

	/// <summary>One verification DM waiting to be sent.</summary>
	public readonly struct DiscordVerificationDelivery
	{
		/// <summary>The account the code verifies.</summary>
		public readonly string AccountName;

		/// <summary>The Discord username to send it to, lowercase.</summary>
		public readonly string DiscordUsername;

		/// <summary>The six-digit code.</summary>
		public readonly int Code;

		/// <summary>Sends Discord has refused so far.</summary>
		public readonly int Attempts;

		/// <summary>Why the last try did not send, or null.</summary>
		public readonly string? LastError;

		/// <summary>Creates a delivery.</summary>
		public DiscordVerificationDelivery(string accountName, string discordUsername, int code, int attempts, string? lastError)
		{
			AccountName = accountName;
			DiscordUsername = discordUsername;
			Code = code;
			Attempts = attempts;
			LastError = lastError;
		}
	}

	/// <summary>A game account's link to a Discord user.</summary>
	public readonly struct DiscordAccountLink
	{
		/// <summary>The game account.</summary>
		public readonly string AccountName;

		/// <summary>The Discord user's snowflake, stored signed.</summary>
		public readonly long DiscordUserId;

		/// <summary>The Discord username on the account, lowercase, or null.</summary>
		public readonly string? DiscordUsername;

		/// <summary>When the link was made, or null for a link older than the column.</summary>
		public readonly DateTime? LinkedAtUtc;

		/// <summary>Creates a link.</summary>
		public DiscordAccountLink(string accountName, long discordUserId, string? discordUsername, DateTime? linkedAtUtc)
		{
			AccountName = accountName;
			DiscordUserId = discordUserId;
			DiscordUsername = discordUsername;
			LinkedAtUtc = linkedAtUtc;
		}
	}
}
