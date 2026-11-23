using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Installs the core's <see cref="AbilityGrants"/>: content that names abilities without referencing this
	/// module (a lore object's reward) teaches them here. Skips what the character already knows, learns the rest through
	/// its <see cref="IAbilityKnowledgeController"/>, and tells its client (<see cref="KnownAbilityAddBroadcast"/>,
	/// <see cref="KnownAbilityEventAddBroadcast"/>), as the lore action did itself.
	/// </summary>
	public static class AbilityGrantsInstaller
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
#endif
		private static void Install()
		{
			AbilityGrants.Granter = Grant;
			AbilityGrants.Equipper = Equip;
		}

		/// <summary>Teaches the character what it does not know yet of <paramref name="abilities"/> and <paramref name="abilityEvents"/>.</summary>
		public static void Grant(ICharacter character, IReadOnlyList<CachedScriptableObject> abilities, IReadOnlyList<Trigger> abilityEvents)
		{
			if (character == null || !character.TryGet(out IAbilityKnowledgeController knowledge))
			{
				return;
			}
			NetworkConnection owner = character.Owner;
			bool tell = owner != null && owner.IsActive;

			if (abilities != null && abilities.Count > 0)
			{
				var toLearn = new List<BaseAbilityTemplate>();
				for (int i = 0; i < abilities.Count; i++)
				{
					if (abilities[i] is BaseAbilityTemplate ability && !knowledge.KnowsAbility(ability.ID))
					{
						toLearn.Add(ability);
					}
				}
				if (toLearn.Count > 0)
				{
					knowledge.LearnBaseAbilities(toLearn);
					for (int i = 0; tell && i < toLearn.Count; i++)
					{
						owner.Broadcast(new KnownAbilityAddBroadcast() { TemplateID = toLearn[i].ID });
					}
				}
			}

			if (abilityEvents != null && abilityEvents.Count > 0)
			{
				var toLearn = new List<AbilityEvent>();
				for (int i = 0; i < abilityEvents.Count; i++)
				{
					if (abilityEvents[i] is AbilityEvent abilityEvent && !knowledge.KnowsAbilityEvent(abilityEvent.ID))
					{
						toLearn.Add(abilityEvent);
					}
				}
				if (toLearn.Count > 0)
				{
					knowledge.LearnAbilityEvents(toLearn);
					for (int i = 0; tell && i < toLearn.Count; i++)
					{
						owner.Broadcast(new KnownAbilityEventAddBroadcast() { TemplateID = toLearn[i].ID });
					}
				}
			}
		}

		/// <summary>
		/// Gives the character a usable ability per template (an NPC's authored kit), keyed by the template's ID: NPCs do
		/// not craft, so there is no database ID. What the AI's ability picker chooses from.
		/// </summary>
		public static void Equip(ICharacter character, IReadOnlyList<CachedScriptableObject> abilityTemplates)
		{
			if (character == null || abilityTemplates == null || !character.TryGet(out IAbilityKnowledgeController abilityController))
			{
				return;
			}
			for (int i = 0; i < abilityTemplates.Count; i++)
			{
				if (!(abilityTemplates[i] is AbilityTemplate template))
				{
					continue;
				}
				abilityController.LearnAbility(new Ability((long)template.ID, template));

				/* What an NPC actually walks away knowing. An NPC that learns nothing here cannot attack whatever its
				 * archetype or rotation says, and that failure is otherwise invisible: the mob still aggroes, chases and
				 * repositions, it simply never swings. */
				FishMMO.Logging.Log.Debug("NPC", $"'{character.Name}' learned ability '{template.name}' (ID {template.ID}).");
			}
		}
	}
}
