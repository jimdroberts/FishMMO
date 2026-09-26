namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Roles that NPCs can play within an <see cref="NPCGroup"/>, authored per spawner entry
	/// (<c>NPCSpawnableSettings.PackRole</c>).
	/// </summary>
	/// <remarks>
	/// Read by <see cref="BaseAttackingState.PickTarget"/> (a tank picks by threat, DPS and Support
	/// take the pack's focus), by <see cref="NPCGroup"/> (a tank's target becomes the focus, and a
	/// Flank puts tanks at the front). A healer's behaviour comes from its archetype
	/// (<see cref="HealerAttackingState"/>), not from this role.
	/// </remarks>
	public enum NPCGroupRole
	{
		/// <summary>No specific role: picks its own targets, but still answers alerts and takes a tactic slot.</summary>
		None,
		/// <summary>Tank — picks by threat; its target is the pack's focus; holds a Flank's front.</summary>
		Tank,
		/// <summary>Healer — no targeting rule of its own; heals through a healer archetype.</summary>
		Healer,
		/// <summary>DPS — takes the pack's focus when choosing a target.</summary>
		DPS,
		/// <summary>Support — takes the pack's focus when choosing a target.</summary>
		Support
	}
}
