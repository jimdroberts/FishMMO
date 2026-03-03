using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a pet to a character.
	/// Contains the pet's unique ID.
	/// </summary>
	public struct PetAddBroadcast : IBroadcast
	{
		/// <summary>Unique ID of the pet to add.</summary>
		public long ID;
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
}