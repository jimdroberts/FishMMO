using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A housing structure in carried form: what comes out of the house vault.
	/// </summary>
	/// <remarks>
	/// <para>A structure standing on a plot is a database row, not an item, so it cannot sit in a bag.
	/// When land is lost or given up its structures go to the owner's vault; buying them back out of
	/// the vault turns each into this item, delivered to the bank (the vault is opened from the bank).
	/// Nothing turns an item back into a vault entry: the vault only empties.</para>
	///
	/// <para>The building tool is what places a kit back onto a plot. Until it exists a kit is
	/// something to keep, sell, or trade.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Structure Kit", menuName = "FishMMO/Item/Structure Kit", order = 1)]
	public class StructureKitTemplate : BaseItemTemplate
	{
		[Tooltip("The structure this kit builds.")]
		public PlotStructureTemplate Structure;

		[TextArea(1, 3)]
		public string Description;

		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);

			content.AddSubtitle("Structure kit");
			if (Structure != null)
			{
				Vector2 footprint = Structure.SafeFootprint;
				content.AddBody($"Builds {Structure.name}: {footprint.x:0.#} × {footprint.y:0.#} m, {Structure.SafeHeight:0.#} m tall.");
			}
			if (!string.IsNullOrWhiteSpace(Description))
			{
				content.AddBody(Description, tone: TooltipTone.Muted);
			}
		}
	}
}
