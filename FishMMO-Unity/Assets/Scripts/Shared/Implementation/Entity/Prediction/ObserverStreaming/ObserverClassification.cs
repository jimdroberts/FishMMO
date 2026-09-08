namespace FishMMO.Shared
{
	/// <summary>
	/// What kind of thing an observed object is, for the purposes of interest management.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each value gets its own visibility range and its own per-viewer budget, authored on a
	/// <see cref="ClassifiedDistanceCondition"/> asset under <c>Assets/Settings/ObserverConditions</c>.
	/// The classifications exist because the costs are not comparable: a player is a moving,
	/// predicted, chattering entity; a dropped sword is a spawn payload that never speaks again; a
	/// Titan is one object that the whole zone is supposed to be able to see.
	/// </para>
	/// <para>
	/// Budgets are per classification precisely so a crowd of one kind cannot squeeze out another.
	/// Under a single shared budget, forty players in a town square evicted the banker they were
	/// queueing for, and a field of dropped loot could have cost you the monster you were fighting.
	/// </para>
	/// </remarks>
	public enum ObserverClassification : byte
	{
		/// <summary>A player character.</summary>
		Player = 0,

		/// <summary>A hostile or neutral mobile NPC, including pets.</summary>
		Monster = 1,

		/// <summary>A stationary service NPC: banker, merchant, ability crafter.</summary>
		Interactable = 2,

		/// <summary>A dropped item lying in the world.</summary>
		WorldItem = 3,

		/// <summary>A fast-travel node.</summary>
		Waypoint = 4,

		/// <summary>
		/// A god-tier entity that walks the world and should be visible across it.
		/// </summary>
		/// <remarks>
		/// Titans are wired differently from everything else: their prefabs use
		/// <c>ConditionOverrideType.IgnoreManager</c> so they never take the <c>GridCondition</c>,
		/// whose cells would otherwise clip them to a fraction of their range. See
		/// <see cref="ClassifiedDistanceCondition"/>.
		/// </remarks>
		Titan = 5,
	}
}
