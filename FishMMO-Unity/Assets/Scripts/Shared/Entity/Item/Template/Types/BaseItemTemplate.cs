using Cysharp.Text;
using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for item templates, providing common properties and tooltip logic for all items.
	/// Implements ITooltip and ICachedObject for UI and caching support.
	/// </summary>
	public abstract class BaseItemTemplate : CachedScriptableObject<BaseItemTemplate>, ITooltip, ICachedObject
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
		/// The icon sprite representing this item in the UI.
		/// </summary>
		public Sprite icon;

		/// <summary>
		/// The mesh used for 3D representation of the item.
		/// </summary>
		public Mesh Mesh;

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
		/// Gets the icon sprite for this item.
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Returns the formatted tooltip string for this item, including name and price.
		/// </summary>
		/// <returns>The formatted tooltip string.</returns>
		public virtual string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, false, "f5ad6e", "120%"));
				sb.Append("\r\n______________________________\r\n");
				if (Price > 0)
				{
					sb.Append(RichText.Format("Price", Price, true, "a66ef5FF"));
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Returns the formatted tooltip string for this item, optionally combining with other tooltips.
		/// </summary>
		/// <param name="combineList">A list of other tooltips to combine (not used in base implementation).</param>
		/// <returns>The formatted tooltip string.</returns>
		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return Tooltip();
		}

		/// <summary>
		/// Returns a formatted description string for the item. Override to provide custom descriptions.
		/// </summary>
		/// <returns>The formatted description string.</returns>
		public virtual string GetFormattedDescription()
		{
			return "";
		}
	}
}