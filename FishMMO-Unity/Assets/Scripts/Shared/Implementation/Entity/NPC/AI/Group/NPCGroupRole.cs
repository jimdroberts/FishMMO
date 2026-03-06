namespace FishMMO.Shared
{
	/// <summary>
	/// Roles that NPCs can play within an <see cref="NPCGroup"/>.
	/// The group brain uses these roles to coordinate tactics —
	/// e.g., the tank holds aggro, the healer prioritizes healing,
	/// and DPS focus the group's target.
	/// </summary>
	public enum NPCGroupRole
	{
		/// <summary>No specific role. Behaves independently.</summary>
		None,
		/// <summary>Tank — taunts and holds threat.</summary>
		Tank,
		/// <summary>Healer — prioritizes healing group members.</summary>
		Healer,
		/// <summary>DPS — focuses fire on the group's target.</summary>
		DPS,
		/// <summary>Support — buffs allies, debuffs enemies.</summary>
		Support
	}
}