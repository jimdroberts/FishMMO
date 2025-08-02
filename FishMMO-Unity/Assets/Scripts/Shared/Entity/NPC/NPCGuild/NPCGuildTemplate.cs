using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining NPC guilds, their icon, description, archetypes, and requirements.
	/// </summary>
	[CreateAssetMenu(fileName = "New NPC Guild", menuName = "FishMMO/Character/NPC/NPC Guild", order = 1)]
	public class NPCGuildTemplate : CachedScriptableObject<NPCGuildTemplate>, ICachedObject
	{
		/// <summary>
		/// The icon representing the guild in UI.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// Description of the guild and its purpose.
		/// </summary>
		public string Description;

		/// <summary>
		/// List of archetypes associated with this guild.
		/// </summary>
		public List<ArchetypeTemplate> Archetypes = new List<ArchetypeTemplate>();

		/// <summary>
		/// Requirements that a player must meet to join or interact with this guild.
		/// </summary>
		public BaseCondition GuildRequirements;

		/// <summary>
		/// The name of the guild, derived from the asset name.
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Checks if the given player character meets the guild's requirements.
		/// Returns true if requirements are met or if no requirements are set.
		/// </summary>
		/// <param name="playerCharacter">The player character to evaluate.</param>
		/// <returns>True if requirements are met, false otherwise.</returns>
		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (GuildRequirements == null)
			{
				// If no requirements are set, assume requirements are met.
				//Log.Warning($"NPCGuildTemplate: No Guild Requirements assigned for {this.name}. Assuming requirements are met.");
				return true;
			}
			// Evaluate the requirements condition for the player character.
			return GuildRequirements.Evaluate(playerCharacter);
		}
	}
}