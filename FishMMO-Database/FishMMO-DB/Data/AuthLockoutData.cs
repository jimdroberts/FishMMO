using System;

namespace FishMMO.Database.Data
{
	/// <summary>The two things a sign-in can be locked out of.</summary>
	public enum AuthFailureKind : byte
	{
		/// <summary>The password step: a failed SRP proof.</summary>
		Password = 0,
		/// <summary>The second step: a wrong authenticator or recovery code.</summary>
		TwoFactor = 1,
	}

	/// <summary>
	/// An account's sign-in lockout, as stored.
	/// </summary>
	/// <remarks>
	/// A lockout is in force only while its instant is in the future. Nothing sweeps a lapsed one, so
	/// readers ask with their own clock.
	/// </remarks>
	public readonly struct AuthLockoutState
	{
		/// <summary>Until when password sign-in is refused, or null.</summary>
		public readonly DateTime? LoginLockedUntilUtc;

		/// <summary>Until when the second step is refused, or null.</summary>
		public readonly DateTime? TwoFactorLockedUntilUtc;

		public AuthLockoutState(DateTime? loginLockedUntilUtc, DateTime? twoFactorLockedUntilUtc)
		{
			LoginLockedUntilUtc = loginLockedUntilUtc;
			TwoFactorLockedUntilUtc = twoFactorLockedUntilUtc;
		}

		/// <summary>Whether <paramref name="kind"/> is locked at <paramref name="nowUtc"/>.</summary>
		public bool IsLocked(AuthFailureKind kind, DateTime nowUtc)
		{
			DateTime? until = kind == AuthFailureKind.Password ? LoginLockedUntilUtc : TwoFactorLockedUntilUtc;
			return until.HasValue && until.Value > nowUtc;
		}
	}

	/// <summary>
	/// The optional contact and identity details an account holder gives at registration.
	/// </summary>
	/// <remarks>
	/// Personal data. It is never an input to authentication — a real name or an address that
	/// matches is not proof of anything a password or authenticator is — and it exists to help staff
	/// establish who owns an account.
	/// </remarks>
	public sealed class AccountProfileData
	{
		/// <summary>Phone number in E.164 form, or null.</summary>
		public string? Phone { get; set; }

		/// <summary>Real name, or null.</summary>
		public string? RealName { get; set; }

		/// <summary>Country or region, or null.</summary>
		public string? Country { get; set; }

		/// <summary>Postal address, or null.</summary>
		public string? Address { get; set; }

		/// <summary>The referring account, or null.</summary>
		public string? ReferralAccount { get; set; }

		/// <summary>The Discord username the one verification DM goes to, or null.</summary>
		public string? DiscordUsername { get; set; }

		/// <summary>The channels chosen to verify with.</summary>
		public Enums.AccountVerificationChannels VerificationChannels { get; set; } = Enums.AccountVerificationChannels.Email;
	}
}
