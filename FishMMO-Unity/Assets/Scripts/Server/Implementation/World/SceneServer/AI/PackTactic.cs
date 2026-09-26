namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// How an <see cref="NPCGroup"/> arranges the members fighting its focus. Each such member is
	/// given a bearing around the focus (<see cref="AIController.PackSlotAngle"/>), which
	/// <see cref="OrbitState"/> steers to on the pack's <see cref="NPCGroup.TacticOrbitRadius"/>.
	/// </summary>
	/// <remarks>
	/// Bearings are fitted to where the members already stand (see
	/// <see cref="NPCGroup.AssignFormation"/>), not measured from the world axes. A tactic shapes the
	/// orbit manoeuvre only: an archetype whose attacking state has no Orbit variety state never
	/// orbits, and its approach is spaced by the combat-slot ring as any attacker's is.
	/// </remarks>
	public enum PackTactic
	{
		/// <summary>No coordinated positioning — members act independently.</summary>
		None = 0,

		/// <summary>
		/// Members spread evenly around the focus, the ring rotated to move them least.
		/// Best for mixed groups that want to prevent the enemy from fleeing.
		/// </summary>
		Surround,

		/// <summary>
		/// The tank holds the front and everyone else spreads across the rear half-circle; a lone
		/// flanker goes directly behind. The front is the tank's side, or, with no tank fighting the
		/// focus, the direction the enemy faces.
		/// </summary>
		Flank,

		/// <summary>
		/// Members close in from the side they are already on, in a tight arc. Combined with
		/// <see cref="NPCGroup.FocusTargeting"/> for maximum single-target pressure.
		/// </summary>
		FocusFire,

		/// <summary>
		/// A Surround ring that keeps turning, at <see cref="NPCGroup.KiteRotationSpeed"/>.
		/// Best for ranged/caster groups that want to avoid melee contact.
		/// </summary>
		Kite,
	}
}
