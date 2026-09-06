using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a pet to a character.
	/// Contains the pet's unique ID and its current orders.
	/// </summary>
	public struct PetAddBroadcast : IBroadcast
	{
		/// <summary>Unique ID of the pet to add.</summary>
		public long ID;

		/// <summary>The pet's current combat stance, so the UI opens showing the truth.</summary>
		public PetStance Stance;

		/// <summary>The pet's current movement order.</summary>
		public PetMovementOrder MovementOrder;

		/// <summary>The packed attack priority the server holds for this pet; see <c>PetAttackPriority</c>.</summary>
		public int AttackPriority;
	}

	/// <summary>
	/// Broadcast for removing a pet from a character.
	/// No additional data required.
	/// </summary>
	public struct PetRemoveBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for commanding a pet to follow its owner.
	/// No additional data required.
	/// </summary>
	public struct PetFollowBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for commanding a pet to stay in its current location.
	/// No additional data required.
	/// </summary>
	public struct PetStayBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for summoning a pet to the owner's location.
	/// No additional data required.
	/// </summary>
	public struct PetSummonBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for releasing a pet (removing it from ownership).
	/// No additional data required.
	/// </summary>
	public struct PetReleaseBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Client-to-server: send the pet at the owner's target.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Carries the owner's pinned and hovered targets separately, each as a NetworkObject id or
	/// 0, because the owner's attack priority (see <c>PetAttackPriority</c>) may try them in
	/// either order. Both are claims, not orders: the server accepts one only if it resolves to
	/// a spawned character in the owner's own scene, within
	/// <see cref="FishMMO.Shared.TargetController.MAX_TARGET_DISTANCE"/> of the owner, and passes
	/// every pet-target rule (alive, hostile by faction, not the owner or the pet). A client can
	/// therefore point but cannot name an arbitrary entity — including one it cannot see or
	/// reach — as its pet's victim.
	/// </para>
	/// <para>
	/// Sent in the message rather than read from the server's copy of the target frame because
	/// that copy is rate-limited and de-duplicated on the way up, so the click can beat it by up
	/// to a report interval. The server still uses its own copy for the "current" step when the
	/// message names nothing, and the highest-threat step needs nothing from the client.
	/// </para>
	/// </remarks>
	public struct PetAttackBroadcast : IBroadcast
	{
		/// <summary>The NetworkObject id of the owner's pinned target, or 0.</summary>
		public int PinnedTargetObjectID;

		/// <summary>The NetworkObject id of the owner's hovered target, or 0.</summary>
		public int HoveredTargetObjectID;
	}

	/// <summary>
	/// Client-to-server: set the order the pet attack command tries its target choices in.
	/// Also sent server-to-client to confirm the authoritative order.
	/// </summary>
	/// <remarks>
	/// The value is a packed <c>PetAttackPriority</c>. The server rejects anything that is not a
	/// permutation of the three steps and confirms what it holds, so the panel always shows the
	/// order actually in force.
	/// </remarks>
	public struct PetAttackPriorityBroadcast : IBroadcast
	{
		/// <summary>The requested (or, from the server, the authoritative) packed order.</summary>
		public int Priority;
	}

	/// <summary>
	/// Client-to-server: change the pet's combat stance.
	/// Also sent server-to-client to confirm the authoritative stance.
	/// </summary>
	public struct PetStanceBroadcast : IBroadcast
	{
		/// <summary>The requested (or, from the server, the authoritative) stance.</summary>
		public PetStance Stance;
	}

	/// <summary>
	/// Server-to-client: the pet's movement order changed.
	/// </summary>
	public struct PetMovementOrderBroadcast : IBroadcast
	{
		/// <summary>The authoritative movement order.</summary>
		public PetMovementOrder MovementOrder;
	}
}
