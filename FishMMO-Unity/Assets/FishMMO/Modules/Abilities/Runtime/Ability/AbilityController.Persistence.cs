using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>The controller's side of the core character load and save (<see cref="IAbilityPersistence"/>, 2026-11-20).</summary>
	public partial class AbilityController : IAbilityPersistence
	{
		/// <inheritdoc />
		public void RestoreAbility(long abilityID, int templateID, List<int> abilityEventIDs, long version, float remainingCooldown)
		{
			Ability ability = new Ability(abilityID, templateID, abilityEventIDs);
			ability.Version = version;
			LearnAbility(ability, remainingCooldown);
		}

		/// <inheritdoc />
		public void RestoreKnowledge(IReadOnlyList<int> templateIDs, List<int> unknown)
		{
			var baseAbilities = new List<BaseAbilityTemplate>();
			var abilityEvents = new List<AbilityEvent>();
			if (templateIDs != null)
			{
				foreach (int templateID in templateIDs)
				{
					/* Base templates first: AbilityTypeOverrideEventType extends BaseAbilityTemplate despite being an
					 * "event", and the event cache would come back empty for it. */
					BaseAbilityTemplate template = BaseAbilityTemplate.Get<BaseAbilityTemplate>(templateID);
					if (template != null)
					{
						baseAbilities.Add(template);
						continue;
					}
					AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(templateID);
					if (abilityEvent != null)
					{
						abilityEvents.Add(abilityEvent);
						continue;
					}
					unknown?.Add(templateID);
				}
			}
			if (baseAbilities.Count > 0)
			{
				LearnBaseAbilities(baseAbilities);
			}
			if (abilityEvents.Count > 0)
			{
				LearnAbilityEvents(abilityEvents);
			}
			// Restored, not learned: the database already holds every row just applied.
			KnowledgeDirty = false;
		}

		/// <inheritdoc />
		public void CaptureDirtyAbilities(Action<long, long, int, List<int>> emit)
		{
			foreach (KeyValuePair<long, Ability> kvp in KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null || !ability.PersistenceDirty)
				{
					continue;
				}
				List<int> eventIds = ability.AbilityEvents.Keys.ToList();
				ability.Version++;
				emit(ability.ID, ability.Version, ability.Template.ID, eventIds);
			}
		}

		/// <inheritdoc />
		public void MarkAbilityPersisted(long abilityID, long version)
		{
			if (KnownAbilities.TryGetValue(abilityID, out Ability ability) && ability != null)
			{
				ability.MarkPersisted(version);
			}
		}
	}
}
