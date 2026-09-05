using FishNet.Broadcast;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Why a housing request was refused.
	/// </summary>
	/// <remarks>
	/// Sent back rather than letting a refusal be silence. Housing refuses things for reasons the
	/// player cannot see — somebody claimed the plot on another channel a second ago, the owner
	/// revoked a key, the land is mid-reclamation — and a request that produces no answer at all is
	/// indistinguishable from one that was dropped. The client would have nothing to show and
	/// nothing to retry against.
	///
	/// <para>Values are explicit because they cross the wire; renumbering would change what an
	/// already-built client displays.</para>
	/// </remarks>
	public enum HousingResult : byte
	{
		/// <summary>The request was accepted.</summary>
		Success = 0,

		/// <summary>Housing is switched off on this server.</summary>
		Disabled = 1,

		/// <summary>The plot named does not exist, or has not been resolved yet.</summary>
		UnknownPlot = 2,

		/// <summary>The requester does not own the plot.</summary>
		NotTheOwner = 3,

		/// <summary>The requester lacks the permission the request needs.</summary>
		NotPermitted = 4,

		/// <summary>The plot is not in a state where this makes sense.</summary>
		WrongState = 5,

		/// <summary>The requester cannot pay.</summary>
		CannotAfford = 6,

		/// <summary>Somebody else got there first.</summary>
		AlreadyClaimed = 7,

		/// <summary>The requester already owns land, and may only own one plot.</summary>
		AlreadyOwnsLand = 8,

		/// <summary>The placement would not fit.</summary>
		DoesNotFit = 9,

		/// <summary>The vault entry named is not there.</summary>
		NothingStored = 10,

		/// <summary>Something failed on the server. Nothing was changed.</summary>
		Failed = 11,
	}

	/// <summary>
	/// The server's answer to a housing request.
	/// </summary>
	/// <remarks>
	/// One reply type for every request rather than one per operation. The client needs to know
	/// which plot and whether it worked; giving each operation its own shape would be several
	/// near-identical structs and several near-identical handlers.
	/// </remarks>
	public struct HousingResultBroadcast : IBroadcast
	{
		/// <summary>The plot the request was about, or zero when it was about a vault entry.</summary>
		public long PlotID;

		/// <summary>How it went.</summary>
		public HousingResult Result;
	}

	/// <summary>
	/// Asks to open a build session on a plot the sender owns.
	/// </summary>
	/// <remarks>
	/// The plot is named by its database identity rather than by a network object. A foundation is a
	/// scene object, and scene object identifiers are handed out fresh on every load and never
	/// persisted — so one would name a different foundation after a restart.
	/// </remarks>
	public struct HousingBeginBuildingBroadcast : IBroadcast
	{
		/// <summary>The plot to open.</summary>
		public long PlotID;
	}

	/// <summary>
	/// Asks to close a build session.
	/// </summary>
	public struct HousingEndBuildingBroadcast : IBroadcast
	{
		/// <summary>The plot to close.</summary>
		public long PlotID;
	}

	/// <summary>
	/// Declares a house finished, moving the plot to occupied.
	/// </summary>
	public struct HousingFinishBuildingBroadcast : IBroadcast
	{
		/// <summary>The plot being finished.</summary>
		public long PlotID;
	}

	/// <summary>
	/// Asks to place a structure on a plot.
	/// </summary>
	/// <remarks>
	/// Names a template by identifier and nothing else. The client never says which prefab to spawn,
	/// only which of the pieces the server offers it wants — so a forged request can only ever ask
	/// for something that already exists in shipped content.
	/// </remarks>
	public struct HousingPlaceStructureBroadcast : IBroadcast
	{
		/// <summary>The plot being built on.</summary>
		public long PlotID;

		/// <summary>Which structure to place.</summary>
		public int TemplateID;

		/// <summary>Where to put it, relative to the plot's origin.</summary>
		/// <remarks>
		/// Plot-relative rather than world-space, so moving a foundation in the editor carries the
		/// house with it instead of leaving it standing in a field.
		/// </remarks>
		public Vector3 LocalPosition;

		/// <summary>Which way it faces, in degrees about the vertical axis.</summary>
		public float Yaw;
	}

	/// <summary>
	/// Asks to take a structure back off a plot.
	/// </summary>
	public struct HousingRemoveStructureBroadcast : IBroadcast
	{
		/// <summary>The plot being changed.</summary>
		public long PlotID;

		/// <summary>Which structure to remove.</summary>
		public long StructureID;
	}

	/// <summary>
	/// Asks to give somebody access to a plot, or to change what they hold.
	/// </summary>
	public struct HousingGrantAccessBroadcast : IBroadcast
	{
		/// <summary>The plot being shared.</summary>
		public long PlotID;

		/// <summary>Who is being let in.</summary>
		public long CharacterID;

		/// <summary>
		/// What they may do, as a <see cref="PlotPermission"/> bitmask.
		/// </summary>
		/// <remarks>
		/// Clamped server-side to what the sender holds themselves, so this number is a request
		/// rather than an instruction. A client asking for more than its sender can give gets the
		/// intersection, not a refusal — the sender may simply have ticked a box they do not have.
		/// </remarks>
		public int Permissions;
	}

	/// <summary>
	/// Asks to take somebody's access away.
	/// </summary>
	public struct HousingRevokeAccessBroadcast : IBroadcast
	{
		/// <summary>The plot being closed.</summary>
		public long PlotID;

		/// <summary>Who is being shut out.</summary>
		public long CharacterID;
	}

	/// <summary>
	/// One person on a plot's guest list.
	/// </summary>
	public struct HousingAccessEntry
	{
		/// <summary>The character granted access.</summary>
		public long CharacterID;

		/// <summary>What they hold, as a <see cref="PlotPermission"/> bitmask.</summary>
		public int Permissions;
	}

	/// <summary>
	/// A plot's guest list, sent to its owner.
	/// </summary>
	/// <remarks>
	/// Sent only to somebody who may see it. A plot's access list is the owner's business, and
	/// broadcasting it to observers would tell the street who has keys to which house.
	/// </remarks>
	public struct HousingAccessListBroadcast : IBroadcast
	{
		/// <summary>The plot the list is for.</summary>
		public long PlotID;

		/// <summary>Everybody currently let in.</summary>
		public HousingAccessEntry[] Entries;
	}

	/// <summary>
	/// Asks for the sender's house vault.
	/// </summary>
	public struct HousingVaultRequestBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// One stack held in a house vault.
	/// </summary>
	public struct HousingVaultEntry
	{
		/// <summary>The vault row, named when retrieving or forfeiting it.</summary>
		public long VaultID;

		/// <summary>Which structure is stored.</summary>
		public int TemplateID;

		/// <summary>How many are held.</summary>
		public int Amount;

		/// <summary>The plot it came off.</summary>
		public long OriginalPlotID;

		/// <summary>
		/// What retrieving it costs right now.
		/// </summary>
		/// <remarks>
		/// Computed by the server and sent, rather than left for the client to work out from a base
		/// fee and a rate. The client would have to reimplement the formula, and the moment the two
		/// drifted the player would be quoted one price and charged another.
		/// </remarks>
		public long Fee;
	}

	/// <summary>
	/// The sender's house vault, with today's fee against each entry.
	/// </summary>
	public struct HousingVaultBroadcast : IBroadcast
	{
		/// <summary>Everything the character is owed.</summary>
		public HousingVaultEntry[] Entries;
	}

	/// <summary>
	/// Asks to buy one stack back out of the vault.
	/// </summary>
	public struct HousingVaultRetrieveBroadcast : IBroadcast
	{
		/// <summary>The entry to retrieve.</summary>
		public long VaultID;
	}

	/// <summary>
	/// Gives one stack up permanently, for nothing.
	/// </summary>
	public struct HousingVaultForfeitBroadcast : IBroadcast
	{
		/// <summary>The entry to give up.</summary>
		public long VaultID;
	}
}
