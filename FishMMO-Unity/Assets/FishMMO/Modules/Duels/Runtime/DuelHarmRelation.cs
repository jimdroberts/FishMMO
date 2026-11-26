using FishMMO.Shared.Core;

[assembly: FishMMO.Shared.HarmRelation(typeof(FishMMO.Shared.DuelHarmRelation))]

namespace FishMMO.Shared
{
	/// <summary>
	/// A duel's two sides (each player and their pets) may harm each other while the duel is fought, whatever their
	/// party, guild, faction or toggles say, and not before: during the countdown neither may strike the other. A blow
	/// that would kill leaves the loser on their last point instead, and ends the duel (<see cref="DuelRegistry"/>).
	/// Asked after the arena (duels are not fought in arenas) and before wildlife, companions and factions.
	/// </summary>
	public sealed class DuelHarmRelation : IHarmRelation
	{
		/// <summary>Between the arena (100) and wildlife (200).</summary>
		public const int RelationOrder = 150;

		/// <inheritdoc />
		public int Order => RelationOrder;

		/// <inheritdoc />
		public HarmJudgement Judge(ICharacter attacker, ICharacter defender)
		{
			if (!TryPair(attacker, defender, out bool live))
			{
				return HarmJudgement.Undecided;
			}
			return live ? HarmJudgement.Allow : HarmJudgement.Refuse;
		}

		/// <inheritdoc />
		public bool Spares(ICharacter attacker, ICharacter defender)
		{
			return TryPair(attacker, defender, out bool live) && live;
		}

		/// <inheritdoc />
		public void OnSpared(ICharacter attacker, ICharacter defender)
		{
			DuelRegistry.RaiseFinishingBlow(attacker.ID, defender.ID);
		}

		/// <inheritdoc />
		public bool TryResolveAlliance(ICharacter a, ICharacter b, out FactionAllianceLevel level)
		{
			if (!TryPair(a, b, out bool live))
			{
				level = FactionAllianceLevel.Neutral;
				return false;
			}
			// Counting down they are no one's enemy, so nothing reads them as a target before the start.
			level = live ? FactionAllianceLevel.Enemy : FactionAllianceLevel.Neutral;
			return true;
		}

		/// <inheritdoc />
		public void OnAllowed(ICharacter attacker, ICharacter defender) { }

		/// <inheritdoc />
		public void OnRefusedAttack(ICharacter attacker, ICharacter defender, long castKey, IHarmRelation refusedBy) { }

		/// <summary>
		/// Whether two principals are a duel's sides. Players only: an NPC's id is not a character id, so an NPC that
		/// happened to share one must never be taken for a duellist.
		/// </summary>
		private static bool TryPair(ICharacter a, ICharacter b, out bool live)
		{
			live = false;
			if (DuelRegistry.Count == 0 || !(a is IPlayerCharacter) || !(b is IPlayerCharacter))
			{
				return false;
			}
			return DuelRegistry.ArePaired(a.ID, b.ID, out live);
		}
	}
}
