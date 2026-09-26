using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Single definition of "this character is evading and accepts nothing hostile".
	/// </summary>
	/// <remarks>
	/// <para>
	/// An NPC whose leash ended a fight walks home evading (<see cref="INPCBrain.IsEvading"/>). The
	/// evade exists to stop a leash being farmed: without it a player could drag a mob to the edge
	/// of its range and hit it all the way home, or land one blow on the way to reset the fight on
	/// their own terms. That is only closed if EVERY hostile route is closed, and they are applied
	/// in three different places — damage in <c>CharacterDamageController.Damage</c>, debuffs in
	/// <c>ApplyBuffAction</c>, displacement in <c>KnockbackHitAction</c> — so the question lives
	/// here rather than being written out three times and drifting. Threat and taunt are refused by
	/// the brain itself, which owns the threat table.
	/// </para>
	/// <para>
	/// <b>Server-side truth only.</b> The brain exists only on the scene server, so on every client
	/// this answers false. A client that predicts a hostile effect on an evading NPC is corrected
	/// the way any refused prediction is: the damage report says <c>Evade</c>, the provisional
	/// debuff is dropped by the next full buff set, and knockback never predicted a displacement.
	/// </para>
	/// <para>
	/// Separate from <see cref="ICharacterDamageController.Immortal"/> on purpose, and it does not
	/// change what Immortal means: an immortal character still takes buffs and debuffs as it always
	/// has. See <see cref="INPCBrain.IsEvading"/> for why the evade is not built on Immortal.
	/// </para>
	/// </remarks>
	public static class CharacterEvade
	{
		/// <summary>
		/// True while <paramref name="character"/> refuses every hostile effect: an NPC its server
		/// brain has evading.
		/// </summary>
		/// <remarks>
		/// The NPC type test comes first so a hit on a player never pays the behaviour lookup; this
		/// is asked on every hit, every debuff and every knockback.
		/// </remarks>
		/// <param name="character">The character something hostile is about to land on.</param>
		/// <returns>True when the effect must be refused.</returns>
		public static bool RefusesHostileEffects(ICharacter character)
		{
			return character is NPC &&
				character.TryGet(out INPCBrain brain) &&
				brain.IsEvading;
		}

		/// <summary>
		/// Whether a buff is a hostile effect: a debuff put on its target by some other character.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Every debuff, not a list of crowd-control shapes.</b> A stun, a freeze (root) and a
		/// mesmerize are state flags, but a slow is nothing more than a debuff lowering the Move
		/// Speed attribute, which in the buff model is indistinguishable from one lowering armour;
		/// there is no silence mechanic, and "similar" has no field to test. A narrower rule would
		/// either miss slows or name one attribute asset. It would also leave the part of the
		/// exploit a debuff opens on its own: a damage-over-time debuff put on during the walk home
		/// has every tick refused while the evade lasts and then ticks on, from full health, the
		/// moment the NPC arrives — pulling it straight back to the player who applied it.
		/// </para>
		/// <para>
		/// <b>Only from another character.</b> A buff an NPC puts on itself is its own business, and
		/// environmental debuffs (weather exposure, buff volumes) never reach this rule: they are
		/// not attacks, and they are applied by their own systems with no caster.
		/// </para>
		/// <para>Pure, so the rule is a truth table.</para>
		/// </remarks>
		/// <param name="isDebuff">True when the template is a debuff (<see cref="BaseBuffTemplate.IsDebuff"/>).</param>
		/// <param name="fromAnotherCharacter">True when a character other than the target is applying it.</param>
		/// <returns>True for a hostile buff.</returns>
		public static bool IsHostileBuff(bool isDebuff, bool fromAnotherCharacter)
		{
			return isDebuff && fromAnotherCharacter;
		}

		/// <summary>
		/// True when <paramref name="target"/> must refuse <paramref name="template"/> from
		/// <paramref name="caster"/>: a hostile buff (<see cref="IsHostileBuff"/>) landing on a
		/// character that refuses hostile effects (<see cref="RefusesHostileEffects"/>).
		/// </summary>
		/// <param name="target">The character the buff would land on.</param>
		/// <param name="template">The buff being applied.</param>
		/// <param name="caster">The character applying it; null for a source with none.</param>
		/// <returns>True when the application must be refused.</returns>
		public static bool RefusesBuff(ICharacter target, BaseBuffTemplate template, ICharacter caster)
		{
			if (target == null || template == null)
			{
				return false;
			}

			bool fromAnotherCharacter = caster != null && !ReferenceEquals(caster, target);

			// The cheap template test first: almost nothing applied is a debuff on an evading NPC.
			return IsHostileBuff(template.IsDebuff, fromAnotherCharacter) &&
				RefusesHostileEffects(target);
		}
	}
}
