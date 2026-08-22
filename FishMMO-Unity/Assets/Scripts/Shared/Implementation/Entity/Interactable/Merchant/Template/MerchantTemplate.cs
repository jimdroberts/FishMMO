using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Merchant", menuName = "FishMMO/Character/Merchant/Merchant", order = 1)]
	public class MerchantTemplate : CachedScriptableObject<MerchantTemplate>, ICachedObject
	{
		/// <summary>
		/// Addressable reference to the icon sprite for this merchant.
		/// </summary>
		public AssetReferenceSprite icon;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

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
		/// Whether this merchant buys items from the player.
		/// </summary>
		/// <remarks>
		/// Off by default so adding the sell path does not silently turn every existing merchant —
		/// including quest givers and trainers that happen to use a merchant template — into a
		/// general fence. Content opts in.
		/// </remarks>
		public bool BuysItems = false;

		/// <summary>
		/// Fraction of an item's template price the merchant pays for it.
		/// </summary>
		/// <remarks>
		/// Lives on the template, and therefore on the server, because it is half of the sale
		/// price and the client is never allowed to contribute to a price. The payout is
		/// <c>floor(Template.Price * SellPriceMultiplier) * quantity</c>, computed server-side from
		/// the item actually found in the named slot.
		/// </remarks>
		[Range(0.0f, 1.0f)]
		public float SellPriceMultiplier = 0.25f;

		/// <summary>
		/// The display name of the merchant (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing this merchant in the UI (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Called when the merchant template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(MerchantTemplate))
				return;

#if !UNITY_SERVER
			if (icon != null && icon.RuntimeKeyIsValid())
			{
				icon.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the merchant template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(MerchantTemplate))
			{
#if !UNITY_SERVER
				if (icon != null && icon.IsValid())
				{
					icon.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}