using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Template defining a lore object's display text and optional immediate unlocks.
	/// When interacted with, the lore text is displayed in a UILore window.
	/// Abilities, ability events, and items listed here are granted immediately on interaction.
	/// Abilities and events are idempotent (already-known entries are skipped).
	/// </summary>
	[CreateAssetMenu(fileName = "New Lore Object", menuName = "FishMMO/Interactable/Lore Object", order = 1)]
	public class LoreObjectTemplate : CachedScriptableObject<LoreObjectTemplate>, ICachedObject
	{
		/// <summary>
		/// Display title for the lore window header.
		/// </summary>
		public string Title;

		/// <summary>
		/// Addressable reference to the icon sprite for this lore object.
		/// </summary>
		public AssetReferenceSprite IconReference;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// The icon for this lore object (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// The lore text displayed to the player.
		/// </summary>
		[TextArea(5, 20)]
		public string LoreText;

		[Header("Optional Grants")]
		/// <summary>
		/// Base abilities immediately learned on interaction. Already-known abilities are skipped.
		/// </summary>
		public List<BaseAbilityTemplate> GrantAbilities;

		/// <summary>
		/// Ability events immediately learned on interaction. Already-known events are skipped.
		/// </summary>
		public List<AbilityEvent> GrantAbilityEvents;

		/// <summary>
		/// Items immediately added to the player's inventory on interaction.
		/// </summary>
		public List<BaseItemTemplate> GrantItems;

		/// <summary>
		/// The display name of this lore object template.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Called when the lore object template is loaded into cache. Loads the icon on the client.
		/// </summary>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(LoreObjectTemplate))
				return;

#if !UNITY_SERVER
			if (IconReference != null && IconReference.RuntimeKeyIsValid())
			{
				IconReference.LoadAssetAsync<Sprite>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedIcon = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the lore object template is unloaded from cache. Releases the icon on the client.
		/// </summary>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(LoreObjectTemplate))
			{
#if !UNITY_SERVER
				if (IconReference != null && IconReference.IsValid())
				{
					IconReference.ReleaseAsset();
				}
				loadedIcon = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}