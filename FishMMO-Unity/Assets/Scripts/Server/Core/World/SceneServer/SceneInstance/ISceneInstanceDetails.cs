using System;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic, read-only view of a scene instance managed by a scene server.
	/// This interface mirrors the implementation-level <c>SceneInstanceDetails</c>
	/// while avoiding engine-specific types so core code can safely consume the data.
	/// </summary>
	/// <remarks>
	/// Implementations should document any threading guarantees for the exposed
	/// properties and keep the values (for example <see cref="CharacterCount"/>
	/// and <see cref="LastExit"/>) updated as instance state changes.
	/// </remarks>
	public interface ISceneInstanceDetails
	{
		/// <summary>
		/// The world server identifier that owns this scene instance. This links the
		/// instance to a specific world server record in central services.
		/// </summary>
		long WorldServerID { get; set; }

		/// <summary>
		/// The scene server identifier that created/hosts this instance. Useful for
		/// tracing which scene server owns the instance in multi-server deployments.
		/// </summary>
		long SceneServerID { get; set; }

		/// <summary>
		/// The canonical name of the scene (for example: "ForestZone").
		/// </summary>
		string Name { get; set; }

		/// <summary>
		/// Database ID of the <c>scenes</c> row this instance was loaded for. The identity of
		/// the instance everywhere outside the hosting process.
		/// </summary>
		/// <remarks>
		/// <see cref="Handle"/> cannot serve this purpose. It is the scene manager's own
		/// identifier for a loaded scene, assigned from a per-process counter, so two scene
		/// servers running the same build and loading the same scenes in the same order
		/// routinely allocate identical handles. Using it as a cross-process identity meant the
		/// world server's handle-to-server map collided between scene servers, and a scene server
		/// happily accepted a character routed to a different server's instance because the
		/// handle and scene name both matched. The row id is unique by construction.
		/// </remarks>
		long SceneID { get; set; }

		/// <summary>
		/// Runtime handle assigned to the loaded scene by this process's scene manager. Valid
		/// only inside the scene server that loaded it, and never to be persisted or sent to
		/// another process as an identifier — see <see cref="SceneID"/>.
		/// </summary>
		int Handle { get; set; }

		/// <summary>
		/// The logical scene type (for example open world, instanced dungeon, PvP arena).
		/// Consumers may use this to apply different connection or persistence logic.
		/// </summary>
		SceneType SceneType { get; set; }

		/// <summary>
		/// Current number of characters present in the scene instance. Implementations
		/// should keep this value up-to-date to support capacity checks and stale
		/// instance detection.
		/// </summary>
		int CharacterCount { get; set; }

		/// <summary>
		/// Indicates whether the scene is stale (no characters present).
		/// </summary>
		bool StalePulse { get; }

		/// <summary>
		/// Timestamp when the last character exited the instance. Useful for stale
		/// instance detection and cleanup heuristics.
		/// </summary>
		DateTime LastExit { get; set; }

		/// <summary>
		/// True when the instance emptied because its occupants CHOSE to leave, rather than
		/// because they went away.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An empty instance means two very different things. Everyone walked out of the dungeon:
		/// the run is over, nobody is coming back, and holding the scene for a timeout wastes a
		/// placement slot. The last player's connection dropped: the run is not over, and reaping
		/// immediately would destroy their progress before they could reconnect to it.
		/// </para>
		/// <para>
		/// Set by <c>CharacterSystem.TryLeaveInstance</c>, which is the only voluntary route out —
		/// the leave-instance broadcast, the <c>/leaveinstance</c> command, and the forced return
		/// when an instance is being closed. Cleared whenever anybody is present again, so a
		/// returning player does not inherit the previous departure's verdict.
		/// </para>
		/// <para>
		/// Note this is not the same distinction combat logout makes: that keeps a disconnected
		/// body counted as PRESENT so the scene never looks empty at all. This covers the case
		/// where the scene really is empty and the question is how long to hold it.
		/// </para>
		/// </remarks>
		bool VacatedDeliberately { get; set; }

		/// <summary>
		/// When the scene row this instance was loaded for was created.
		/// </summary>
		/// <remarks>
		/// The row's creation time rather than the moment the scene finished loading, so the age
		/// this yields includes the time spent queued and loading. That is the age a lifetime cap
		/// has to measure: an instance that took a minute to come up has still been occupying a
		/// slot for that minute.
		/// <para>
		/// Distinct from <see cref="LastExit"/>, which measures how long an instance has been
		/// <em>empty</em>. The two bound different things — an abandoned instance and an endless
		/// one — and neither substitutes for the other.
		/// </para>
		/// </remarks>
		DateTime CreatedUtc { get; set; }

		/// <summary>
		/// Character the instance was created for. Zero for an open-world scene.
		/// </summary>
		/// <remarks>
		/// Taken from the scene row's <c>character_id</c>, which the dungeon finder stamps when it
		/// requests the instance. Records who opened the run, which is not the same as who leads
		/// it: leadership is the owning party's and moves with it — see <see cref="PartyID"/>.
		/// The owner is the fallback authority for a run that has no party at all.
		/// </remarks>
		long OwnerCharacterID { get; set; }

		/// <summary>
		/// Party that owns this instance, or 0 when an ungrouped character opened it.
		/// </summary>
		/// <remarks>
		/// The durable identity of an instance's group, and the anchor for everything about
		/// controlling it: the instance's leader is this party's leader, kick authority is that
		/// leader's, and the dungeon finder resolves a party's own instance through this rather
		/// than through whoever happened to create it — so the run stays findable after its opener
		/// has left or logged out.
		/// </remarks>
		long PartyID { get; set; }

		/// <summary>
		/// Difficulty index this instance was opened at, into the dungeon's own difficulty list.
		/// </summary>
		/// <remarks>
		/// Meaningful only alongside <see cref="Name"/>: every dungeon declares its own list, and
		/// there is no global set of difficulty levels. Zero for an open-world scene and for a
		/// dungeon that declares no difficulties.
		/// </remarks>
		int Difficulty { get; set; }

		/// <summary>
		/// Whether the owning party has hidden this instance from the dungeon finder's list.
		/// </summary>
		/// <remarks>
		/// A lock on the front door, not on the instance. A private instance is still enterable by
		/// the party that owns it — which is what keeps re-entry working for a run that has been
		/// closed to strangers — it simply stops being offered to everybody else.
		/// </remarks>
		bool IsPrivate { get; set; }

		/// <summary>
		/// Adds to the current character count for the scene instance.
		/// </summary>
		/// <param name="count">Amount to add to the character count. May be negative to decrement. Implementations should clamp the resulting count to zero if necessary.</param>
		/// <remarks>
		/// This method is a convenience used by scene server implementations to
		/// update the <see cref="CharacterCount"/>. Callers should prefer
		/// atomic or synchronized implementations when updating counts from
		/// multiple threads.
		/// </remarks>
		void AddCharacterCount(int count);
	}
}