using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Teaches a character abilities and ability events named by content the core or another module owns (a lore object's
	/// reward): installed by the Abilities module, which skips what the character already knows, learns the
	/// rest and tells its client; and equips an NPC's authored abilities. Without the module nothing is learned.
	/// </summary>
	public static class AbilityGrants
	{
		/// <summary>Grants on the server; installed by the Abilities module.</summary>
		public static System.Action<ICharacter, IReadOnlyList<CachedScriptableObject>, IReadOnlyList<Trigger>> Granter;

		/// <summary>Teaches <paramref name="character"/> the ability templates and ability events listed (either may be null).</summary>
		public static void Grant(ICharacter character, IReadOnlyList<CachedScriptableObject> abilities, IReadOnlyList<Trigger> abilityEvents)
		{
			if (character == null || Granter == null)
			{
				return;
			}
			if ((abilities == null || abilities.Count == 0) && (abilityEvents == null || abilityEvents.Count == 0))
			{
				return;
			}
			Granter(character, abilities, abilityEvents);
		}

		/// <summary>Equips on the server; installed by the Abilities module.</summary>
		public static System.Action<ICharacter, IReadOnlyList<CachedScriptableObject>> Equipper;

		/// <summary>
		/// Gives <paramref name="character"/> a usable ability for each template listed (an NPC's authored kit, learned as
		/// it starts on the server): the template's ID is the instance's, since nothing crafted it. Nothing without the module.
		/// </summary>
		public static void Equip(ICharacter character, IReadOnlyList<CachedScriptableObject> abilityTemplates)
		{
			if (character == null || Equipper == null || abilityTemplates == null || abilityTemplates.Count == 0)
			{
				return;
			}
			Equipper(character, abilityTemplates);
		}
	}
}
