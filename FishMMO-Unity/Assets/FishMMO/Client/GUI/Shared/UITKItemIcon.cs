using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Paints an item's icon into a slot or drag-ghost element, falling back to a visible
	/// placeholder when the item's template has no icon assigned.
	/// </summary>
	/// <remarks>
	/// This exists because "no icon" and "no item" were drawn identically. Every item surface
	/// wrote <c>backgroundImage</c> from <c>Template.Icon</c> and cleared it when that was null,
	/// so an item whose art is not in yet occupied a slot that rendered completely empty. The
	/// player could not see that the slot held anything, which made every interaction built on it
	/// undiscoverable — there was nothing on screen to press, drag, or right-click.
	/// <para>
	/// The placeholder is a filled square at the element's existing size rather than a substitute
	/// sprite. A sprite would have to be authored, imported and referenced from five call sites,
	/// and would be one more asset able to go missing in exactly the situation this handles. A
	/// USS class needs none of that and cannot itself fail to load without the whole theme
	/// failing with it.
	/// </para>
	/// <para>
	/// Whether the player may move an item is not the icon's business — see
	/// <see cref="UITKDragObject"/>, which used to cancel any drag carrying a null sprite. These
	/// two together are what make an art-less project playable: the drag is allowed to start, and
	/// there is something on screen to start it from.
	/// </para>
	/// </remarks>
	public static class UITKItemIcon
	{
		/// <summary>
		/// USS class marking an icon element that is standing in for missing art. Defined in
		/// FishMMO-Theme.uss so every item surface draws the same placeholder.
		/// </summary>
		public const string CSS_PLACEHOLDER = "fish-icon--placeholder";

		/// <summary>
		/// Shows <paramref name="sprite"/> in <paramref name="icon"/>, or the placeholder when it
		/// is null. Call this wherever a slot is filled.
		/// </summary>
		/// <param name="icon">The element that draws the icon. Null is tolerated.</param>
		/// <param name="sprite">The item's icon, or null if its template has none.</param>
		public static void Apply(VisualElement icon, Sprite sprite)
		{
			if (icon == null)
			{
				return;
			}

			if (sprite != null)
			{
				icon.style.backgroundImage = new StyleBackground(sprite);
				icon.RemoveFromClassList(CSS_PLACEHOLDER);
				return;
			}

			/* StyleKeyword.None, not a default StyleBackground. An empty StyleBackground is a
			 * value — it re-enters the cascade as "no texture" and leaves any inline background
			 * from a previous sprite in place on some element types; None removes the inline
			 * declaration outright and lets the class below be the only thing painting. */
			icon.style.backgroundImage = StyleKeyword.None;
			icon.AddToClassList(CSS_PLACEHOLDER);
		}

		/// <summary>
		/// Empties <paramref name="icon"/> completely. Call this wherever a slot is cleared —
		/// an empty slot must draw nothing at all, placeholder included.
		/// </summary>
		/// <param name="icon">The element that draws the icon. Null is tolerated.</param>
		public static void Clear(VisualElement icon)
		{
			if (icon == null)
			{
				return;
			}

			icon.style.backgroundImage = StyleKeyword.None;
			icon.RemoveFromClassList(CSS_PLACEHOLDER);
		}
	}
}
