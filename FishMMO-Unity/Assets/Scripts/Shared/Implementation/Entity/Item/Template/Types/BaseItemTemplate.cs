using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for item templates, providing common properties and tooltip logic for all items.
	/// Implements ITooltip for UI display and ICachedObject for template caching.
	/// </summary>
	public abstract class BaseItemTemplate : CachedScriptableObject<BaseItemTemplate>, ICachedObject, ITooltip
	{
		/// <summary>
		/// Indicates if the item can be identified (e.g., has hidden stats).
		/// </summary>
		public bool IsIdentifiable;

		/// <summary>
		/// Indicates if the item should be generated (used in item generation systems).
		/// </summary>
		public bool Generate;

		/// <summary>
		/// The maximum stack size for this item (if greater than 1, item is stackable).
		/// </summary>
		public uint MaxStackSize = 1;

		/// <summary>
		/// The price of the item, used for buying/selling.
		/// </summary>
		public int Price;

		/// <summary>
		/// Pools of icons for item generation and randomization.
		/// </summary>
		public int[] IconPools;

		/// <summary>
		/// Addressable reference to the icon sprite for this item.
		/// </summary>
		public AssetReferenceSprite icon;

		/// <summary>
		/// The loaded icon sprite. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Sprite loadedIcon;

		/// <summary>
		/// Addressable reference to the mesh for 3D representation of the item.
		/// </summary>
		public AssetReference MeshReference;

		/// <summary>
		/// The loaded mesh. Only available on the client after OnLoad completes.
		/// </summary>
		[System.NonSerialized]
		private Mesh loadedMesh;

		/// <summary>
		/// The base attributes added to the item after generation.
		/// </summary>
		[Tooltip("The base attributes that are added to the item after the ItemGenerator has completed.")]
		public List<ItemAttributeTemplate> Attributes;

		/// <summary>
		/// Gets the name of this item template (defaults to the asset name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Returns true if the item is stackable (MaxStackSize > 1).
		/// </summary>
		public bool IsStackable { get { return MaxStackSize > 1; } }

		/// <summary>
		/// Gets the icon sprite for this item (loaded at runtime on client).
		/// </summary>
		public Sprite Icon { get { return this.loadedIcon; } }

		/// <summary>
		/// Gets the mesh for this item (loaded at runtime on client).
		/// </summary>
		public Mesh Mesh { get { return this.loadedMesh; } }

		/// <summary>
		/// Returns the formatted tooltip string for this item, including name and price.
		/// </summary>
		/// <returns>The formatted tooltip string.</returns>
		public virtual string Tooltip()
		{
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder);
				return builder.Build();
			}
		}

		/// <summary>
		/// Populates the tooltip builder with this item's tooltip lines.
		/// Override in derived types to add additional lines.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public virtual void BuildTooltip(TooltipBuilder builder)
		{
			builder.AddLine(Name, 0, TooltipColors.Title, false, "120%");
			builder.AddSeparator(10);
			if (Price > 0)
			{
				builder.AddLine($"Price: {Price}", 80, TooltipColors.Label);
			}
		}

		/// <summary>
		/// Called when the item template is loaded into cache. Loads the icon and mesh on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);

			if (typeName != nameof(BaseItemTemplate))
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

			if (MeshReference != null && MeshReference.RuntimeKeyIsValid())
			{
				MeshReference.LoadAssetAsync<Mesh>().Completed += (handle) =>
				{
					if (handle.Status == AsyncOperationStatus.Succeeded)
						loadedMesh = handle.Result;
				};
			}
#endif
		}

		/// <summary>
		/// Called when the item template is unloaded from cache. Releases the icon and mesh on the client.
		/// </summary>
		/// <param name="typeName">The type name of the resource.</param>
		/// <param name="resourceName">The resource name.</param>
		/// <param name="resourceID">The resource ID.</param>
		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			if (typeName == nameof(BaseItemTemplate))
			{
#if !UNITY_SERVER
				if (icon != null && icon.IsValid())
				{
					icon.ReleaseAsset();
				}
				loadedIcon = null;

				if (MeshReference != null && MeshReference.IsValid())
				{
					MeshReference.ReleaseAsset();
				}
				loadedMesh = null;
#endif
			}

			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}