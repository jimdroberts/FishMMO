using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for heal-related triggers. Carries the heal amount.
	/// Automatically includes a CharacterHitEventData with the target.
	/// </summary>
	public class HealEventData : EventData
	{
		/// <summary>
		/// The amount healed.
		/// </summary>
		public int Amount { get; }

		/// <summary>
		/// Creates a new HealEventData.
		/// </summary>
		/// <param name="initiator">The character whose triggers are being invoked.</param>
		/// <param name="target">The other character involved in the heal interaction.</param>
		/// <param name="amount">The heal amount.</param>
		public HealEventData(ICharacter initiator, ICharacter target, int amount)
			: base(initiator, new CharacterHitEventData(initiator, target))
		{
			Amount = amount;
		}
	}
}