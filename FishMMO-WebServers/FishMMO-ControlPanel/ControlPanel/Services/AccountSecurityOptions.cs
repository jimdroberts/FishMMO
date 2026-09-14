using FishMMO.Database.Data.Enums;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Which verification channels this server actually verifies (<c>Verification:*</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A player chooses the channels they verify with at registration — email, SMS, a Discord DM, or
	/// several — and a code goes out on each one this server switches on. <b>Any one correct code
	/// verifies the account.</b> More channels give the player more ways to receive a code, never more
	/// codes to enter. Which channels an account is actually sent and asked for is
	/// <c>AccountVerificationRules</c>, shared with the login server, not anything decided here: when
	/// nothing the player chose is switched on, the account falls back to one that is, so switching a
	/// channel off never lets an account in without a code.
	/// </para>
	/// <para>
	/// A server with every switch off verifies nothing, and registration takes the same auto-verify
	/// path the development <see cref="PanelRegistrationOptions.AutoVerifyAccounts"/> setting takes —
	/// one mechanism, not two. These switches must match the login server's <c>VerifyEmail</c>,
	/// <c>VerifySms</c> and <c>VerifyDiscord</c>, or an account could be let into the game and refused
	/// by the panel, or the other way round.
	/// </para>
	/// <para>
	/// Unlike the development bypass these are honoured in Production. They are a deliberate
	/// shard policy ("this shard does not verify phone numbers"), not a convenience that must not
	/// escape a laptop, and a shard that turns them all off is told so loudly at startup.
	/// </para>
	/// </remarks>
	public sealed class VerificationOptions
	{
		/// <summary>Whether email addresses are verified with an emailed code.</summary>
		public bool Email { get; init; } = true;

		/// <summary>Whether phone numbers are verified with a texted code.</summary>
		public bool Sms { get; init; } = true;

		/// <summary>
		/// Whether a Discord username can be verified with a code sent as a DM by the game's Discord bot.
		/// </summary>
		/// <remarks>
		/// Only meaningful with the Discord bot running: the panel issues the code, and the bot delivers
		/// it. Switched on with no bot, a player who chose only Discord is asked for a code nobody sends,
		/// and the rule's fallback cannot help, because Discord counts as switched on.
		/// </remarks>
		public bool Discord { get; init; } = true;

		/// <summary>
		/// The invite link to the game's Discord server, shown beside the Discord username field. Empty for none.
		/// </summary>
		/// <remarks>
		/// The bot can only message members of a server it shares with the player, so the player has to
		/// join first. It is published only when it is an <c>https://</c> link; anything else is dropped
		/// at startup rather than rendered as a link on an anonymous page.
		/// </remarks>
		public string DiscordInviteUrl { get; init; } = "";

		/// <summary>The channels this server sends codes for.</summary>
		public AccountVerificationChannels Enabled =>
			(Email ? AccountVerificationChannels.Email : AccountVerificationChannels.None) |
			(Sms ? AccountVerificationChannels.Sms : AccountVerificationChannels.None) |
			(Discord ? AccountVerificationChannels.Discord : AccountVerificationChannels.None);

		/// <summary>True when no channel is verified at all.</summary>
		public bool VerifiesNothing => !Email && !Sms && !Discord;
	}

	/// <summary>
	/// The closed-beta registration gate (<c>Beta:*</c>).
	/// </summary>
	/// <remarks>
	/// Registration only. Panel sign-in is deliberately NOT gated: an account created before the
	/// gate went up, or one whose redemption lost a race, needs the panel to redeem a code at all.
	/// </remarks>
	public sealed class BetaOptions
	{
		/// <summary>Whether registration requires a beta code.</summary>
		public bool Enabled { get; init; }

		/// <summary>
		/// Program keys a code must belong to, such as <c>closed_beta</c>. Empty means any program.
		/// </summary>
		public IReadOnlyList<string> Programs { get; init; } = Array.Empty<string>();
	}

	/// <summary>
	/// Sign-in lockout thresholds (<c>Auth:Lockout:*</c>).
	/// </summary>
	/// <remarks>
	/// The counters live in the database and the LoginServer counts into the same columns, so these
	/// numbers must match the game's. The defaults are the shard defaults; change them in both
	/// places or neither.
	/// </remarks>
	public sealed class AuthLockoutOptions
	{
		/// <summary>Failed password proofs inside the window that lock the password step.</summary>
		public int PasswordThreshold { get; init; } = 5;

		/// <summary>The password failure window, in minutes.</summary>
		public int PasswordWindowMinutes { get; init; } = 15;

		/// <summary>How long a password lock lasts, in minutes.</summary>
		public int PasswordLockMinutes { get; init; } = 15;

		/// <summary>Failed authenticator or recovery codes inside the window that lock the second step.</summary>
		public int TwoFactorThreshold { get; init; } = 5;

		/// <summary>The two-factor failure window, in minutes.</summary>
		public int TwoFactorWindowMinutes { get; init; } = 15;

		/// <summary>How long a two-factor lock lasts, in minutes.</summary>
		public int TwoFactorLockMinutes { get; init; } = 30;

		/// <summary>Reads the section, clamping each value to something that can still lock.</summary>
		public static AuthLockoutOptions From(IConfiguration configuration)
		{
			var section = configuration.GetSection("Auth:Lockout");
			var defaults = new AuthLockoutOptions();
			return new AuthLockoutOptions
			{
				PasswordThreshold = Math.Clamp(section.GetValue("PasswordThreshold", defaults.PasswordThreshold), 1, 1000),
				PasswordWindowMinutes = Math.Clamp(section.GetValue("PasswordWindowMinutes", defaults.PasswordWindowMinutes), 1, 1440),
				PasswordLockMinutes = Math.Clamp(section.GetValue("PasswordLockMinutes", defaults.PasswordLockMinutes), 1, 10080),
				TwoFactorThreshold = Math.Clamp(section.GetValue("TwoFactorThreshold", defaults.TwoFactorThreshold), 1, 1000),
				TwoFactorWindowMinutes = Math.Clamp(section.GetValue("TwoFactorWindowMinutes", defaults.TwoFactorWindowMinutes), 1, 1440),
				TwoFactorLockMinutes = Math.Clamp(section.GetValue("TwoFactorLockMinutes", defaults.TwoFactorLockMinutes), 1, 10080),
			};
		}
	}

	/// <summary>
	/// The self-service two-factor reset waiting period (<c>TwoFactorReset:*</c>).
	/// </summary>
	public sealed class TwoFactorResetOptions
	{
		/// <summary>Days between asking for a reset and being allowed to complete it.</summary>
		public int DelayDays { get; init; } = 7;

		/// <summary>The waiting period.</summary>
		public TimeSpan Delay => TimeSpan.FromDays(Math.Clamp(DelayDays, 1, 90));
	}
}
