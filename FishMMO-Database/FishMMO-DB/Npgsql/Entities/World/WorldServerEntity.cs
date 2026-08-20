using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>World server registration entity representing an active world server instance.</summary>
	public class WorldServerEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>World server display name.</summary>
		public string Name { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Most recent heartbeat / pulse timestamp (UTC) for liveness detection.</summary>
		public DateTime LastPulse { get; set; }
		/// <summary>Network address of the world server.</summary>
		public string Address { get; set; }
		/// <summary>Server port number. Must be in the valid port range 0-65535.</summary>
		[System.ComponentModel.DataAnnotations.Range(0, 65535)]
		public int Port { get; set; }
		/// <summary>Current number of characters on this world server.</summary>
		public int CharacterCount { get; set; }
		/// <summary>Whether the world server is locked to new connections.</summary>
		public bool Locked { get; set; }
		/// <summary>
		/// When this world stops for maintenance, or <c>null</c> when no shutdown is scheduled.
		/// </summary>
		/// <remarks>
		/// Read by the world server and by every scene server hosting scenes for this world, so
		/// they can warn their players and clear them out on the same deadline. Absolute UTC —
		/// see <see cref="FishMMO.Database.Data.ServerControlState.ShutdownAtUtc"/>.
		/// </remarks>
		public DateTime? ShutdownAtUtc { get; set; }
	}
}