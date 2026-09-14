using System;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A request to remove an account's second factor after a waiting period, for a player who has
	/// lost both their authenticator and their recovery codes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The delay is the security.</b> Whoever asks has, at most, the password; if asking removed
	/// the second factor straight away, the second factor would protect against nothing a stolen
	/// password does not already give. The waiting period is the window in which the real owner —
	/// who still has their authenticator — signs in normally, and a normal sign-in cancels the
	/// request. Staff may shorten the wait once they have verified the player some other way.
	/// </para>
	/// <para>
	/// <b>Completing a request never satisfies two-factor.</b> It removes the old factor so the
	/// account can enrol a new one; the session that completes it has proved the password and
	/// nothing more, and must be sent to enrolment, not treated as having passed a second factor.
	/// </para>
	/// <para>
	/// At most one request per account is pending, enforced by a partial unique index on
	/// <c>account_name WHERE status = 0</c>. Asking again returns the existing request unchanged, so
	/// repeating the request can neither restart the clock on the owner nor shorten it for an
	/// attacker.
	/// </para>
	/// <para>
	/// No foreign key to <c>accounts</c>, for the reasons given on
	/// <see cref="PasswordResetTokenEntity"/>: accounts are never deleted, and every write here
	/// follows a read of the account.
	/// </para>
	/// </remarks>
	public class TwoFactorResetRequestEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// PostgreSQL <c>xmin</c> concurrency token. Staff shortening a request while the player
		/// signs in and cancels it is two writers on one row.
		/// </summary>
		public uint Version { get; set; }

		/// <summary>The account, matching <c>accounts.name</c>, stored lowercase.</summary>
		public string AccountName { get; set; }

		/// <summary>Where the request has got to.</summary>
		public TwoFactorResetStatus Status { get; set; } = TwoFactorResetStatus.Pending;

		/// <summary>When it was asked for.</summary>
		public DateTime RequestedUtc { get; set; }

		/// <summary>
		/// When it may be completed.
		/// </summary>
		/// <remarks>
		/// Only ever moved earlier, and only by staff. Pushing it later is a cancel followed by a
		/// new request, so the longer wait starts from a request the history can show.
		/// </remarks>
		public DateTime EffectiveUtc { get; set; }

		/// <summary>Client address that asked, for abuse investigation.</summary>
		public string? RequestedIp { get; set; }

		/// <summary>When it was cancelled or completed.</summary>
		public DateTime? ResolvedUtc { get; set; }

		/// <summary>Who cancelled or completed it: a staff account, or the account itself.</summary>
		public string? ResolvedBy { get; set; }

		/// <summary>When staff last brought the effective time forward.</summary>
		public DateTime? ShortenedUtc { get; set; }

		/// <summary>The staff account that last brought it forward.</summary>
		public string? ShortenedBy { get; set; }

		/// <summary>Why staff last shortened or cancelled it.</summary>
		public string? StaffReason { get; set; }
	}
}
