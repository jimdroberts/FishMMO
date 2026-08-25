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
	/// Client-to-server: send the pet at the owner's current target.
	/// </summary>
	/// <remarks>
	/// Carries no target ID on purpose. The server raycasts from the owner's replicated camera
	/// transform, in the owner's own physics scene and clamped to
	/// <see cref="FishMMO.Shared.TargetController.MAX_TARGET_DISTANCE"/>, exactly as the ability
	/// system resolves what a cast hits. A client can therefore point but cannot name an
	/// arbitrary entity — including one it cannot see or reach — as its pet's victim.
	/// </remarks>
	public struct PetAttackBroadcast : IBroadcast
	{
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
