using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Whether a claimed daemon command may still be executed, judged without the daemon's wall
	/// clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A command is stamped, claimed and completed by the database clock. A daemon that compared
	/// the command's deadline with its own <see cref="DateTime.UtcNow"/> measured the skew between
	/// its host and the database along with the time left: one running five minutes fast refused
	/// every command it was given (five minutes is the lifetime), and one running slow executed
	/// commands after the operator had stopped waiting for them.
	/// </para>
	/// <para>
	/// So the claim returns the seconds left, measured by the database in the statement that
	/// claimed the row (<see cref="DaemonCommandData.ExpiresInSeconds"/>), and the daemon counts
	/// them down on a monotonic clock, which no wall-clock step, correction or time-zone change can
	/// move. The daemon anchors that count when it SENDS the claim, not when the reply arrives: the
	/// database measured somewhere in between, so an early anchor can only overstate the time
	/// elapsed. Every error is therefore toward refusing, because a restart carried out after its
	/// requester stopped waiting is the failure this check exists to prevent.
	/// </para>
	/// </remarks>
	public static class DaemonCommandExpiry
	{
		/// <summary>
		/// Seconds still left: the database's measurement at the claim, less the time that has
		/// passed since the claim was sent.
		/// </summary>
		/// <param name="expiresInSecondsAtClaim">
		/// <see cref="DaemonCommandData.ExpiresInSeconds"/> from the claim, or null when nothing
		/// was measured.
		/// </param>
		/// <param name="sinceClaimSent">
		/// Monotonic time since the claim was sent. Negative counts as zero: elapsed time can
		/// never extend a deadline.
		/// </param>
		/// <returns>
		/// The seconds left; zero or less is expired. Negative infinity when nothing was measured,
		/// so a command with no measured deadline is never run.
		/// </returns>
		public static double SecondsLeft(double? expiresInSecondsAtClaim, TimeSpan sinceClaimSent)
		{
			if (!expiresInSecondsAtClaim.HasValue || double.IsNaN(expiresInSecondsAtClaim.Value))
			{
				return double.NegativeInfinity;
			}
			return expiresInSecondsAtClaim.Value - Math.Max(0.0, sinceClaimSent.TotalSeconds);
		}

		/// <summary>
		/// Whether a claimed command must be refused as expired.
		/// </summary>
		/// <remarks>
		/// Exactly zero left is expired: the claim takes a row only while
		/// <c>expires_utc &gt; now</c>, so this is the same boundary seen from the daemon.
		/// </remarks>
		/// <param name="expiresInSecondsAtClaim">See <see cref="SecondsLeft"/>.</param>
		/// <param name="sinceClaimSent">See <see cref="SecondsLeft"/>.</param>
		public static bool IsExpired(double? expiresInSecondsAtClaim, TimeSpan sinceClaimSent)
		{
			return !(SecondsLeft(expiresInSecondsAtClaim, sinceClaimSent) > 0.0);
		}
	}
}
