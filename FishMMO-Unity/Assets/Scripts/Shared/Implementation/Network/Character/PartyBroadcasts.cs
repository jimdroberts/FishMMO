using FishNet.Broadcast;

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
	/// </summary>
	/// <remarks>
	/// Carries the identity of the invitation being answered. This used to be an empty struct,
	/// which left the server resolving "whatever invitation is pending for this character" — so a
	/// dialog the player left open past the invitation TTL joined whoever invited them NEXT. The
	/// server re-verifies this against its own pending record and refuses a mismatch; the field is
	/// a claim to be checked, never a value to be trusted.
	/// </remarks>
	public struct PartyAcceptInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player whose invitation is being accepted.</summary>
		public long InviterCharacterID;
	}
	/// <summary>
	/// Broadcast for declining a party invitation.
	/// </summary>
	/// <remarks>
	/// Carries the same identity as the accept broadcast for the same reason: a decline that
	/// arrives after the pending slot has been refilled would otherwise throw away an invitation
	/// the player has not seen yet.
	/// </remarks>
	public struct PartyDeclineInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player whose invitation is being declined.</summary>
		public long InviterCharacterID;
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
		public PartyAddBroadcast[] Members;
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
	/// Contains the character ID to be removed.
	/// </summary>
	public struct PartyRemoveBroadcast : IBroadcast
	{
		/// <summary>Character ID of the party member to remove.</summary>
		public long CharacterID;
	}

	/// <summary>
	/// Broadcast for changing a member's rank within a party.
	/// Contains the character ID and the new rank.
	/// </summary>
	public struct PartyChangeRankBroadcast : IBroadcast
	{
		/// <summary>Character ID of the party member whose rank is changing.</summary>
		public long CharacterID;
		/// <summary>New rank to assign to the member.</summary>
		public PartyRank Rank;
	}

	/// <summary>
	/// One party member's live health, as observed on a scene server.
	/// </summary>
	public struct PartyMemberHealthEntry
	{
		/// <summary>Character ID of the member.</summary>
		public long CharacterID;
		/// <summary>The member's health fraction, 0-1.</summary>
		public float HealthPCT;
	}

	/// <summary>
	/// Broadcast carrying live health for the party members visible to one scene server.
	/// </summary>
	/// <remarks>
	/// The roster payload gets its health figure from the party database row, and that row is
	/// written on connect and on disconnect and at no other time — so every party health bar sat
	/// frozen at the value its owner logged in with, for the whole session. This message is sent
	/// on the party update pump from the in-memory attribute controllers of the members the scene
	/// server actually hosts, which is the only place a current value exists without adding a
	/// database write per member per second.
	///
	/// Members on OTHER scene servers are not in this payload. Their bars keep the last figure the
	/// roster carried; a stale bar for someone in a different zone is a far smaller problem than
	/// a stale bar for the person standing next to you.
	/// </remarks>
	public struct PartyMemberHealthUpdateBroadcast : IBroadcast
	{
		/// <summary>Live health for each locally-visible party member.</summary>
		public PartyMemberHealthEntry[] Members;
	}
}
