using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scroll that teaches ability knowledge when it is read.
	/// </summary>
	/// <remarks>
	/// The concrete <see cref="ScrollConsumableTemplate"/>. That class and
	/// <see cref="ConsumableTemplate"/> under it were both abstract with nothing deriving from
	/// either, so the scroll behaviour existed in code and could not be authored as an asset.
	/// Everything it does is inherited; this exists so a scroll can be created.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Knowledge Scroll", menuName = "FishMMO/Character/Item/Consumable/Knowledge Scroll", order = 1)]
	public class KnowledgeScrollTemplate : ScrollConsumableTemplate
	{
	}
}
