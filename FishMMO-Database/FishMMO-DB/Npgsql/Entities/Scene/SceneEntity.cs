using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Scene instance entity representing a currently loaded scene on a scene server.</summary>
	public class SceneEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Concurrency version for optimistic locking.</summary>
		public long Version { get; set; }
		/// <summary>Scene server instance ID that hosts this scene.</summary>
		public long SceneServerID { get; set; }
		/// <summary>World server ID this scene belongs to.</summary>
		public long WorldServerID { get; set; }
		/// <summary>Scene name (e.g. the Unity scene asset name).</summary>
		public string SceneName { get; set; }
		/// <summary>Scene handle assigned by the scene server.</summary>
		/// <summary>
		/// The hosting scene server's own scene-manager handle for this instance.
		/// </summary>
		/// <remarks>
		/// Diagnostic only. A scene-manager handle is assigned from a per-process counter, so it
		/// is meaningful solely inside the process that allocated it — two scene servers running
		/// the same build routinely produce the same value for different scenes. Anything that
		/// needs to identify a scene instance from outside its host uses <see cref="ID"/>.
		/// </remarks>
		public int SceneHandle { get; set; }
		/// <summary>Current status of the scene (e.g. loading, running, unloading).</summary>
		public int SceneStatus { get; set; }
		/// <summary>Scene type (over-world vs instanced).</summary>
		public int SceneType { get; set; }
		/// <summary>
		/// Character ID of the player than opened this scene if it's instanced.
		/// </summary>
		public long CharacterID { get; set; }
		/// <summary>Number of characters currently in this scene.</summary>
		public int CharacterCount { get; set; }

		/// <summary>
		/// Party that owns this instance, or 0 when it was opened by an ungrouped character.
		/// </summary>
		/// <remarks>
		/// The durable answer to "whose dungeon is this". <see cref="CharacterID"/> records only
		/// who happened to open it, and that character can leave the party, log out, or be dropped
		/// from it — after which the remaining members could no longer resolve their own instance
		/// and would silently open a second one, splitting the group. Membership is looked up
		/// through this column so the instance survives any change to who created it.
		/// </remarks>
		public long PartyID { get; set; }

		/// <summary>
		/// Difficulty this instance was opened at, as an index into the dungeon's own difficulty
		/// list. 0 is the first entry a dungeon declares.
		/// </summary>
		/// <remarks>
		/// Stored per instance rather than per dungeon because the same dungeon runs at several
		/// difficulties at once, and the ruleset has to survive the round trip through the queue:
		/// the scene server that eventually loads this row is not the one that enqueued it and has
		/// nothing else to read the choice from. Every dungeon declares its own list, so this is
		/// meaningful only alongside <see cref="SceneName"/>.
		/// </remarks>
		public int Difficulty { get; set; }

		/// <summary>
		/// True when the owning party has hidden this instance from the dungeon finder's list.
		/// </summary>
		/// <remarks>
		/// A lock on the front door, not on the instance: a private instance is still enterable by
		/// the party that owns it, it simply stops being offered to everyone else. Public by
		/// default, because a finder whose list is empty by default is not a finder.
		/// </remarks>
		public bool IsPrivate { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}