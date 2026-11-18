namespace FishMMO.Shared.Core
{
	/// <summary>
	/// How a character earned a share of another character's death.
	/// </summary>
	/// <remarks>
	/// Recorded rather than merely counted so a future loot policy can weight the kinds
	/// differently — the current policy treats all three as equal grounds for loot rights.
	/// </remarks>
	public enum CombatContributionKind : byte
	{
		/// <summary>Dealt damage directly to the victim.</summary>
		Damage = 0,
		/// <summary>Healed someone who was themselves contributing to the victim's death.</summary>
		Healing = 1,
		/// <summary>Applied a debuff to the victim.</summary>
		Debuff = 2,
	}
}
