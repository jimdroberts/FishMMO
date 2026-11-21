using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for requesting to use an interactable object.
	/// Contains the ID of the interactable object.
	/// </summary>
	public struct InteractableBroadcast : IBroadcast
	{
		/// <summary>ID of the interactable object to use.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Broadcast for interacting with an ability crafter object.
	/// Contains the interactable object's ID.
	/// </summary>
	public struct AbilityCrafterBroadcast : IBroadcast
	{
		/// <summary>ID of the ability crafter object.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Broadcast for interacting with a banker object.
	/// No additional data required.
	/// </summary>
	public struct BankerBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Server → Client broadcast opening the dungeon finder for one entrance.
	/// </summary>
	/// <remarks>
	/// Carries the entrance's identity and the dungeon's template ID and nothing else. The
	/// description, the artwork and the whole difficulty list are resolved client-side from the
	/// template cache, so opening a panel costs one <c>int</c> on the wire rather than a copy of
	/// the dungeon's configuration — and the two sides can never disagree about the rules,
	/// because they are reading the same asset.
	/// <para>
	/// Nothing is armed server-side by this message. It opens a window; every action the window
	/// offers is a separate request that is authorised on its own.
	/// </para>
	/// </remarks>
	public struct DungeonFinderBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Template ID of the dungeon, or 0 when the entrance has none configured.</summary>
		public int DungeonTemplateID;
	}

	/// <summary>
	/// Where a character stands with the group finder.
	/// </summary>
	public enum GroupFinderState : byte
	{
		/// <summary>Not in the queue. <see cref="GroupFinderStatusBroadcast.Reason"/> says why, when there is a why.</summary>
		None = 0,

		/// <summary>Waiting for enough players, or for an open run with room.</summary>
		Waiting = 1,

		/// <summary>
		/// A group formed and the character has been placed in its party. The transfer into the
		/// run follows on the server's next pump — immediately if the character is free to
		/// travel, otherwise as soon as they are. Leaving is no longer possible from here.
		/// </summary>
		Matched = 2,
	}

	/// <summary>
	/// Why the group finder refused, or ended, a character's request.
	/// </summary>
	public enum GroupFinderRefusalReason : byte
	{
		/// <summary>Nothing to explain.</summary>
		None = 0,
		/// <summary>The entrance no longer exists, or the player has walked away from it.</summary>
		NoEntrance = 1,
		/// <summary>The dungeon does not offer the difficulty that was asked for.</summary>
		UnknownDifficulty = 2,
		/// <summary>Find Group is not offered at this difficulty, or the dungeon cannot seat a group.</summary>
		NotAvailable = 3,
		/// <summary>The character is already inside instanced content.</summary>
		InInstance = 4,
		/// <summary>
		/// The character is in a party with other people. Matching joins a party the finder
		/// builds, so the player must leave theirs first — or have its leader open the dungeon
		/// to others, which is how a partial group fills its empty slots.
		/// </summary>
		InParty = 5,
		/// <summary>The character, or a party they were in, still holds an open instance.</summary>
		HoldsInstance = 6,
		/// <summary>Asked again too soon.</summary>
		OnCooldown = 7,
		/// <summary>The server could not complete the request; try again.</summary>
		ServerError = 8,
		/// <summary>The player left the queue.</summary>
		Left = 9,
		/// <summary>Removed from the queue because the character joined a party by other means.</summary>
		JoinedParty = 10,
		/// <summary>Removed from the queue because the character entered an instance by other means.</summary>
		EnteredInstance = 11,
		/// <summary>
		/// A group formed, but the character stayed untransferable — in combat, dead — for
		/// longer than the server allows, and the group went on without them.
		/// </summary>
		GroupLeftWithoutYou = 12,
		/// <summary>The queue entry disappeared server-side: a server restart, or a stale-row sweep.</summary>
		Removed = 13,
		/// <summary>
		/// Removed from the queue because the character walked away from the entrance. Waiting is
		/// done standing at the dungeon, so that being moved into it is never a surprise.
		/// </summary>
		LeftEntrance = 14,
		/// <summary>Only the party leader may queue the party for an arena.</summary>
		NotPartyLeader = 15,
		/// <summary>The party has more members than a team of the chosen format seats.</summary>
		PartyTooLarge = 16,
		/// <summary>A party member is not at the board, or not on this scene server.</summary>
		PartyNotPresent = 17,
		/// <summary>A party member is inside an instance, holds one, or is seated in a live arena match.</summary>
		PartyMemberBusy = 18,
		/// <summary>Locked out of the arena queue for deserting a match or declining a ready check.</summary>
		QueueLocked = 19,
		/// <summary>Ranked arenas: a pre-made party must fill a whole team of the format.</summary>
		PartyMustFillTeam = 20,
	}

	/// <summary>
	/// Broadcast for interacting with a merchant object.
	/// Contains the interactable object's ID and the merchant's template ID.
	/// </summary>
	public struct MerchantBroadcast : IBroadcast
	{
		/// <summary>ID of the merchant object.</summary>
		public long InteractableID;
		/// <summary>Template ID of the merchant.</summary>
		public int TemplateID;
	}

	// ──────────────────────────────────────────
	//  Mailbox
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast to open the mailbox UI.
	/// </summary>
	public struct MailboxBroadcast : IBroadcast
	{
		/// <summary>ID of the mailbox interactable.</summary>
		public long InteractableID;
	}
}
