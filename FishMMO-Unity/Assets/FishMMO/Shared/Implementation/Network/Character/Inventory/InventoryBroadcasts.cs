using FishNet.Broadcast;
using FishNet.Serializing;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for setting a single item in the character's inventory.
	/// Contains all data needed to place or update an item in an inventory slot.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct InventorySetItemBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the item.</summary>
		public long InstanceID;
		/// <summary>Template ID of the item type.</summary>
		public int TemplateID;
		/// <summary>Slot index in the inventory.</summary>
		public int Slot;
		/// <summary>Seed value for item randomization or uniqueness.</summary>
		public int Seed;
		/// <summary>Stack size of the item.</summary>
		public uint StackSize;
	}

	/// <summary>Wire format for <see cref="InventorySetItemBroadcast"/>.</summary>
	/// <remarks>
	/// Hand written for two fields. <c>TemplateID</c> is a deterministic 32-bit hash
	/// (<c>CachedScriptableObject.AddToCache</c>) and <c>Seed</c> is full entropy by construction,
	/// so both span the whole range and FishNet's signed-packed form spends FIVE bytes on each
	/// where unpacked spends exactly four. The login sync ships the ENTIRE inventory in one
	/// <see cref="InventorySetMultipleItemsBroadcast"/>, so those two bytes are paid per occupied
	/// slot on every login. <c>InstanceID</c> stays packed — it is a database sequence value and
	/// small — as do the slot index and stack size. See <c>ObservedBuffEntry</c>.
	/// </remarks>
	public static class InventorySetItemBroadcastSerializer
	{
		/// <summary>Upper bound accepted for an inventory array length, against a corrupt stream.</summary>
		/// <remarks>
		/// <para>
		/// Anchored on the container: <c>InventoryController.OnAwake</c> calls
		/// <c>AddSlots(null, 32)</c>, so the largest legitimate array — a full inventory sent in one
		/// <see cref="InventorySetMultipleItemsBroadcast"/> at login — is 32 entries. There is no
		/// <c>Constants</c> value to point at; 32 is written into the controller directly, which is
		/// why this bound restates it here rather than referencing it.
		/// </para>
		/// <para>
		/// The ceiling is 32x that capacity on purpose. Truncating a legitimate array is worse than
		/// the allocation it prevents — a player would silently lose rows from their own bags — and
		/// the bound's whole job is to stop <c>new InventorySetItemBroadcast[2_000_000_000]</c>, not
		/// to police a plausible count. A thousand of these structs is tens of kilobytes, so the
		/// headroom costs nothing and survives bag slots, expansions, or a doubled starting size
		/// without anybody remembering this file exists.
		/// </para>
		/// <para>
		/// This is a server → client path, so the realistic trigger is a mangled or truncated
		/// stream — a desynchronised reader interpreting the middle of some other field as a length
		/// prefix — rather than a hostile client. A client cannot send this message to itself. The
		/// bound is cheap insurance against corruption, not a security control.
		/// </para>
		/// </remarks>
		public const int MaxInventoryItems = 1024;

		/// <summary>Writes an <see cref="InventorySetItemBroadcast"/>.</summary>
		public static void WriteInventorySetItemBroadcast(this Writer writer, InventorySetItemBroadcast value)
		{
			writer.WriteInt64(value.InstanceID);
			writer.WriteInt32Unpacked(value.TemplateID);
			writer.WriteInt32(value.Slot);
			writer.WriteInt32Unpacked(value.Seed);
			writer.WriteUInt32(value.StackSize);
		}

		/// <summary>Reads an <see cref="InventorySetItemBroadcast"/>.</summary>
		public static InventorySetItemBroadcast ReadInventorySetItemBroadcast(this Reader reader)
		{
			return new InventorySetItemBroadcast()
			{
				InstanceID = reader.ReadInt64(),
				TemplateID = reader.ReadInt32Unpacked(),
				Slot = reader.ReadInt32(),
				Seed = reader.ReadInt32Unpacked(),
				StackSize = reader.ReadUInt32(),
			};
		}

		/// <summary>Writes an array of <see cref="InventorySetItemBroadcast"/>.</summary>
		/// <remarks>
		/// Explicit because the element carries <c>[UseGlobalCustomSerializer]</c>. That attribute
		/// tells FishNet the element has a hand-written serializer, and codegen then declines to
		/// synthesise one for the ARRAY as well — it emits nothing and says nothing at build time.
		/// The gap only appears when a broadcast carrying the array is actually sent, as
		/// "Write method not found for InventorySetItemBroadcast[]", by which point the send has already failed.
		/// A null array is written as length -1 so it round-trips as null rather than as empty.
		/// </remarks>
		public static void WriteInventorySetItemBroadcastArray(this Writer writer, InventorySetItemBroadcast[] value)
		{
			if (value == null)
			{
				writer.WriteInt32(-1);
				return;
			}

			writer.WriteInt32(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				writer.WriteInventorySetItemBroadcast(value[i]);
			}
		}

		/// <summary>Reads an array of <see cref="InventorySetItemBroadcast"/>.</summary>
		/// <remarks>
		/// A length past <see cref="MaxInventoryItems"/> cannot be allocated and cannot be
		/// resynchronised past either — the entries behind it are only locatable by trusting the
		/// count just rejected — so the array comes back empty and the caller applies nothing. Only
		/// the sender's own -1, which is not a length at all, means null.
		/// </remarks>
		public static InventorySetItemBroadcast[] ReadInventorySetItemBroadcastArray(this Reader reader)
		{
			int length = reader.ReadInt32();
			if (length < 0)
			{
				return null;
			}
			if (length > MaxInventoryItems)
			{
				return System.Array.Empty<InventorySetItemBroadcast>();
			}

			InventorySetItemBroadcast[] value = new InventorySetItemBroadcast[length];
			for (int i = 0; i < length; i++)
			{
				value[i] = reader.ReadInventorySetItemBroadcast();
			}

			return value;
		}
	}

	/// <summary>
	/// Broadcast for setting multiple items in the character's inventory at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct InventorySetMultipleItemsBroadcast : IBroadcast
	{
		/// <summary>List of items to set in the inventory.</summary>
		public InventorySetItemBroadcast[] Items;
	}

	/// <summary>
	/// Broadcast for removing an item from a specific inventory slot.
	/// </summary>
	public struct InventoryRemoveItemBroadcast : IBroadcast
	{
		/// <summary>Slot index to remove the item from.</summary>
		public int Slot;
	}

	/// <summary>
	/// Broadcast for swapping two item slots in the inventory or between inventories.
	/// </summary>
	public struct InventorySwapItemSlotsBroadcast : IBroadcast
	{
		/// <summary>Source slot index.</summary>
		public int From;
		/// <summary>Destination slot index.</summary>
		public int To;
		/// <summary>Type of inventory the item is being moved from.</summary>
		public InventoryType FromInventory;
	}

	/// <summary>
	/// Client request to split part of a stack off into an inventory slot. Issue #198.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Shaped like <see cref="InventorySwapItemSlotsBroadcast"/> on purpose: the destination is
	/// always an inventory slot, <see cref="FromInventory"/> names where the stack is, and the
	/// two indices travel in the same fields — so the failure message, the client's pending-slot
	/// bookkeeping and the server's banker check all treat it exactly as they treat a swap.
	/// </para>
	/// <para>
	/// <b>Never echoed.</b> A swap is acknowledged by echoing the request, because the client can
	/// apply a swap from the two indices alone. A split creates an item the client has never seen,
	/// so the server answers with the ordinary set-slot messages for both slots instead, the same
	/// way it reports a granted item.
	/// </para>
	/// </remarks>
	public struct InventorySplitItemBroadcast : IBroadcast
	{
		/// <summary>Slot holding the stack being split, in <see cref="FromInventory"/>.</summary>
		public int From;
		/// <summary>Inventory slot the split half goes to: empty, or a matching stack with room.</summary>
		public int To;
		/// <summary>How much to take. At least 1 and less than the stack holds; anything else is refused.</summary>
		public uint Amount;
		/// <summary>Container holding the stack being split.</summary>
		public InventoryType FromInventory;
	}
}