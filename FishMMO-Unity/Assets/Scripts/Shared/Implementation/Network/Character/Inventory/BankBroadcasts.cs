using FishNet.Broadcast;
using FishNet.Serializing;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for setting a single item in the bank inventory.
	/// Contains all data needed to place or update an item in a bank slot.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct BankSetItemBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the item.</summary>
		public long InstanceID;
		/// <summary>Template ID of the item type.</summary>
		public int TemplateID;
		/// <summary>Slot index in the bank inventory.</summary>
		public int Slot;
		/// <summary>Seed value for item randomization or uniqueness.</summary>
		public int Seed;
		/// <summary>Stack size of the item.</summary>
		public uint StackSize;
	}

	/// <summary>Wire format for <see cref="BankSetItemBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for the same two fields as
	/// <c>InventorySetItemBroadcastSerializer</c>, and for the same reason.
	/// <c>TemplateID</c> is a deterministic 32-bit hash (<c>CachedScriptableObject.AddToCache</c>)
	/// and <c>Seed</c> is full entropy by construction, so both span the whole range and FishNet's
	/// signed-packed form spends FIVE bytes on each where unpacked spends four. The login sync
	/// ships the ENTIRE bank in one <see cref="BankSetMultipleItemsBroadcast"/>, and a bank is the
	/// largest container a character owns. <c>InstanceID</c> stays packed — a database sequence
	/// value is small — as do the slot index and stack size. See <c>ObservedBuffEntry</c>.
	/// </remarks>
	public static class BankSetItemBroadcastSerializer
	{
		/// <summary>Writes a <see cref="BankSetItemBroadcast"/>.</summary>
		public static void WriteBankSetItemBroadcast(this Writer writer, BankSetItemBroadcast value)
		{
			writer.WriteInt64(value.InstanceID);
			writer.WriteInt32Unpacked(value.TemplateID);
			writer.WriteInt32(value.Slot);
			writer.WriteInt32Unpacked(value.Seed);
			writer.WriteUInt32(value.StackSize);
		}

		/// <summary>Reads a <see cref="BankSetItemBroadcast"/>.</summary>
		public static BankSetItemBroadcast ReadBankSetItemBroadcast(this Reader reader)
		{
			return new BankSetItemBroadcast()
			{
				InstanceID = reader.ReadInt64(),
				TemplateID = reader.ReadInt32Unpacked(),
				Slot = reader.ReadInt32(),
				Seed = reader.ReadInt32Unpacked(),
				StackSize = reader.ReadUInt32(),
			};
		}

		/// <summary>Writes an array of <see cref="BankSetItemBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for BankSetItemBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteBankSetItemBroadcastArray(this Writer writer, BankSetItemBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteBankSetItemBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="BankSetItemBroadcast"/>.</summary>
		public static BankSetItemBroadcast[] ReadBankSetItemBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}

			BankSetItemBroadcast[] value = new BankSetItemBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadBankSetItemBroadcast();
			}

			return value;
		}
	}

	/// <summary>
	/// Broadcast for setting multiple items in the bank inventory at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct BankSetMultipleItemsBroadcast : IBroadcast
	{
		/// <summary>List of items to set in the bank.</summary>
		public BankSetItemBroadcast[] Items;
	}

	/// <summary>
	/// Broadcast for removing an item from a specific bank slot.
	/// </summary>
	public struct BankRemoveItemBroadcast : IBroadcast
	{
		/// <summary>Slot index to remove the item from.</summary>
		public int Slot;
	}

	/// <summary>
	/// Broadcast for swapping two item slots in the bank or between inventories.
	/// </summary>
	public struct BankSwapItemSlotsBroadcast : IBroadcast
	{
		/// <summary>Source slot index.</summary>
		public int From;
		/// <summary>Destination slot index.</summary>
		public int To;
		/// <summary>Type of inventory the item is being moved from.</summary>
		public InventoryType FromInventory;
	}

	/// <summary>
	/// Client request to split part of a stack off into a bank slot. Issue #198.
	/// </summary>
	/// <remarks>
	/// The bank-destination twin of <c>InventorySplitItemBroadcast</c>; see it for why the shape
	/// mirrors the swap and why the server never echoes this message.
	/// </remarks>
	public struct BankSplitItemBroadcast : IBroadcast
	{
		/// <summary>Slot holding the stack being split, in <see cref="FromInventory"/>.</summary>
		public int From;
		/// <summary>Bank slot the split half goes to: empty, or a matching stack with room.</summary>
		public int To;
		/// <summary>How much to take. At least 1 and less than the stack holds; anything else is refused.</summary>
		public uint Amount;
		/// <summary>Container holding the stack being split.</summary>
		public InventoryType FromInventory;
	}
}