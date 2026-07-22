using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Kick request entity representing a request to forcibly disconnect an account from the server.</summary>
	public class KickRequestEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Account name to be kicked.</summary>
		public string AccountName { get; set; }
		/// <summary>Row creation timestamp (UTC) — when the kick was requested.</summary>
		public DateTime TimeCreated { get; set; }
	}
}