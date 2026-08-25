using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Party entity representing a player party group and its member associations.</summary>
	public class PartyEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }
		/// <summary>
		/// World server this party belongs to.
		/// </summary>
		/// <remarks>
		/// A party exists on one world server and nowhere else. Characters are global — the same
		/// character can be played on any world server, deliberately, so that friends can play
		/// together wherever they are — but a party spans scene servers by being replicated
		/// through the database, and that replication is scoped to a world server. A party whose
		/// members were spread across two of them would be updated by pumps that cannot see each
		/// other, invited to instances that do not exist on the other side, and shown rosters that
		/// never converge.
		/// <para>
		/// So this is the column that lets a character's party membership be dropped when they
		/// arrive on a different world server, rather than being carried into one where it cannot
		/// work.
		/// </para>
		/// </remarks>
		public long WorldServerID { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Navigation collection of party member entries.</summary>
		public List<CharacterPartyEntity> Characters { get; set; }
	}
}