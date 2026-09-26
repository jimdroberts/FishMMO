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
	/// Discord bot, the Control Panel, psql) controls the servers, exactly as the kick-request table already
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
		/// An absolute UTC instant on the row, so every process that reads it agrees on which
		/// shutdown is scheduled. It is the schedule's identity (a changed value is a rescheduled
		/// shutdown) and what the logs print. It is NOT what a reader counts down against: that
		/// would compare the database's clock with the reader's, see
		/// <see cref="ShutdownInSeconds"/>.
		/// </remarks>
		public readonly DateTime? ShutdownAtUtc;

		/// <summary>
		/// Seconds from the moment the database read this row until <see cref="ShutdownAtUtc"/>,
		/// measured by the database clock; zero or negative once the deadline has passed.
		/// <c>null</c> exactly when no shutdown is scheduled.
		/// </summary>
		/// <remarks>
		/// The deadline is stamped by whoever scheduled it and read by every server the shutdown
		/// concerns, so no reader's own clock is a fair judge of how far away it is. Each server
		/// used to subtract <c>DateTime.UtcNow</c> from the instant: a world host running a minute
		/// fast stopped a minute early, one stepped forward by NTP stopped at once, and every
		/// process serving one world counted down to a different moment. The database measures
		/// the remaining time inside the statement that reads the row, and a reader anchors that
		/// on its own monotonic clock the moment the reply arrives.
		/// </remarks>
		public readonly double? ShutdownInSeconds;

		/// <summary>Creates a control state.</summary>
		/// <param name="locked">Whether the server is closed to new arrivals.</param>
		/// <param name="shutdownAtUtc">Scheduled stop time, or null.</param>
		/// <param name="shutdownInSeconds">
		/// Seconds until <paramref name="shutdownAtUtc"/> as the database measured them when it read
		/// the row. Ignored, and stored as null, when no shutdown is scheduled.
		/// </param>
		public ServerControlState(bool locked, DateTime? shutdownAtUtc, double? shutdownInSeconds)
		{
			Locked = locked;
			ShutdownAtUtc = shutdownAtUtc;
			ShutdownInSeconds = shutdownAtUtc.HasValue ? shutdownInSeconds : null;
		}

		/// <summary>Whether a shutdown is scheduled at all.</summary>
		public bool HasShutdown => ShutdownAtUtc.HasValue;
	}
}
