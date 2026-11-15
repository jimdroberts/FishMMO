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
			/* Wildlife is outside the faction system: anyone may harm it and it may harm anyone, except a pack its own
			 * members. Before the companion test: a wild animal has no party or guild. */
			WildlifeRole attackerWild = WildlifeRules.RoleOf(attackerSide);
			WildlifeRole defenderWild = WildlifeRules.RoleOf(defenderSide);
			if (WildlifeRules.Decides(attackerWild, defenderWild))
			{
				return WildlifeRules.MayHarm(attackerWild, WildlifeRules.PackOf(attackerSide), defenderWild, WildlifeRules.PackOf(defenderSide));
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
		/// <see cref="MayHarm"/>, with its consequences for the aggression toggle. The call every hostile route makes once it is
		/// about to apply the effect.
		/// </summary>
		/// <remarks>
		/// Allowed: each side whose aggression is on has its lock restarted (<see cref="FactionController.RefreshAggressionLock"/>)
		/// — the attacker for hitting or killing anything, the defender for being hit by anything. Refused: when only the
		/// attacker's aggression would have let it land and it is the character an attack cast actually struck (not area
		/// splash), it counts toward turning that player's aggression on (<see cref="FactionController.NoteRefusedAttack"/>).
		/// </remarks>
		public static bool Allows(ICharacter attacker, ICharacter defender)
		{
			if (!MayHarm(attacker, defender, out _))
			{
				NoteRefusal(attacker, defender);
				return false;
			}
			if (attacker == null || defender == null || ReferenceEquals(attacker, defender) || attacker.ID == defender.ID)
			{
				return true;
			}
			ICharacter attackerSide = Principal(attacker);
			ICharacter defenderSide = Principal(defender);
			if (ReferenceEquals(attackerSide, defenderSide))
			{
				return true;
			}
			RefreshLockIfAggressive(attackerSide);
			RefreshLockIfAggressive(defenderSide);
			return true;
		}

		private static void RefreshLockIfAggressive(ICharacter side)
		{
			if (side.TryGet(out IFactionController faction) && faction is FactionController controller && controller.AggressionEnabled)
			{
				controller.RefreshAggressionLock();
			}
		}

		/// <summary>The attack cast whose hit is being applied, if any: see <see cref="BeginAttack"/>.</summary>
		public struct AttackScope
		{
			internal bool Active;
			internal long CastKey;
			internal ICharacter Target;
			internal bool Splash;
		}

		private static AttackScope currentAttack;

		/// <summary>
		/// Marks the effects applied until <see cref="EndAttack"/> as one hit of the cast <paramref name="castKey"/> on
		/// <paramref name="target"/>, so a refusal among them can be counted per cast. Returns the scope it replaced, for
		/// <see cref="EndAttack"/> (hits can nest).
		/// </summary>
		/// <param name="castKey">Identifies the cast (its projectiles and pulses share it).</param>
		/// <param name="target">The character the cast struck.</param>
		/// <param name="splash">True for an area blast's victims, which are never counted: a neutral beside the enemy is not a target.</param>
		public static AttackScope BeginAttack(long castKey, ICharacter target, bool splash)
		{
			AttackScope previous = currentAttack;
			currentAttack = new AttackScope { Active = true, CastKey = castKey, Target = target, Splash = splash };
			return previous;
		}

		/// <summary>Ends the scope <see cref="BeginAttack"/> opened, restoring the one it returned.</summary>
		public static void EndAttack(AttackScope previous)
		{
			currentAttack = previous;
		}

		private static void NoteRefusal(ICharacter attacker, ICharacter defender)
		{
			AttackScope attack = currentAttack;
			if (!attack.Active || attack.Splash || attacker == null || defender == null || !ReferenceEquals(defender, attack.Target))
			{
				return;
			}
			ICharacter attackerSide = Principal(attacker);
			ICharacter defenderSide = Principal(defender);
			if (!(attackerSide is IPlayerCharacter) || ReferenceEquals(attackerSide, defenderSide))
			{
				return;
			}
			// Refused for want of aggression only: an arena match, or a companion, refuses whatever the toggle says.
			if (ArenaTeamRegistry.TryResolveAlliance(attackerSide, defenderSide, out _) || SharesSide(attackerSide, defenderSide))
			{
				return;
			}
			if (attackerSide.TryGet(out IFactionController faction) && faction is FactionController controller)
			{
				controller.NoteRefusedAttack(defenderSide, attack.CastKey);
			}
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

	/// <summary>
	/// Counts attack casts refused at one character, toward turning aggression on: the same target, different casts, within
	/// a window. Pure, so it is tested on its own.
	/// </summary>
	public struct AggressionProvocation
	{
		private long targetId;
		private long lastCastKey;
		private int count;
		// The times of the last three different casts, newest first.
		private float t0, t1, t2;
		private bool any;

		/// <summary>
		/// Notes one refused hit. True when it completes <paramref name="needed"/> (at most 3) different casts at the same
		/// target, the oldest of them no more than <paramref name="windowSeconds"/> ago; the count then starts again.
		/// Another target starts the count again too.
		/// </summary>
		public bool Note(long target, long castKey, float now, int needed, float windowSeconds)
		{
			if (any && target == targetId && castKey == lastCastKey)
			{
				// Another projectile or pulse of a cast already counted.
				return false;
			}
			if (!any || target != targetId)
			{
				this = default;
				any = true;
				targetId = target;
			}
			lastCastKey = castKey;
			t2 = t1;
			t1 = t0;
			t0 = now;
			count = count < 3 ? count + 1 : 3;
			int n = needed < 1 ? 1 : needed > 3 ? 3 : needed;
			if (count < n)
			{
				return false;
			}
			float oldest = n == 1 ? t0 : n == 2 ? t1 : t2;
			if (now - oldest > windowSeconds)
			{
				return false;
			}
			this = default;
			return true;
		}
	}
}
