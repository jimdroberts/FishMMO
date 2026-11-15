using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// An ability effect that belongs on a summon spell: it runs when the spell's pet arrives, and
	/// can teach that pet abilities.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The only effect a <see cref="PetAbilityTemplate"/> accepts, and one no other ability does
	/// (<see cref="IsCompatibleWith"/>). Crafted onto a summon, or bundled into one through
	/// <see cref="PetAbilityTemplate.OnSummonEvents"/>, it does two things on the server once the
	/// pet has spawned:
	/// </para>
	/// <list type="bullet">
	/// <item><description><see cref="PetAbilities"/> are taught to the pet, merged with the spell's own
	/// <see cref="PetAbilityTemplate.PetAbilities"/> before the spawn (<see cref="PetAbilityTemplate.BuildPetAbilityIDs"/>).</description></item>
	/// <item><description>The inherited conditions and actions run with a <see cref="PetEventData"/> whose
	/// target is the new pet, so an action with no selector acts on the pet (a buff, a heal) and
	/// <see cref="InitiatorTargetSelector"/> reaches the summoner instead.</description></item>
	/// </list>
	/// <para>
	/// The pet keeps what it was taught until it is dismissed or summoned fresh: the abilities are
	/// saved with the pet, so a pet restored at login still has them, and a new summon rebuilds the
	/// list from the spell as it stands then.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Ability On Summon Event", menuName = "FishMMO/Abilities/Events/Ability On Summon Event", order = 0)]
	public class AbilityOnSummonEvent : AbilityEvent
	{
		/// <summary>
		/// Abilities the summoned pet learns when this effect is part of the summon spell.
		/// </summary>
		[Tooltip("Abilities the summoned pet learns. Only works on summon spells. A summon spell cannot be taught to a pet.")]
		public List<AbilityTemplate> PetAbilities = new List<AbilityTemplate>();

		/// <summary>
		/// Summon spells only: there is no pet for this effect to act on anywhere else.
		/// </summary>
		/// <param name="template">The base ability the effect would be added to.</param>
		/// <returns>True only for a <see cref="PetAbilityTemplate"/>.</returns>
		public override bool IsCompatibleWith(AbilityTemplate template)
		{
			return template is PetAbilityTemplate;
		}

		/// <summary>
		/// Teaching abilities is an effect in its own right; a summon effect needs no actions.
		/// </summary>
		protected override bool HasEffectWithoutActions
		{
			get
			{
				if (PetAbilities == null)
				{
					return false;
				}
				for (int i = 0; i < PetAbilities.Count; ++i)
				{
					if (PetAbilityTemplate.CanBeTaughtToPet(PetAbilities[i]))
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>
		/// One line per ability the pet learns.
		/// </summary>
		/// <param name="into">The list to append to.</param>
		public override void AppendEffectDescriptions(List<string> into)
		{
			PetAbilityTemplate.AppendPetAbilityDescriptions(PetAbilities, into);
		}

		/// <summary>
		/// Describes the effect, and says where it can go: it is refused everywhere but a summon.
		/// </summary>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content)
		{
			base.BuildTooltip(content);
			content.AddBody("Summon spells only.", TooltipPriority.Description + 1, TooltipTone.Muted);
		}

#if UNITY_EDITOR
		/// <summary>
		/// Flags a grant the server would skip: a summon spell cannot be taught to a pet.
		/// </summary>
		protected override void OnValidate()
		{
			base.OnValidate();

			if (PetAbilities == null)
			{
				return;
			}
			for (int i = 0; i < PetAbilities.Count; ++i)
			{
				AbilityTemplate template = PetAbilities[i];
				if (template is PetAbilityTemplate)
				{
					Debug.LogWarning($"[AbilityOnSummonEvent] '{name}': '{template.name}' is a summon spell; pets cannot summon, so it will not be taught.", this);
				}
			}
		}
#endif
	}
}
