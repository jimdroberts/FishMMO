using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The operator-controlled lifecycle state of a world or scene server.
	/// </summary>
	/// <remarks>
	/// The database row is the authority for both fields, not the process. A server writes them
	/// only when an operator asks it to; on every pulse it reads them back and adopts whatever it
	/// finds. That inversion is what makes the controls usable at all — the previous arrangement
	/// had each server write its own in-memory <c>locked</c> flag on every pulse, so anything
	/// that set the column out of band was overwritten within five seconds, and nothing in the
	/// process ever set the flag either. It also means any tool that can write the row (the
	/// Discord bot, a CMS, psql) controls the servers, exactly as the kick-request table already
	/// does for accounts.
	/// </remarks>
	public readonly struct ServerControlState
	{
		/// <summary>
		/// Whether the server is closed to new arrivals.
		/// </summary>
		/// <remarks>
		/// A drain, not an eviction: players already on the server keep playing. Elevated
		/// accounts are admitted anyway, so locking a world does not lock out the operator who
		/// has to go in and look at it.
		/// </remarks>
		public readonly bool Locked;

		/// <summary>
		/// When this server stops, or <c>null</c> when no shutdown is scheduled.
		/// </summary>
		/// <remarks>
		/// An absolute UTC instant rather than a remaining duration, so every process that reads
		/// it agrees on the deadline regardless of when it last pulsed, and a countdown survives
		/// a reader missing a beat.
		/// </remarks>
		public readonly DateTime? ShutdownAtUtc;

		/// <summary>Creates a control state.</summary>
		/// <param name="locked">Whether the server is closed to new arrivals.</param>
		/// <param name="shutdownAtUtc">Scheduled stop time, or null.</param>
		public ServerControlState(bool locked, DateTime? shutdownAtUtc)
		{
			Locked = locked;
			ShutdownAtUtc = shutdownAtUtc;
		}

		/// <summary>Whether a shutdown is scheduled at all.</summary>
		public bool HasShutdown => ShutdownAtUtc.HasValue;

		/// <summary>
		/// Seconds remaining until the scheduled shutdown, clamped at zero. Zero when no
		/// shutdown is scheduled or the deadline has passed.
		/// </summary>
		/// <param name="nowUtc">Current time.</param>
		public double SecondsUntilShutdown(DateTime nowUtc)
		{
			if (!ShutdownAtUtc.HasValue)
			{
				return 0.0;
			}
			double seconds = (ShutdownAtUtc.Value - nowUtc).TotalSeconds;
			return seconds > 0.0 ? seconds : 0.0;
		}
	}
}
