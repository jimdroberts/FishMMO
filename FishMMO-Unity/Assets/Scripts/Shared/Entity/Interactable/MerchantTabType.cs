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
	}
}