using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for creating a new party.
	/// Contains the party ID and location.
	/// </summary>
	public struct PartyCreateBroadcast : IBroadcast
	{
		/// <summary>ID of the newly created party.</summary>
		public long PartyID;
		/// <summary>Location of the party (may be used for region or instance).</summary>
		public string Location;
	}

	/// <summary>
	/// Broadcast for inviting a character to a party.
	/// Contains the inviter and target character IDs.
	/// </summary>
	public struct PartyInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player sending the invite.</summary>
		public long InviterCharacterID;
		/// <summary>Character ID of the player being invited.</summary>
		public long TargetCharacterID;
	}

	/// <summary>
	/// Broadcast for accepting a party invitation.
	/// No additional data required.
	/// </summary>
	public struct PartyAcceptInviteBroadcast : IBroadcast
	{
	}
	/// <summary>
	/// Broadcast for declining a party invitation.
	/// No additional data required.
	/// </summary>
	public struct PartyDeclineInviteBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for adding a member to a party.
	/// Contains party ID, character ID, rank, and health percentage.
	/// </summary>
	public struct PartyAddBroadcast : IBroadcast
	{
		/// <summary>ID of the party the member is being added to.</summary>
		public long PartyID;
		/// <summary>Character ID of the member being added.</summary>
		public long CharacterID;
		/// <summary>Rank of the member within the party.</summary>
		public PartyRank Rank;
		/// <summary>Current health percentage of the member.</summary>
		public float HealthPCT;
	}

	/// <summary>
	/// Broadcast for adding multiple members to a party at once.
	/// Used for bulk member addition or synchronization.
	/// </summary>
	public struct PartyAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of members to add to the party.</summary>
		public List<PartyAddBroadcast> Members;
	}

	/// <summary>
	/// Broadcast for a member leaving a party.
	/// No additional data required.
	/// </summary>
	public struct PartyLeaveBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for removing a member from a party.
	/// Contains the member ID to be removed.
	/// </summary>
	public struct PartyRemoveBroadcast : IBroadcast
	{
		/// <summary>Member ID of the party member to remove.</summary>
		public long MemberID;
	}

	/// <summary>
	/// Broadcast for changing a member's rank within a party.
	/// Contains the member ID whose rank is changing.
	/// </summary>
	public struct PartyChangeRankBroadcast : IBroadcast
	{
		/// <summary>Member ID of the party member whose rank is changing.</summary>
		public long MemberID;
	}
}