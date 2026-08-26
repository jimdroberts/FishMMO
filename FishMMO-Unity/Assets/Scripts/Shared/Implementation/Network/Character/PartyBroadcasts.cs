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
	/// One party member's live state, as observed on a scene server.
	/// </summary>
	/// <remarks>
	/// Only sent for members the sending scene server hosts <b>in the same Unity scene as the
	/// recipient</b>. A member the recipient cannot be standing next to — different zone,
	/// different dungeon instance, different scene server, or offline — is absent from the
	/// payload entirely, and the client greys their row out rather than drawing values it has no
	/// reason to believe. Absence is therefore meaningful: see
	/// <see cref="PartyMemberVitalsUpdateBroadcast"/>.
	/// </remarks>
	public struct PartyMemberVitalsEntry
	{
		/// <summary>Character ID of the member.</summary>
		public long CharacterID;
		/// <summary>The member's health fraction, 0-1.</summary>
		public float HealthPCT;
		/// <summary>The member's mana fraction, 0-1.</summary>
		public float ManaPCT;
		/// <summary>The member's stamina fraction, 0-1.</summary>
		public float StaminaPCT;
		/// <summary>
		/// Damage the member has dealt in the current encounter, divided by its elapsed length.
		/// </summary>
		/// <remarks>
		/// Zero once the encounter has timed out — the meter is per-encounter, not per-session,
		/// so it must not carry a number from the last fight into the lull after it.
		/// </remarks>
		public float DamagePerSecond;
		/// <summary>Healing the member has done in the current encounter, per second.</summary>
		public float HealPerSecond;
		/// <summary>
		/// The buffs and debuffs the member is showing, exactly as the SERVER chose them.
		/// </summary>
		/// <remarks>
		/// The same server-filtered list the target frame draws from — already stripped of
		/// anything marked <c>HiddenFromOthers</c>, carrying no tick-domain state and no template
		/// hooks. Buffs and debuffs travel together and are split by the client on the template's
		/// own <c>IsDebuff</c> flag, because that flag is a property of the template rather than
		/// of the wire format and duplicating it into two arrays would let the two disagree.
		/// <para>
		/// <see cref="ObservedBuffEntry.RemainingSeconds"/> is re-based to the moment this payload
		/// was built rather than to the last observed-buff push, so the client's local countdown
		/// starts from a current figure however long ago the buff was applied.
		/// </para>
		/// </remarks>
		public ObservedBuffEntry[] Buffs;
	}

	/// <summary>
	/// Broadcast carrying live state for the party members sharing a scene with the recipient.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The roster payload (<see cref="PartyAddMultipleBroadcast"/>) gets its health figure from
	/// the party database row, and that row is written on connect and on disconnect and at no
	/// other time — so every party bar sat frozen at the value its owner logged in with, for the
	/// whole session. This message is sent on the party update pump from the in-memory
	/// controllers of the members the scene server actually hosts, which is the only place a
	/// current value exists without a database write per member per second.
	/// </para>
	/// <para>
	/// <b>The payload is complete for its scene.</b> Every member the recipient shares a scene
	/// with appears in it, including the recipient. A client may therefore treat a roster member
	/// missing from the latest payload as being somewhere else, which is exactly what drives the
	/// greyed-out facade — no separate presence message, and no way for the two to disagree.
	/// </para>
	/// </remarks>
	public struct PartyMemberVitalsUpdateBroadcast : IBroadcast
	{
		/// <summary>Live state for each party member sharing the recipient's scene.</summary>
		public PartyMemberVitalsEntry[] Members;
	}
}
