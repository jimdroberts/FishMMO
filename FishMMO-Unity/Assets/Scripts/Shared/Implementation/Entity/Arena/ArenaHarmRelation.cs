using FishMMO.Shared.Core;

[assembly: FishMMO.Shared.HarmRelation(typeof(FishMMO.Shared.ArenaHarmRelation))]

namespace FishMMO.Shared
{
	/// <summary>
	/// Inside an arena the match's sides decide alone: only an opponent, whatever anyone's toggle, party or guild says.
	/// Asked first.
	/// </summary>
	public sealed class ArenaHarmRelation : IHarmRelation
	{
		public const int RelationOrder = 100;

		public int Order => RelationOrder;

		public HarmJudgement Judge(ICharacter attackerSide, ICharacter defenderSide)
		{
			if (ArenaTeamRegistry.TryResolveAlliance(attackerSide, defenderSide, out FactionAllianceLevel arenaLevel))
			{
				return arenaLevel == FactionAllianceLevel.Enemy ? HarmJudgement.Allow : HarmJudgement.Refuse;
			}
			return HarmJudgement.Undecided;
		}

		public void OnAllowed(ICharacter attackerSide, ICharacter defenderSide) { }

		public void OnRefusedAttack(ICharacter attackerSide, ICharacter defenderSide, long castKey, IHarmRelation refusedBy) { }
	}
}
