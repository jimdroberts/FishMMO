using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Which verification codes an account is sent and asked for. One copy, shared by the game's login
	/// server and the Control Panel, so the two can never disagree about whether an account is let in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Any one channel verifies the account.</b> A player may choose email, SMS and a Discord DM
	/// together; a code goes out on each, and whichever is entered first sets <c>verified</c>.
	/// </para>
	/// <para>
	/// <b>Verification is required when the server switches at least one channel on.</b> A channel
	/// the player chose is asked for only when it is switched on and the account can receive it — SMS
	/// needs a phone number, Discord a username. When none of the player's choices qualifies, the
	/// account falls back to the first channel that does, in the order email, SMS, Discord: every
	/// account has an email address, so a player who picked a switched-off channel is still asked for
	/// a code rather than let in without one. Only an account that can receive no enabled channel at
	/// all is verified without a code, because no code could ever reach it.
	/// </para>
	/// </remarks>
	public static class AccountVerificationRules
	{
		/// <summary>Every channel there is.</summary>
		public const AccountVerificationChannels All =
			AccountVerificationChannels.Email | AccountVerificationChannels.Sms | AccountVerificationChannels.Discord;

		/// <summary>
		/// Incorrect codes, since the last correct one, at which a support ticket is opened for the account.
		/// </summary>
		/// <remarks>
		/// The ticket is opened when the count reaches this number exactly, so one run of failures opens
		/// one ticket however long it continues.
		/// </remarks>
		public const int FailedCodesBeforeTicket = 3;

		/// <summary>The channels an account could be sent a code on, from what it holds.</summary>
		public static AccountVerificationChannels Receivable(string? email, string? phone, string? discordUsername)
		{
			AccountVerificationChannels receivable = AccountVerificationChannels.None;
			if (!string.IsNullOrWhiteSpace(email)) receivable |= AccountVerificationChannels.Email;
			if (!string.IsNullOrWhiteSpace(phone)) receivable |= AccountVerificationChannels.Sms;
			if (!string.IsNullOrWhiteSpace(discordUsername)) receivable |= AccountVerificationChannels.Discord;
			return receivable;
		}

		/// <summary>
		/// The channels a code is sent on and asked for. None means the account is not asked for a code.
		/// </summary>
		/// <param name="chosen">What the player chose.</param>
		/// <param name="enabled">What this server has switched on.</param>
		/// <param name="receivable">What the account can receive; see <see cref="Receivable"/>.</param>
		public static AccountVerificationChannels Effective(
			AccountVerificationChannels chosen,
			AccountVerificationChannels enabled,
			AccountVerificationChannels receivable)
		{
			AccountVerificationChannels available = enabled & receivable & All;
			if (available == AccountVerificationChannels.None)
			{
				return AccountVerificationChannels.None;
			}

			AccountVerificationChannels effective = chosen & available;
			if (effective != AccountVerificationChannels.None)
			{
				return effective;
			}

			foreach (AccountVerificationChannels fallback in new[] { AccountVerificationChannels.Email, AccountVerificationChannels.Sms, AccountVerificationChannels.Discord })
			{
				if ((available & fallback) != 0)
				{
					return fallback;
				}
			}
			return AccountVerificationChannels.None;
		}

		/// <summary>
		/// The channels an account is still asked for a code on. None for a verified account, and for one
		/// that no enabled channel can reach.
		/// </summary>
		public static AccountVerificationChannels Outstanding(in AccountData account, AccountVerificationChannels enabled)
		{
			if (account.Verified)
			{
				return AccountVerificationChannels.None;
			}
			return Effective(
				(AccountVerificationChannels)account.VerificationChannels,
				enabled,
				Receivable(account.Email, account.Phone, account.DiscordUsername));
		}

		/// <summary>
		/// True when an unverified account must be let in without a code: the server verifies at least one
		/// channel, and none of them can reach this account.
		/// </summary>
		public static bool IsWaived(in AccountData account, AccountVerificationChannels enabled)
		{
			return !account.Verified &&
				(enabled & All) != AccountVerificationChannels.None &&
				Outstanding(account, enabled) == AccountVerificationChannels.None;
		}
	}
}
