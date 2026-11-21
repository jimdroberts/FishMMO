using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks the alliance relationship between the initiator and the target (defender).
	/// Allows configuration for which alliance types (self, enemy, neutral, ally) the condition applies to.
	/// </summary>
	[Serializable]
	public sealed class TargetAllianceCondition : BaseCondition
	{
		/// <summary>
		/// Whether the condition applies when the initiator targets themselves.
		/// </summary>
		public bool ApplyToSelf;

		/// <summary>
		/// Whether the condition applies to enemies (default: true).
		/// </summary>
		public bool ApplyToEnemy = true;

		/// <summary>
		/// Whether the condition applies to neutral targets.
		/// </summary>
		public bool ApplyToNeutral;

		/// <summary>
		/// Whether the condition applies to allies.
		/// </summary>
		public bool ApplyToAllies;

		/// <summary>
		/// With <see cref="ApplyToNeutral"/>: a neutral target passes only if it is a player character. Wildlife, neutral NPCs
		/// and pets read as neutral too, and a heal meant for a stranger must not mend a wolf mid-fight.
		/// </summary>
		[Tooltip("With Apply To Neutral: only neutral PLAYERS pass (not wildlife, neutral NPCs or pets).")]
		public bool NeutralPlayersOnly;

		/// <summary>
		/// With <see cref="ApplyToNeutral"/>: a neutral target passes unless it is wildlife or someone else's pet (2026-11-21,
		/// heals reach neutral NPCs such as townsfolk and guards, still never a wolf mid-fight or a stranger's companion). Your
		/// own pets, and your party's and guild's, read as allies and are not affected.
		/// </summary>
		[Tooltip("With Apply To Neutral: neutral wildlife and other people's pets do not pass (neutral players and NPCs do).")]
		public bool NeutralExcludesWildlifeAndPets;

		/// <summary>
		/// A pet passes only for its own side: its owner, or the owner's other pets (Jim, 2026-11-21: "players that summon pets
		/// should be required to handle their own pet maintenance"). Applies at every standing, so a party or guild member's pet,
		/// an ally, is not healed by anyone but its owner either.
		/// </summary>
		[Tooltip("A pet passes only when it is the initiator's own (or the initiator's owner's): nobody else maintains a pet.")]
		public bool ExcludeOthersPets;

		/// <summary>The pet rule as a pure function: whether a target that is someone else's pet is let through.</summary>
		public bool PassesPetRule(bool defenderIsOthersPet) => !ExcludeOthersPets || !defenderIsOthersPet;

		/// <summary>
		/// The rule for a target other than the initiator, as a pure function of how the two stand and whether the target is a
		/// player character.
		/// </summary>
		public bool Passes(FactionAllianceLevel allianceLevel, bool defenderIsPlayer)
		{
			return Passes(allianceLevel, allianceLevel, defenderIsPlayer, false);
		}

		/// <summary>
		/// The rule with the aggression toggle taken into account: hostile reach (<see cref="ApplyToEnemy"/>) reads the level the
		/// toggle widens (<paramref name="allianceLevel"/>, a neutral reads as Enemy while either side's aggression is on), and
		/// the neutral and ally reach reads the standing without it (<paramref name="standing"/>).
		/// </summary>
		/// <remarks>
		/// With a single level, turning aggression on made every neutral player an Enemy to a heal or cleanse that reaches
		/// neutral players, so Lesser Heal and Dispel Magic stopped landing on them. Damage that reaches enemies and
		/// neutrals passes either way; damage that reaches enemies only still widens to neutrals with the toggle.
		/// </remarks>
		public bool Passes(FactionAllianceLevel allianceLevel, FactionAllianceLevel standing, bool defenderIsPlayer)
		{
			return Passes(allianceLevel, standing, defenderIsPlayer, false);
		}

		/// <summary>
		/// The rule with the target's kind: <paramref name="defenderIsWildlifeOrPet"/> is wildlife, or a companion whose owner
		/// is someone else (see <see cref="NeutralExcludesWildlifeAndPets"/>).
		/// </summary>
		public bool Passes(FactionAllianceLevel allianceLevel, FactionAllianceLevel standing, bool defenderIsPlayer, bool defenderIsWildlifeOrPet)
		{
			return (allianceLevel == FactionAllianceLevel.Enemy && ApplyToEnemy) ||
				   (standing == FactionAllianceLevel.Neutral && ApplyToNeutral &&
					(!NeutralPlayersOnly || defenderIsPlayer) &&
					(!NeutralExcludesWildlifeAndPets || !defenderIsWildlifeOrPet)) ||
				   (standing == FactionAllianceLevel.Ally && ApplyToAllies);
		}

		/// <summary>
		/// Evaluates whether the condition applies based on the alliance relationship between initiator and target.
		/// </summary>
		/// <param name="initiator">The character initiating the action or condition.</param>
		/// <param name="eventData">Event data containing the target character.</param>
		/// <returns>True if the alliance relationship matches the allowed types; otherwise, false.</returns>
		/// <remarks>
		/// This method checks the target's alliance level (enemy, neutral, ally, or self) and returns true if the corresponding flag is enabled.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter defender = (eventData?.TargetCharacter ?? initiator);

			if (initiator == null)
			{
				return false; // Initiator or their faction controller is missing
			}

			if (defender == null)
			{
				// If no defender, this condition typically wouldn't pass for character-specific effects
				return false;
			}

			if (!initiator.TryGet(out IFactionController attackerFactionController))
			{
				return false; // Initiator must have a faction controller
			}

			// If targeting self, use the ApplyToSelf flag
			if (defender.ID == initiator.ID)
			{
				return ApplyToSelf;
			}
			// Someone else's pet, at any standing (a pet's side is its owner's).
			ICharacter defenderSide = HarmRule.Principal(defender);
			bool othersPet = !ReferenceEquals(defenderSide, defender) && !ReferenceEquals(defenderSide, HarmRule.Principal(initiator));
			if (!PassesPetRule(othersPet))
			{
				return false;
			}
			// Otherwise, check the alliance level between initiator and defender
			if (defender.TryGet(out IFactionController defenderFactionController))
			{
				FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defenderFactionController);
				FactionAllianceLevel standing = attackerFactionController.GetStandingAllianceLevel(defenderFactionController);

				bool wildlifeOrPet = WildlifeRules.RoleOf(defender) != WildlifeRole.None ||
					!ReferenceEquals(HarmRule.Principal(defender), defender);
				return Passes(allianceLevel, standing, defender is IPlayerCharacter, wildlifeOrPet);
			}

			// Not able to resolve target or other conditions not met
			return false;
		}

	}
}