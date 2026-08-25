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
}