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
		/// Runtime handle or identifier assigned to the loaded scene instance by the
		/// scene manager. This value is typically unique on the scene server and is
		/// used when loading/unloading or routing connections to the instance.
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