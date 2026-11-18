using System;
using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// How long a sign-in step stays locked, and how many failures inside how long a window lock it.
	/// </summary>
	public readonly struct AuthLockoutSettings
	{
		/// <summary>Failures inside <see cref="Window"/> that lock the step.</summary>
		public readonly int Threshold;
		/// <summary>The window failures are counted in.</summary>
		public readonly TimeSpan Window;
		/// <summary>How long the step stays locked once the threshold is reached.</summary>
		public readonly TimeSpan Lockout;

		/// <summary>Creates the settings.</summary>
		public AuthLockoutSettings(int threshold, TimeSpan window, TimeSpan lockout)
		{
			Threshold = threshold;
			Window = window;
			Lockout = lockout;
		}
	}

	/// <summary>
	/// The login server's closed-test (beta) and database-backed sign-in lockout settings, read from
	/// <c>LoginServer.cfg</c>. Shared by account creation and the SRP authenticator so the two can
	/// never disagree about whether the server is in a closed test or what a lockout is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The lockout defaults are the Control Panel's.</b> Failures are counted on the account row,
	/// so every login server and the panel lock one account together; a login server configured
	/// differently from the panel would still share the counter but disagree about when it trips.
	/// Change them in both places or neither.
	/// </para>
	/// <para>
	/// Read on every use rather than cached, like <see cref="AccountVerificationPolicy"/>: the
	/// parsing is a handful of dictionary lookups and a login is not a hot loop.
	/// </para>
	/// </remarks>
	public static class LoginSecurityPolicy
	{
		/// <summary>Key that turns closed-test mode on. Default false.</summary>
		public const string BetaModeKey = "BetaMode";
		/// <summary>Key listing the beta programs, comma-separated.</summary>
		public const string BetaProgramsKey = "BetaPrograms";

		/// <summary>Password failures that lock password sign-in.</summary>
		public const string PasswordThresholdKey = "AuthLockoutPasswordThreshold";
		/// <summary>Minutes password failures are counted over.</summary>
		public const string PasswordWindowMinutesKey = "AuthLockoutPasswordWindowMinutes";
		/// <summary>Minutes password sign-in stays locked.</summary>
		public const string PasswordLockMinutesKey = "AuthLockoutPasswordLockMinutes";
		/// <summary>Wrong second-factor codes that lock the two-factor step.</summary>
		public const string TwoFactorThresholdKey = "AuthLockoutTwoFactorThreshold";
		/// <summary>Minutes second-factor failures are counted over.</summary>
		public const string TwoFactorWindowMinutesKey = "AuthLockoutTwoFactorWindowMinutes";
		/// <summary>Minutes the two-factor step stays locked.</summary>
		public const string TwoFactorLockMinutesKey = "AuthLockoutTwoFactorLockMinutes";

		/// <summary>Shared default: 5 password failures.</summary>
		public const int DefaultPasswordThreshold = 5;
		/// <summary>Shared default: counted over 15 minutes.</summary>
		public const int DefaultPasswordWindowMinutes = 15;
		/// <summary>Shared default: locked for 15 minutes.</summary>
		public const int DefaultPasswordLockMinutes = 15;
		/// <summary>Shared default: 5 wrong second-factor codes.</summary>
		public const int DefaultTwoFactorThreshold = 5;
		/// <summary>Shared default: counted over 15 minutes.</summary>
		public const int DefaultTwoFactorWindowMinutes = 15;
		/// <summary>Shared default: locked for 30 minutes.</summary>
		public const int DefaultTwoFactorLockMinutes = 30;

		/// <summary>Upper bound on any configured threshold.</summary>
		private const int MaxThreshold = 1000;
		/// <summary>Upper bound on any configured minute value: one week.</summary>
		private const int MaxMinutes = 7 * 24 * 60;

		/// <summary>Whether the server is in a closed test. Anything but a readable <c>true</c> is off.</summary>
		public static bool IsBetaModeEnabled(IServerConfiguration configuration) =>
			AccountVerificationPolicy.TryReadBool(configuration, BetaModeKey, out bool enabled) && enabled;

		/// <summary>
		/// The active beta programs: trimmed, lowercased, de-duplicated, invalid names dropped.
		/// </summary>
		/// <param name="configuration">Server configuration, or null.</param>
		/// <param name="rejected">How many listed names were not valid program names.</param>
		public static IReadOnlyList<string> GetBetaPrograms(IServerConfiguration configuration, out int rejected)
		{
			rejected = 0;
			var programs = new List<string>();
			if (configuration == null ||
				!configuration.TryGetString(BetaProgramsKey, out string raw) ||
				string.IsNullOrWhiteSpace(raw))
			{
				return programs;
			}

			foreach (string part in raw.Split(','))
			{
				if (string.IsNullOrWhiteSpace(part))
				{
					continue;
				}
				string program = BetaCodeFormat.NormalizeProgram(part);
				if (!BetaCodeFormat.IsValidProgram(program))
				{
					rejected++;
					continue;
				}
				if (!programs.Contains(program))
				{
					programs.Add(program);
				}
			}
			return programs;
		}

		/// <summary>The password-step lockout.</summary>
		public static AuthLockoutSettings GetPasswordLockout(IServerConfiguration configuration) =>
			Read(configuration,
				PasswordThresholdKey, DefaultPasswordThreshold,
				PasswordWindowMinutesKey, DefaultPasswordWindowMinutes,
				PasswordLockMinutesKey, DefaultPasswordLockMinutes);

		/// <summary>The two-factor-step lockout.</summary>
		public static AuthLockoutSettings GetTwoFactorLockout(IServerConfiguration configuration) =>
			Read(configuration,
				TwoFactorThresholdKey, DefaultTwoFactorThreshold,
				TwoFactorWindowMinutesKey, DefaultTwoFactorWindowMinutes,
				TwoFactorLockMinutesKey, DefaultTwoFactorLockMinutes);

		private static AuthLockoutSettings Read(
			IServerConfiguration configuration,
			string thresholdKey, int thresholdDefault,
			string windowKey, int windowDefault,
			string lockKey, int lockDefault)
		{
			int threshold = configuration?.GetInt(thresholdKey, thresholdDefault) ?? thresholdDefault;
			int window = configuration?.GetInt(windowKey, windowDefault) ?? windowDefault;
			int lockMinutes = configuration?.GetInt(lockKey, lockDefault) ?? lockDefault;

			// A zero or negative value would be refused by the database outright, which would silently
			// switch the lockout off; clamp to the smallest meaningful setting instead.
			threshold = Math.Max(1, Math.Min(threshold, MaxThreshold));
			window = Math.Max(1, Math.Min(window, MaxMinutes));
			lockMinutes = Math.Max(1, Math.Min(lockMinutes, MaxMinutes));

			return new AuthLockoutSettings(threshold, TimeSpan.FromMinutes(window), TimeSpan.FromMinutes(lockMinutes));
		}
	}
}
