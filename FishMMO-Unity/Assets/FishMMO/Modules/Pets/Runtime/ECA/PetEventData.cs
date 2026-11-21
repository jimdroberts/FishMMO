using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for pet summon and dismiss events.
	/// </summary>
	public class PetEventData : EventData
	{
		/// <summary>
		/// The pet involved. Null when a pet is dismissed.
		/// </summary>
		public Pet Pet { get; }

		/// <summary>
		/// Creates a new PetEventData.
		/// </summary>
		/// <param name="initiator">The character summoning or dismissing the pet.</param>
		/// <param name="pet">The pet involved, or null for dismiss events.</param>
		public PetEventData(ICharacter initiator, Pet pet)
			: base(initiator)
		{
			Pet = pet;
		}
	}
}