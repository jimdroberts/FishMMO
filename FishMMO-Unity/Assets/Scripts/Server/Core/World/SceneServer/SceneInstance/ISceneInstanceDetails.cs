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