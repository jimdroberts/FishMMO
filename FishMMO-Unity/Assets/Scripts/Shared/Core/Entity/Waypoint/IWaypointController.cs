using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The set of waypoints a character has discovered, keyed by scene, and the events the
	/// waypoint system raises around it.
	/// </summary>
	/// <remarks>
	/// <para>Discovery is server-authoritative. The server unlocks a waypoint when the character
	/// interacts with it (or a designer grants it), persists the unlock, and tells the owner; the
	/// owner's copy exists so the world map can draw what it knows without asking. Nothing the
	/// client holds here is trusted for anything — a fast-travel request is re-checked against the
	/// server's copy.</para>
	/// <para>The static events are the seams the rest of the game hangs off: the server's
	/// interactable system persists and broadcasts on <see cref="OnWaypointUnlocked"/>, the client
	/// map redraws on it, and the UI plays its sounds on the travel events. They are static because
	/// the controller lives on the character and the systems that care outlive any character.</para>
	/// </remarks>
	public interface IWaypointController : ICharacterBehaviour
	{
		/// <summary>
		/// Raised when a waypoint becomes unlocked for a character. On the server this fires from
		/// the unlock itself; on the owner client it fires when the server's notification arrives.
		/// <para>Parameters: the character, the scene name, the waypoint index.</para>
		/// </summary>
		static Action<ICharacter, string, int> OnWaypointUnlocked;

		/// <summary>
		/// Raised after a character has been moved to a waypoint. Server: from the travel routine.
		/// Owner client: when the server's confirmation arrives.
		/// <para>Parameters: the character, the scene name, the waypoint index.</para>
		/// </summary>
		static Action<ICharacter, string, int> OnWaypointTravelled;

		/// <summary>
		/// Raised on the owner client when the server declines a fast-travel request.
		/// <para>Parameters: the character, the scene name, the waypoint index, the reason.</para>
		/// </summary>
		static Action<ICharacter, string, int, WaypointTravelRefusalReason> OnWaypointTravelRefused;

		/// <summary>
		/// Raised on the owner client when the server asks the map to open on a waypoint — the
		/// response to interacting with a waypoint the character had already discovered.
		/// <para>Parameters: the character, the scene name, the waypoint index.</para>
		/// </summary>
		static Action<ICharacter, string, int> OnWaypointMapRequested;

		/// <summary>Every scene the character has at least one unlocked waypoint in.</summary>
		IReadOnlyCollection<string> UnlockedScenes { get; }

		/// <summary>Whether the character has discovered a waypoint.</summary>
		/// <param name="sceneName">The scene the waypoint stands in.</param>
		/// <param name="waypointIndex">The waypoint's authored index within that scene.</param>
		bool IsUnlocked(string sceneName, int waypointIndex);

		/// <summary>
		/// Marks a waypoint as discovered and raises <see cref="OnWaypointUnlocked"/> when it was
		/// not already.
		/// </summary>
		/// <param name="sceneName">The scene the waypoint stands in.</param>
		/// <param name="waypointIndex">The waypoint's authored index within that scene.</param>
		/// <returns>True when the waypoint was newly unlocked; false when it already was, or the arguments were invalid.</returns>
		bool Unlock(string sceneName, int waypointIndex);

		/// <summary>
		/// Installs a persisted page of unlock bits without raising events or dirtying anything.
		/// The load path, and the owner client's spawn payload.
		/// </summary>
		/// <param name="sceneName">The scene the page belongs to.</param>
		/// <param name="page">Which 64-waypoint page of the scene.</param>
		/// <param name="mask">The unlocked bits of that page.</param>
		void Restore(string sceneName, int page, ulong mask);

		/// <summary>
		/// The bits unlocked since the database last confirmed them, grouped per scene page, so
		/// the save path can write only what changed.
		/// </summary>
		/// <param name="results">Receives one entry per page with unwritten bits.</param>
		void CollectDirtyPages(List<WaypointPageSnapshot> results);

		/// <summary>
		/// Records that a page's bits reached the database, so they are no longer dirty.
		/// </summary>
		/// <remarks>
		/// The bits are OR-ed into the confirmed set rather than replacing it. The database write
		/// is itself an OR-merge, so any bit that was written is durable regardless of what else
		/// was unlocked while the write was in flight — those later bits stay dirty on their own
		/// and go out with the next save. No version counter is needed for that; the mask is its
		/// own version.
		/// </remarks>
		/// <param name="sceneName">The scene the page belongs to.</param>
		/// <param name="page">Which page.</param>
		/// <param name="writtenMask">The bits the database confirmed.</param>
		void MarkPersisted(string sceneName, int page, ulong writtenMask);
	}

	/// <summary>
	/// One scene page of unlock bits, as handed to the save path.
	/// </summary>
	public readonly struct WaypointPageSnapshot
	{
		/// <summary>The scene the page belongs to.</summary>
		public readonly string SceneName;

		/// <summary>Which 64-waypoint page of the scene.</summary>
		public readonly int Page;

		/// <summary>The unlocked bits of that page. Includes bits already persisted.</summary>
		public readonly ulong Mask;

		public WaypointPageSnapshot(string sceneName, int page, ulong mask)
		{
			SceneName = sceneName;
			Page = page;
			Mask = mask;
		}
	}
}
