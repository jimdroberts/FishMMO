using FishMMO.Database.Data.Enums;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Which verification channels this server actually verifies (<c>Verification:*</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A player chooses the channels they verify with at registration. A channel whose switch is
	/// off is not asked for a code: it is marked proven through
	/// <c>IAccountService.PersistChannelsVerifiedAsync</c>, which recomputes <c>verified</c>. A
	/// server with both switches off verifies nothing, and registration takes the same
	/// auto-verify path the development <see cref="PanelRegistrationOptions.AutoVerifyAccounts"/>
	/// setting takes — one mechanism, not two.
	/// </para>
	/// <para>
	/// Unlike the development bypass these are honoured in Production. They are a deliberate
	/// shard policy ("this shard does not verify phone numbers"), not a convenience that must not
	/// escape a laptop, and a shard that turns both off is told so loudly at startup.
	/// </para>
	/// </remarks>
	public sealed class VerificationOptions
	{
		/// <summary>Whether email addresses are verified with an emailed code.</summary>
		public bool Email { get; init; } = true;

		/// <summary>Whether phone numbers are verified with a texted code.</summary>
		public bool Sms { get; init; } = true;

		/// <summary>The channels this server sends codes for.</summary>
		public AccountVerificationChannels Enabled =>
			(Email ? AccountVerificationChannels.Email : AccountVerificationChannels.None) |
			(Sms ? AccountVerificationChannels.Sms : AccountVerificationChannels.None);

		/// <summary>True when no channel is verified at all.</summary>
		public bool VerifiesNothing => !Email && !Sms;
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
