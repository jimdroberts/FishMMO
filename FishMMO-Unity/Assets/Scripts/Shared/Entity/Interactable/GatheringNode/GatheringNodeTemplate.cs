using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Template defining a gathering node's loot table and interaction parameters.
	/// Each gathering interaction rolls the drop table and grants items to the player.
	/// </summary>
	[CreateAssetMenu(fileName = "New Gathering Node", menuName = "FishMMO/Interactable/Gathering Node", order = 1)]
	public class GatheringNodeTemplate : CachedScriptableObject<GatheringNodeTemplate>, ICachedObject
	{
		/// <summary>
		/// Optional icon for the gathering node in the UI.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// Description displayed in tooltips or UI.
		/// </summary>
		[TextArea(2, 4)]
		public string Description;

		/// <summary>
		/// The list of possible drops when gathering from this node.
		/// </summary>
		public List<GatheringDrop> Drops;

		/// <summary>
		/// Number of successful gathers before the node is depleted and despawns.
		/// </summary>
		[Min(1)]
		public int MaxUses = 3;

		/// <summary>
		/// Time in seconds the gathering action takes. Used by the client for a progress bar.
		/// Set to 0 for instant gathering.
		/// </summary>
		[Min(0f)]
		public float GatherTimeSeconds = 2.0f;

		/// <summary>
		/// The display name of this gathering node template.
		/// </summary>
		public string Name { get { return this.name; } }
	}
}