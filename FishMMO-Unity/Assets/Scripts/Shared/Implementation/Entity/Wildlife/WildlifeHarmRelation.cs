using FishMMO.Shared.Core;

[assembly: FishMMO.Shared.HarmRelation(typeof(FishMMO.Shared.WildlifeHarmRelation))]

namespace FishMMO.Shared
{
	/// <summary>
	/// Wildlife is outside the faction system: anyone may harm it and it may harm anyone, except a pack its own members.
	/// Asked before companions: a wild animal has no party or guild.
	/// </summary>
	public sealed class WildlifeHarmRelation : IHarmRelation
	{
		public const int RelationOrder = 200;

		public int Order => RelationOrder;

		public HarmJudgement Judge(ICharacter attackerSide, ICharacter defenderSide)
		{
			WildlifeRole attackerWild = WildlifeRules.RoleOf(attackerSide);
			WildlifeRole defenderWild = WildlifeRules.RoleOf(defenderSide);
			if (!WildlifeRules.Decides(attackerWild, defenderWild))
			{
				return HarmJudgement.Undecided;
			}
			return WildlifeRules.MayHarm(attackerWild, WildlifeRules.PackOf(attackerSide), defenderWild, WildlifeRules.PackOf(defenderSide))
				? HarmJudgement.Allow : HarmJudgement.Refuse;
		}

		public void OnAllowed(ICharacter attackerSide, ICharacter defenderSide) { }

		public void OnRefusedAttack(ICharacter attackerSide, ICharacter defenderSide, long castKey, IHarmRelation refusedBy) { }
	}
}
