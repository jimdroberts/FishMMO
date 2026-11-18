using System;

namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Runtime data container for world server instance state.
	/// Tracks world server ID and lock status.
	/// </summary>
	public interface IWorldServerSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Database ID for this world server instance.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// Indicates whether the world server is locked (not accepting new connections).
		/// </summary>
		/// <remarks>
		/// Adopted from the database row on every pulse rather than owned here; the row is what
		/// an operator sets. A locked world still admits accounts above
		/// <see cref="FishMMO.Auth.Core.AccessLevel.Player"/>, so locking it for maintenance
		/// does not lock out the people doing the maintenance.
		/// </remarks>
		bool IsLocked { get; set; }

		/// <summary>
		/// When this world stops, or <c>null</c> when no shutdown is scheduled.
		/// </summary>
		/// <remarks>
		/// Also adopted from the row, so a shutdown scheduled from anywhere — an in-game
		/// <c>/admin shutdown</c>, the Discord bot, psql — reaches this process within two pulses
		/// (a pulse's reading is adopted on the next one). It says whether and when a shutdown is
		/// scheduled; the countdown itself runs on <c>ShutdownCountdown</c>, from the seconds left
		/// as the database measured them, so every process serving this world stops at the same
		/// moment whatever its own clock says.
		/// </remarks>
		DateTime? ShutdownAtUtc { get; set; }

		/// <summary>
		/// Atomically transitions the pulse gate from idle to in-flight.
		/// Returns true if this call won the race; false if a pulse is already in flight.
		/// </summary>
		bool TryBeginPulse();

		/// <summary>
		/// Atomically transitions the pulse gate from in-flight back to idle.
		/// </summary>
		void EndPulse();
	}
}