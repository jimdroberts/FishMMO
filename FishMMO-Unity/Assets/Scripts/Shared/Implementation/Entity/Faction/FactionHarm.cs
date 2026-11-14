using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Who may harm whom: the one rule every hostile effect answers to, so an ability needs no authored condition to leave
	/// friends alone. Asked by damage (<see cref="CharacterDamageController.Damage"/>) and, through
	/// <see cref="CharacterEvade"/>, by debuffs, knockback, interrupts, taunts, buff-stripping dispels and resource drains.
	/// </summary>
	/// <remarks>
	/// <list type="number">
	/// <item>A pet answers as its owner, on either side: it harms whom its owner may, and may be harmed by whoever may harm
	/// its owner.</item>
	/// <item>Inside an arena, the match's sides decide alone: only an opponent, whatever anyone's toggle says.</item>
	/// <item>Otherwise an enemy may always be harmed (<see cref="IFactionController.GetAllianceLevel"/>: hostile standing,
	/// or an aggressive NPC on either side).</item>
	/// <item>Never a companion (<see cref="AreCompanions"/>: the same party, the same guild, or one's own pet), whatever
	/// either toggle says: aggression never turns into friendly fire.</item>
	/// <item>A neutral or an ally only while the attacker's aggression toggle is on, or the defender's: a player who turned
	/// it on may be struck back by anyone outside their party and guild, which is how a neutral creature it attacked fights
	/// back.</item>
	/// </list>
	/// No attacker (the world: a fall, lava), the character harming itself, or a side with no faction controller is not
	/// restricted.
	/// </remarks>
	public static class FactionHarm
	{
		/// <summary>
		/// Whether <paramref name="attacker"/> may harm <paramref name="defender"/>. <paramref name="aggressor"/> is the
		/// character (a pet's owner, for a pet) whose aggression toggle alone allowed it, else null: a hit that locks that
		/// toggle on for a while.
		/// </summary>
		public static bool MayHarm(ICharacter attacker, ICharacter defender, out ICharacter aggressor)
		{
			aggressor = null;
			if (attacker == null || defender == null || ReferenceEquals(attacker, defender) || attacker.ID == defender.ID)
			{
				return true;
			}
			// A pet is its owner's hand: its relations, its toggle, its arena team.
			ICharacter attackerSide = Principal(attacker);
			ICharacter defenderSide = Principal(defender);
			if (ArenaTeamRegistry.TryResolveAlliance(attackerSide, defenderSide, out FactionAllianceLevel arenaLevel))
			{
				return arenaLevel == FactionAllianceLevel.Enemy;
			}
			// Party, guild, and an owner with its own pet: never, toggle or not.
			if (SharesSide(attackerSide, defenderSide))
			{
				return false;
			}
			if (!attackerSide.TryGet(out IFactionController attackerFaction) || !defenderSide.TryGet(out IFactionController defenderFaction))
			{
				return true;
			}
			if (attackerFaction.GetAllianceLevel(defenderFaction) == FactionAllianceLevel.Enemy)
			{
				return true;
			}
			if (defenderFaction.AggressionEnabled)
			{
				return true;
			}
			if (attackerFaction.AggressionEnabled)
			{
				aggressor = attackerSide;
				return true;
			}
			return false;
		}

		/// <summary>
		/// <see cref="MayHarm"/>, recording a hit only the attacker's toggle allowed (so the toggle stays locked on for a
		/// while). The call every hostile route makes once it is about to apply the effect.
		/// </summary>
		public static bool Allows(ICharacter attacker, ICharacter defender)
		{
			if (!MayHarm(attacker, defender, out ICharacter aggressor))
			{
				return false;
			}
			if (aggressor != null && aggressor.TryGet(out IFactionController faction) && faction is FactionController controller)
			{
				controller.NoteAggressiveHarm();
			}
			return true;
		}

		/// <summary>
		/// Whether <paramref name="a"/> and <paramref name="b"/> are on one side outside an arena: the same party, the same
		/// guild, or a pet and its owner (or two pets of one owner). Companions are never harmed, and never drawn hostile,
		/// whatever their aggression toggles say. Inside an arena the match's teams decide instead (<see cref="MayHarm"/>).
		/// </summary>
		public static bool AreCompanions(ICharacter a, ICharacter b)
		{
			if (a == null || b == null)
			{
				return false;
			}
			return SharesSide(Principal(a), Principal(b));
		}

		private static bool SharesSide(ICharacter a, ICharacter b)
		{
			if (ReferenceEquals(a, b))
			{
				return true;
			}
			if (a.TryGet(out IPartyController party) && b.TryGet(out IPartyController otherParty) &&
				party.ID != 0 && party.ID == otherParty.ID)
			{
				return true;
			}
			return a.TryGet(out IGuildController guild) && b.TryGet(out IGuildController otherGuild) &&
				guild.ID != 0 && guild.ID == otherGuild.ID;
		}

		/// <summary>The character whose relations <paramref name="character"/> acts under: a pet's owner, else itself.</summary>
		public static ICharacter Principal(ICharacter character)
		{
			return character is Pet pet && pet.PetOwner != null ? pet.PetOwner : character;
		}
	}
}
