using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A raw material: ore, crystal, herb, fungus, wood. Stackable, sellable, and otherwise inert —
	/// it exists to be gathered and, later, crafted with.
	/// </summary>
	/// <remarks>
	/// <para>The project had no concrete item type for a thing that is only carried: every
	/// <see cref="BaseItemTemplate"/> subclass was equipment or a consumable. Gathering nodes need
	/// something to drop, so the generated nodes (Jim, 2026-10-10: generate placeholder resource
	/// items) drop these. No crafting system reads them yet; <see cref="Family"/> and
	/// <see cref="Tier"/> are the handles one will key on.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Crafting Material", menuName = "FishMMO/Item/Crafting Material", order = 1)]
	public class CraftingMaterialTemplate : BaseItemTemplate
	{
		/// <summary>What kind of material: "Ore", "Crystal", "Herb", "Fungus", "Wood".</summary>
		[Tooltip("What kind of material: Ore, Crystal, Herb, Fungus, Wood.")]
		public string Family;

		/// <summary>Rarity tier, 1 common … 4 rare.</summary>
		[Tooltip("Rarity tier, 1 common to 4 rare.")]
		[Range(1, 4)]
		public int Tier = 1;

		/// <summary>Flavour line shown under the stats.</summary>
		[TextArea(1, 3)]
		public string Description;

		/// <inheritdoc />
		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);

			content.AddSubtitle(string.IsNullOrWhiteSpace(Family) ? "Material" : $"{Family}, tier {Tier}");
			if (!string.IsNullOrWhiteSpace(Description))
			{
				content.AddBody(Description, tone: TooltipTone.Muted);
			}
		}
	}
}
