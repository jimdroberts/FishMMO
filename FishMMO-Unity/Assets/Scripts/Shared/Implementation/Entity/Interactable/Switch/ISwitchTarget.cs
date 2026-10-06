using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for objects that can be activated or deactivated by a <see cref="Switch"/> interactable.
	/// Implement on doors, chests, traps, or any scene object that responds to switch interactions.
	/// </summary>
	public interface ISwitchTarget
	{
		/// <summary>
		/// Whether this target is currently in the activated state.
		/// </summary>
		bool IsActivated { get; }

		/// <summary>
		/// Activates this target (e.g., opens a door, unlocks a chest, disarms a trap).
		/// </summary>
		/// <param name="activator">The player character who triggered the switch.</param>
		void Activate(IPlayerCharacter activator);

		/// <summary>
		/// Deactivates this target (e.g., closes a door, locks a chest, re-arms a trap).
		/// </summary>
		/// <param name="activator">The player character who triggered the switch.</param>
		void Deactivate(IPlayerCharacter activator);

		/// <summary>
		/// Puts this target into the given state with no transition.
		/// </summary>
		/// <remarks>
		/// For catching up rather than for reacting. A client that starts observing a switch reads
		/// the switch's current state out of its spawn payload, and that state describes something
		/// that happened before the player arrived — possibly hours before. Replaying it through
		/// <see cref="Activate"/> would play the transition too, so walking into a room would set
		/// every door in it swinging. Nothing on the interaction path calls this.
		/// </remarks>
		/// <param name="activated">The state to adopt immediately.</param>
		void SnapTo(bool activated);
	}

	/// <summary>
	/// A <see cref="ISwitchTarget"/> whose change plays out over time, from the server tick it began
	/// at: every peer works out the same pose from the tick, so a door is in the same place on every
	/// screen and on the server, and a player who arrives mid-swing sees it mid-swing.
	/// </summary>
	public interface ITimedSwitchTarget : ISwitchTarget
	{
		/// <summary>The server tick the last change began at (0: none since the scene loaded).</summary>
		uint ChangeTick { get; }

		/// <summary>How far along its travel it stood when that change began, 0..1.</summary>
		float ChangeTravel { get; }

		/// <summary>Adopts the server's state: <paramref name="activated"/>, changed at <paramref name="changeTick"/> from <paramref name="changeTravel"/>.</summary>
		void SetState(bool activated, uint changeTick, float changeTravel);
	}
}