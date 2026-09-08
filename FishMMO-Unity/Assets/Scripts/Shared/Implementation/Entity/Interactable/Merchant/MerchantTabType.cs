namespace FishMMO.Shared
{
	/// <summary>
	/// Specifies the types of tabs available in a merchant's UI, representing different categories of goods or services.
	/// </summary>
	public enum MerchantTabType : byte
	{
		/// <summary>
		/// No tab selected or no category available.
		/// </summary>
		None = 0,

		/// <summary>
		/// Tab for abilities that can be purchased or learned from the merchant.
		/// </summary>
		Ability,

		/// <summary>
		/// Tab for ability events, such as triggers or special actions related to abilities.
		/// </summary>
		AbilityEvent,

		/// <summary>
		/// Tab for items that can be bought or sold.
		/// </summary>
		Item,

		/// <summary>
		/// Tab for complete, premade abilities (<see cref="PremadeAbilityTemplate"/>). Bought
		/// straight into the usable set; nothing to craft afterwards.
		/// </summary>
		/// <remarks>
		/// Appended, never inserted. This enum is part of the purchase wire format and the server
		/// switches on it, so the existing values must keep their numbers.
		/// </remarks>
		PremadeAbility,
	}
}