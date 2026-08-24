namespace FishMMO.Shared
{
	/// <summary>
	/// How an NPC picks which enemy to hit when several are available.
	/// Supplied by <see cref="AICombatPersonality.TargetingMode"/> and consumed by
	/// <see cref="BaseAttackingState.PickTarget"/>.
	/// </summary>
	public enum AITargetingMode
	{
		/// <summary>
		/// Default MMO behaviour: whoever has generated the most threat, with a small chance of
		/// a secondary pick for variety. Uses the aggression table.
		/// </summary>
		Threat = 0,

		/// <summary>
		/// Ignores the threat table and picks a random living candidate, re-rolling periodically.
		/// This is what makes a "rampaging" enemy feel uncontrollable — taunts and threat do not
		/// hold it, and it will wander off mid-fight onto someone else.
		/// </summary>
		Random,

		/// <summary>Picks the candidate with the lowest health fraction — an executioner.</summary>
		Weakest,

		/// <summary>Picks the physically closest candidate. Simple, predictable, good for critters.</summary>
		Nearest,
	}
}
