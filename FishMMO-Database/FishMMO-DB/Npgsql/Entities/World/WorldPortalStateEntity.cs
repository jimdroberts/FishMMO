using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A portal opened for the whole world, permanently or until <see cref="ActiveUntil"/>. See
	/// <see cref="Data.WorldPortalStateData"/>.
	/// </summary>
	/// <remarks>
	/// No version and no soft-delete: every write is an idempotent merge (permanent ORs, the
	/// deadline takes the later), so there is no stale write to reject and nothing to undo.
	/// </remarks>
	public class WorldPortalStateEntity
	{
		/// <summary>The scene the portal stands in. Part of the primary key.</summary>
		public string SceneName { get; set; }

		/// <summary>The portal's authored index within that scene. Part of the primary key.</summary>
		public int PortalIndex { get; set; }

		/// <summary>Open for good.</summary>
		public bool Permanent { get; set; }

		/// <summary>UTC moment a timed opening ends; the Unix epoch when never timed.</summary>
		public DateTime ActiveUntil { get; set; }

		public DateTime TimeCreated { get; set; }
		public DateTime TimeUpdated { get; set; }
	}
}
