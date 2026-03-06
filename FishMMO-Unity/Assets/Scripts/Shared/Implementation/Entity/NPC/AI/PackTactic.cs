namespace FishMMO.Shared
{
	/// <summary>
	/// Describes how an <see cref="NPCGroup"/> coordinates spatial positioning
	/// during combat. The tactic determines how each member's
	/// <see cref="AIController.OrbitAngle"/> is assigned relative to the
	/// group target.
	/// </summary>
	public enum PackTactic
	{
		/// <summary>No coordinated positioning — members act independently.</summary>
		None = 0,

		/// <summary>
		/// Members spread evenly around the target in a ring.
		/// Orbit angles are distributed 360° / alive-member-count apart.
		/// Best for mixed groups that want to prevent the enemy from fleeing.
		/// </summary>
		Surround,

		/// <summary>
		/// Tank holds the front, other members position behind the target.
		/// DPS and support members receive orbit angles in the rear 180° arc
		/// while the tank faces the target head-on.
		/// </summary>
		Flank,

		/// <summary>
		/// All members converge on the same target from the same direction.
		/// Orbit angles are tightly clustered. Combined with
		/// <see cref="NPCGroup.FocusTargeting"/> for maximum single-target pressure.
		/// </summary>
		FocusFire,

		/// <summary>
		/// Members maintain maximum distance and orbit the target.
		/// Orbit angles rotate slowly each evaluation, creating a swirling pattern.
		/// Best for ranged/caster groups that want to avoid melee contact.
		/// </summary>
		Kite,
	}
}