using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an ability crafter NPC that allows players to craft or modify abilities.
	/// </summary>
	/// <summary>
	/// AbilityCrafter NPC that allows players to craft or modify abilities. Inherits from Interactable.
	/// Displays a custom title and color in the UI.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class AbilityCrafter : Interactable
	{
		/// <summary>
		/// The display title for the ability crafter, shown in the UI.
		/// </summary>
		public override string Title { get { return "Ability Crafter"; } }

		/// <summary>
		/// The color of the title displayed for the ability crafter in the UI.
		/// Uses a lavender color for distinction.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.lavender); } }
	}
}