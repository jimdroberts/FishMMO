using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for damage-related triggers. Carries the damage amount and
	/// damage attribute type. The damaged character is exposed on <see cref="EventData.TargetCharacter"/>.
	/// </summary>
	public class DamageEventData : EventData
	{
		/// <summary>
		/// The amount of damage dealt after modifiers.
		/// </summary>
		public int Amount { get; }

		/// <summary>
		/// The damage attribute type (e.g. physical, fire) used in this damage event.
		/// </summary>
		public DamageAttributeTemplate DamageAttribute { get; }

		/// <summary>
		/// Creates a new DamageEventData.
		/// </summary>
		/// <param name="initiator">The character whose triggers are being invoked.</param>
		/// <param name="target">The other character involved in the damage interaction.</param>
		/// <param name="amount">The damage amount after modifiers.</param>
		/// <param name="damageAttribute">The damage attribute type.</param>
		public DamageEventData(ICharacter initiator, ICharacter target, int amount, DamageAttributeTemplate damageAttribute)
			: base(initiator, target)
		{
			Amount = amount;
			DamageAttribute = damageAttribute;
		}
	}
}