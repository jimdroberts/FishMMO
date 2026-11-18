using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Something the UI can describe: an item, an ability, a buff, an attribute.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The contract is <b>data</b>. An implementer fills a <see cref="FishMMO.Shared.TooltipContent"/>
	/// with typed rows and never formats a string, because the same description is drawn in several
	/// places that disagree about how it should look: a hover tooltip, an inline details pane, a
	/// crafting preview that shows what each part changed, a merchant row.
	/// </para>
	/// <para>
	/// This used to be <c>string Tooltip()</c>, and every implementer concatenated rich text on the
	/// spot. Nothing downstream could read a stat back out, so the crafting panel could not preview
	/// what it was about to build and the items and abilities systems shared nothing but the
	/// interface name.
	/// </para>
	/// </remarks>
	public interface ITooltip
	{
		/// <summary>
		/// Gets the icon sprite to display in the tooltip.
		/// </summary>
		Sprite Icon { get; }

		/// <summary>
		/// Gets the display name for the tooltip.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Writes this object's description into <paramref name="content"/> as typed rows.
		/// </summary>
		/// <remarks>
		/// Rows are added with a priority from <see cref="FishMMO.Shared.TooltipPriority"/> and
		/// sorted once at the end, so a component's stats land in the stats block rather than after
		/// everything its parent wrote. Implementers append; they never clear.
		/// </remarks>
		/// <param name="content">The content being assembled.</param>
		void BuildTooltip(FishMMO.Shared.TooltipContent content);
	}
}
