using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class SceneServerEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Scene server display name.</summary>
		public string Name { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Most recent heartbeat / pulse timestamp (UTC) for liveness detection.</summary>
		public DateTime LastPulse { get; set; }
		/// <summary>Network address of the scene server.</summary>
		public string Address { get; set; }
		/// <summary>Network port of the scene server.</summary>
		public ushort Port { get; set; }
		/// <summary>Current number of characters hosted by this scene server.</summary>
		public int CharacterCount { get; set; }
		/// <summary>Whether the scene server is locked to new connections.</summary>
		public bool Locked { get; set; }
	}
}