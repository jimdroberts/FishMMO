using System.Collections.Generic;
using UnityEngine;

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
		/// Optional icon displayed in the lore window.
		/// </summary>
		public Sprite Icon;

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
	}
}