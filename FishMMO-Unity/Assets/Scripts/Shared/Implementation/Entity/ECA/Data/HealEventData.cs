using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for heal-related triggers. Carries the heal amount.
	/// The healed character is exposed on <see cref="EventData.TargetCharacter"/>.
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
			: base(initiator, target)
		{
			Amount = amount;
		}
	}
}