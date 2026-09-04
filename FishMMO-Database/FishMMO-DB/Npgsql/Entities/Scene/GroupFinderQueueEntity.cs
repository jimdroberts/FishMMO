using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One character waiting in the dungeon group finder, or matched by it and about to be moved.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The queue lives in the database rather than in any one process because the players in it
	/// are spread across scene servers. Whoever presses Find Group first may be standing on a
	/// different scene server from the four people who complete their group, and the only thing
	/// all of those servers share is this table.
	/// </para>
	/// <para>
	/// A row is created when a character asks to find a group and deleted when they leave the
	/// queue, log out, or are moved into the run the finder built for them. A character has at
	/// most one row — the queue is not a wishlist — which is what <c>character_id</c>'s unique
	/// index enforces.
	/// </para>
	/// <para>
	/// <see cref="LastPulse"/> is a heartbeat written by the scene server the character is
	/// connected to. A row whose heartbeat has gone quiet belongs to a server that died or a
	/// character that vanished without the disconnect path running, and it is excluded from
	/// matching and eventually reaped rather than being handed a party it cannot join.
	/// </para>
	/// </remarks>
	public class GroupFinderQueueEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>World server the character belongs to. Groups never form across world servers.</summary>
		public long WorldServerID { get; set; }

		/// <summary>The waiting character. Unique: a character queues for one thing at a time.</summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// What kind of instance is being waited for: a dungeon (<c>Group</c>) or an arena
		/// (<c>PvP</c>), as the shared <c>SceneType</c> value.
		/// </summary>
		/// <remarks>
		/// Separates the two queues so a count or a match for one never reads the other's rows,
		/// and lets one table, one pump and one leash serve both.
		/// </remarks>
		public int SceneType { get; set; }

		/// <summary>
		/// Pre-made group this character queued with, or 0 when they queued alone.
		/// </summary>
		/// <remarks>
		/// Arenas allow a party to queue together; the composer keeps rows sharing a group id on
		/// one team and takes all of them or none. Dungeons never set it — the finder fills a
		/// party's dungeon through the open run instead.
		/// </remarks>
		public long GroupID { get; set; }

		/// <summary>Dungeon or arena scene the character wants to play.</summary>
		public string SceneName { get; set; }

		/// <summary>
		/// Index into the template's own list: a dungeon's difficulty, or an arena's format
		/// (team size).
		/// </summary>
		public int Difficulty { get; set; }

		/// <summary>Where the row is in its life. See <c>GroupFinderQueueStatus</c>.</summary>
		public int Status { get; set; }

		/// <summary>Party the character was matched into, or 0 while still waiting.</summary>
		public long PartyID { get; set; }

		/// <summary>Instance row the character was matched into, or 0 while still waiting.</summary>
		public long InstanceID { get; set; }

		/// <summary>When the character joined the queue (UTC). Decides who has waited longest.</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>Last heartbeat from the scene server hosting the character (UTC).</summary>
		public DateTime LastPulse { get; set; }

		/// <summary>When the row was matched (UTC), or null while waiting.</summary>
		public DateTime? TimeMatched { get; set; }
	}
}
