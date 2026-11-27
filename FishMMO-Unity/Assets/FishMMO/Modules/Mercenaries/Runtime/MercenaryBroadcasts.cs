using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>Server to client: a Mercenary was spoken to; ask the player whether to hire it.</summary>
	public struct MercenaryOfferBroadcast : IBroadcast
	{
		/// <summary>The Mercenary spoken to; the hire names it back.</summary>
		public long InteractableID;
		/// <summary>The kit it offers.</summary>
		public int KitID;
		/// <summary>Its name.</summary>
		public string Name;
		/// <summary>What hiring costs.</summary>
		public long Cost;
	}

	/// <summary>Client to server: hire the Mercenary at <see cref="InteractableID"/>.</summary>
	public struct MercenaryHireBroadcast : IBroadcast
	{
		/// <summary>The Mercenary spoken to.</summary>
		public long InteractableID;
	}

	/// <summary>Client to server: send the mercenary to rest (a temporary dismissal, which frees its party seat).</summary>
	public struct MercenaryDismissBroadcast : IBroadcast
	{
	}

	/// <summary>Client to server: bring the resting mercenary back (it takes a party seat again).</summary>
	public struct MercenaryReturnBroadcast : IBroadcast
	{
	}

	/// <summary>Client to server: let the mercenary go for good. Its gear is kept.</summary>
	public struct MercenaryReleaseBroadcast : IBroadcast
	{
	}

	/// <summary>Client to server (a request) and server to client (the stance now in force).</summary>
	public struct MercenaryStanceBroadcast : IBroadcast
	{
		/// <summary>When it fights.</summary>
		public PetStance Stance;
	}

	/// <summary>Client to server: put the item in this bag slot on the mercenary (in the gear slot it goes in).</summary>
	public struct MercenaryEquipBroadcast : IBroadcast
	{
		/// <summary>The inventory slot.</summary>
		public int InventorySlot;
	}

	/// <summary>Client to server: take the item in this gear slot back into the bags.</summary>
	public struct MercenaryUnequipBroadcast : IBroadcast
	{
		/// <summary>The gear slot (an <see cref="ItemSlot"/>).</summary>
		public byte GearSlot;
	}

	/// <summary>Server to client: the answer to a mercenary request, or news (it died, it went to rest).</summary>
	public struct MercenaryResultBroadcast : IBroadcast
	{
		/// <summary>What happened.</summary>
		public MercenaryResult Result;
		/// <summary>The mercenary's or kit's name, for the message.</summary>
		public string Name;
	}

	/// <summary>
	/// Server to client: the player's mercenary as the server holds it. Sent when the player arrives and whenever it
	/// changes. <see cref="KitID"/> 0 means no mercenary.
	/// </summary>
	public struct MercenaryStateBroadcast : IBroadcast
	{
		/// <summary>The kit hired, or 0 for none.</summary>
		public int KitID;
		/// <summary>Its name.</summary>
		public string Name;
		/// <summary>True while resting (not out, holding no seat).</summary>
		public bool Resting;
		/// <summary>When it fights.</summary>
		public PetStance Stance;
	}

	/// <summary>One item in the mercenary gear.</summary>
	public struct MercenaryGearEntry
	{
		/// <summary>The gear slot.</summary>
		public byte Slot;
		/// <summary>The item's identity.</summary>
		public long ItemID;
		/// <summary>Its template.</summary>
		public int TemplateID;
		/// <summary>Its generation seed.</summary>
		public int Seed;
		/// <summary>Its stack size.</summary>
		public uint Amount;
	}

	/// <summary>Server to client: every item in the player's mercenary gear (the whole set, replacing what the client holds).</summary>
	public struct MercenaryGearBroadcast : IBroadcast
	{
		/// <summary>The items, by slot. Slots not listed are empty.</summary>
		public MercenaryGearEntry[] Entries;
	}
}
