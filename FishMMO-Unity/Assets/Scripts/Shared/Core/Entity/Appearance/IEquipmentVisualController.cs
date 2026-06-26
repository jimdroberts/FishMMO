using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages visual representation of equipped items on a character.
	/// Handles mesh loading, skeleton binding, renderer pooling, and body region hiding.
	/// </summary>
	public interface IEquipmentVisualController : ICharacterBehaviour
	{
		/// <summary>
		/// Refreshes all equipment visuals from the current equipment state.
		/// Called after character spawn or appearance data load.
		/// </summary>
		void RefreshAllEquipment();
	}
}
