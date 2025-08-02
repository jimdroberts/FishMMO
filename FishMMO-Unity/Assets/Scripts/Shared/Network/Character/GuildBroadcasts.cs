using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for creating a new guild.
	/// Contains the name of the guild to be created.
	/// </summary>
	public struct GuildCreateBroadcast : IBroadcast
	{
		/// <summary>Name of the guild to create.</summary>
		public string GuildName;
	}

	/// <summary>
	/// Broadcast for inviting a character to a guild.
	/// Contains the inviter and target character IDs.
	/// </summary>
	public struct GuildInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player sending the invite.</summary>
		public long InviterCharacterID;
		/// <summary>Character ID of the player being invited.</summary>
		public long TargetCharacterID;
	}

	/// <summary>
	/// Broadcast for accepting a guild invitation.
	/// No additional data required.
	/// </summary>
	public struct GuildAcceptInviteBroadcast : IBroadcast
	{
	}
	/// <summary>
	/// Broadcast for declining a guild invitation.
	/// No additional data required.
	/// </summary>
	public struct GuildDeclineInviteBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for adding a member to a guild.
	/// Contains guild ID, character ID, rank, and location.
	/// </summary>
	public struct GuildAddBroadcast : IBroadcast
	{
		/// <summary>ID of the guild the member is being added to.</summary>
		public long GuildID;
		/// <summary>Character ID of the member being added.</summary>
		public long CharacterID;
		/// <summary>Rank of the member within the guild.</summary>
		public GuildRank Rank;
		/// <summary>Location of the member (may be used for online status or region).</summary>
		public string Location;
	}

	/// <summary>
	/// Broadcast for adding multiple members to a guild at once.
	/// Used for bulk member addition or synchronization.
	/// </summary>
	public struct GuildAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of members to add to the guild.</summary>
		public List<GuildAddBroadcast> Members;
	}

	/// <summary>
	/// Broadcast for a member leaving a guild.
	/// No additional data required.
	/// </summary>
	public struct GuildLeaveBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for removing a member from a guild.
	/// Contains the guild member ID to be removed.
	/// </summary>
	public struct GuildRemoveBroadcast : IBroadcast
	{
		/// <summary>Guild member ID of the member to remove.</summary>
		public long GuildMemberID;
	}

	/// <summary>
	/// Broadcast for changing a member's rank within a guild.
	/// Contains the guild member ID and the new rank.
	/// </summary>
	public struct GuildChangeRankBroadcast : IBroadcast
	{
		/// <summary>Guild member ID of the member whose rank is changing.</summary>
		public long GuildMemberID;
		/// <summary>New rank to assign to the member.</summary>
		public GuildRank Rank;
	}

	/// <summary>
	/// Result types for guild operations, indicating success or specific failure reasons.
	/// </summary>
	public enum GuildResultType : byte
	{
		/// <summary>Operation succeeded.</summary>
		Success = 0,
		/// <summary>Guild name is invalid.</summary>
		InvalidGuildName,
		/// <summary>Guild name already exists.</summary>
		NameAlreadyExists,
		/// <summary>Character is already in a guild.</summary>
		AlreadyInGuild,
	}

	/// <summary>
	/// Broadcast for sending the result of a guild operation.
	/// Contains the result type indicating success or failure reason.
	/// </summary>
	public struct GuildResultBroadcast : IBroadcast
	{
		/// <summary>Result of the guild operation.</summary>
		public GuildResultType Result;
	}
}