using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA condition: the character knows an ability — the base template itself (learned from a
	/// scroll or a trainer), or a crafted ability built on it.
	/// </summary>
	/// <remarks>
	/// Written for portal activation (a portal any mage who knows Blink can open), and general
	/// enough for dialogue and quests. Answers on every peer that holds the knowledge record: the
	/// server, and the owner client.
	/// </remarks>
	[Serializable]
	public class KnowsAbilityCondition : BaseCondition
	{
		[Tooltip("The ability the character must know.")]
		public BaseAbilityTemplate Ability;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			ICharacter characterToCheck = eventData?.TargetCharacter ?? initiator;
			if (Ability == null || characterToCheck == null ||
				!characterToCheck.TryGet(out IAbilityKnowledgeController knowledge))
			{
				return false;
			}
			return knowledge.KnowsAbility(Ability.ID) || knowledge.KnowsLearnedAbility(Ability.ID);
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			return Ability != null ? $"Requires knowing {Ability.Name}" : null;
		}
	}
}
