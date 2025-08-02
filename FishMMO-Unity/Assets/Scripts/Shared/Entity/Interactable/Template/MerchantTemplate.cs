using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Merchant", menuName = "FishMMO/Character/Merchant/Merchant", order = 1)]
	public class MerchantTemplate : CachedScriptableObject<MerchantTemplate>, ICachedObject
	{
		/// <summary>
		/// The icon representing this merchant in the UI.
		/// </summary>
		public Sprite icon;

		/// <summary>
		/// Description of the merchant, used for tooltips and UI.
		/// </summary>
		public string Description;

		/// <summary>
		/// List of abilities that this merchant can offer or use.
		/// </summary>
		public List<AbilityTemplate> Abilities;

		/// <summary>
		/// List of ability events associated with this merchant (e.g., triggers for special actions).
		/// </summary>
		public List<AbilityEvent> AbilityEvents;

		/// <summary>
		/// List of items available for sale by this merchant.
		/// </summary>
		public List<BaseItemTemplate> Items;

		/// <summary>
		/// The display name of the merchant (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing this merchant in the UI (property accessor).
		/// </summary>
		public Sprite Icon { get { return this.icon; } }
	}
}