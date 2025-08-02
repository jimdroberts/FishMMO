using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template representing a character archetype, including rewards, requirements, and metadata.
	/// </summary>
	[CreateAssetMenu(fileName = "New Archetype", menuName = "FishMMO/Character/Archetype/Archetype", order = 1)]
	public class ArchetypeTemplate : CachedScriptableObject<ArchetypeTemplate>, ICachedObject
	{
		/// <summary>
		/// The NPC guild associated with this archetype, if any.
		/// </summary>
		public NPCGuildTemplate NPCGuild;

		/// <summary>
		/// The icon representing this archetype in the UI.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// The description of the archetype, shown to the player.
		/// </summary>
		public string Description;

		/// <summary>
		/// List of attribute templates rewarded for unlocking this archetype.
		/// </summary>
		public List<CharacterAttributeTemplate> AttributeRewards;

		/// <summary>
		/// List of ability templates rewarded for unlocking this archetype.
		/// </summary>
		public List<BaseAbilityTemplate> AbilityRewards;

		/// <summary>
		/// List of item templates rewarded for unlocking this archetype.
		/// </summary>
		public List<BaseItemTemplate> ItemRewards;

		/// <summary>
		/// List of buff templates rewarded for unlocking this archetype.
		/// </summary>
		public List<BaseBuffTemplate> BuffRewards;

		/// <summary>
		/// List of title strings rewarded for unlocking this archetype.
		/// </summary>
		public List<string> TitleRewards;

		/// <summary>
		/// The condition that must be met to unlock this archetype.
		/// </summary>
		public BaseCondition ArchetypeRequirements;

		/// <summary>
		/// The name of this archetype template (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Checks if the given player character meets the requirements for this archetype.
		/// </summary>
		/// <param name="playerCharacter">The player character to evaluate.</param>
		/// <returns>True if requirements are met, or if no requirements are set; otherwise, false.</returns>
		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (ArchetypeRequirements == null)
			{
				// If no requirements are set, assume requirements are met.
				//Log.Warning($"ArchetypeTemplate: No Archetype Requirements assigned for {this.name}. Assuming requirements are met.");
				return true;
			}
			return ArchetypeRequirements.Evaluate(playerCharacter);
		}
	}
}