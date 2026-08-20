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
		/// Also adopted from the row on every pulse, so a shutdown scheduled from anywhere — an
		/// in-game <c>/admin shutdown</c>, the Discord bot, psql — reaches this process within
		/// one pulse. Absolute UTC so every process serving this world counts down to the same
		/// instant.
		/// </remarks>
		DateTime? ShutdownAtUtc { get; set; }
	}
}