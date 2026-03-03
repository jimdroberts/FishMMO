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
	}
}