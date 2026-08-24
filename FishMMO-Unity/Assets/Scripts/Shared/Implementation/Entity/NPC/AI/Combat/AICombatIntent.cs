namespace FishMMO.Shared
{
	/// <summary>
	/// What an NPC has decided to do about its current target this combat tick.
	/// Produced by <see cref="AICombatDecision.Plan"/> and executed by
	/// <see cref="BaseAttackingState"/>.
	/// </summary>
	/// <remarks>
	/// The intent is deliberately free of Unity types so the decision itself can be
	/// unit-tested without a NavMeshAgent, a NetworkManager, or a scene. Every attacking
	/// state shares this one decision function; archetypes differ only by the tuning values
	/// they feed into it.
	/// </remarks>
	public enum AICombatIntent
	{
		/// <summary>Health has fallen past the personality's threshold — leave the fight entirely.</summary>
		Flee,

		/// <summary>The target is inside the panic radius — break away hard, interrupting any cast.</summary>
		EmergencyRetreat,

		/// <summary>The target is uncomfortably close — back off toward the preferred distance.</summary>
		BackAway,

		/// <summary>An ability is usable and the target is within its range — cast it.</summary>
		Attack,

		/// <summary>Move toward the target until <see cref="AICombatPlan.DesiredDistance"/> is reached.</summary>
		CloseDistance,

		/// <summary>Already at a comfortable distance with nothing to cast — stand and wait.</summary>
		HoldPosition,
	}
}
