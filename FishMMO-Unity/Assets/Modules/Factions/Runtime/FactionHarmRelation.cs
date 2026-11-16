using FishMMO.Shared.Core;

[assembly: FishMMO.Shared.HarmRelation(typeof(FishMMO.Shared.FactionHarmRelation))]

namespace FishMMO.Shared
{
	/// <summary>
	/// Faction standing and the aggression toggles: the factions' say in <see cref="HarmRule"/>, asked after the arena,
	/// wildlife and companions.
	/// </summary>
	/// <remarks>
	/// <list type="number">
	/// <item>An enemy may always be harmed (<see cref="IFactionController.GetAllianceLevel"/>: hostile standing, or an
	/// aggressive NPC on either side).</item>
	/// <item>A neutral or an ally only while the defender's aggression toggle is on (how a struck neutral fights back), or
	/// the attacker's (the attacker is then the aggressor).</item>
	/// </list>
	/// A side with no faction controller has no say here. An allowed hit restarts the lock of each side whose aggression is
	/// on (<see cref="FactionController.RefreshAggressionLock"/>): the attacker for hitting or killing anything, the
	/// defender for being hit by anything. A player's attack cast refused here (for want of aggression only: arena and
	/// companion refusals are other relations) counts toward turning their aggression on
	/// (<see cref="FactionController.NoteRefusedAttack"/>).
	/// </remarks>
	public sealed class FactionHarmRelation : IHarmRelation
	{
		public const int RelationOrder = 400;

		public int Order => RelationOrder;

		public HarmJudgement Judge(ICharacter attackerSide, ICharacter defenderSide)
		{
			if (!attackerSide.TryGet(out IFactionController attackerFaction) || !defenderSide.TryGet(out IFactionController defenderFaction))
			{
				return HarmJudgement.Undecided;
			}
			if (attackerFaction.GetAllianceLevel(defenderFaction) == FactionAllianceLevel.Enemy)
			{
				return HarmJudgement.Allow;
			}
			if (defenderFaction.AggressionEnabled)
			{
				return HarmJudgement.Allow;
			}
			if (attackerFaction.AggressionEnabled)
			{
				return HarmJudgement.AllowAsAggressor;
			}
			return HarmJudgement.Refuse;
		}

		public void OnAllowed(ICharacter attackerSide, ICharacter defenderSide)
		{
			RefreshLockIfAggressive(attackerSide);
			RefreshLockIfAggressive(defenderSide);
		}

		private static void RefreshLockIfAggressive(ICharacter side)
		{
			if (side.TryGet(out IFactionController faction) && faction is FactionController controller && controller.AggressionEnabled)
			{
				controller.RefreshAggressionLock();
			}
		}

		public void OnRefusedAttack(ICharacter attackerSide, ICharacter defenderSide, long castKey, IHarmRelation refusedBy)
		{
			// Refused for want of aggression only: an arena match, or a companion, refuses whatever the toggle says.
			if (!ReferenceEquals(refusedBy, this) || !(attackerSide is IPlayerCharacter))
			{
				return;
			}
			if (attackerSide.TryGet(out IFactionController faction) && faction is FactionController controller)
			{
				controller.NoteRefusedAttack(defenderSide, castKey);
			}
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
