using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for switch interactables.
	/// Exposes the switch target and toggle behaviour needed by the interaction handler.
	/// </summary>
	public interface ISwitch : IInteractable
	{
		/// <summary>
		/// The target that this switch activates or deactivates (door, chest, trap, etc.).
		/// </summary>
		ISwitchTarget SwitchTarget { get; }

		/// <summary>
		/// When true, the switch toggles between activated and deactivated states on each interaction.
		/// When false, the switch can only be activated once.
		/// </summary>
		bool IsToggle { get; }

		/// <summary>
		/// Achievement template to increment when a player operates this switch.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}