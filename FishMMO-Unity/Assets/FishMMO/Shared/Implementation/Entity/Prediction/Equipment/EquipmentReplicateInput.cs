using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Packs an owner's equipment request into the single byte
	/// <see cref="CharacterReplicateData.EquipmentRequest"/> carries.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Layout, low bits first: two bits of <see cref="EquipmentRequestKind"/>, two bits of
	/// <see cref="InventoryType"/>, four bits of <see cref="ItemSlot"/>. Every field is checked
	/// against its width on the way in, so a value that does not fit is refused rather than
	/// silently aliased onto another socket — a sword equipped to the wrong part of the body is
	/// the failure <see cref="ItemSlot"/>'s own remarks warn about.
	/// </para>
	/// <para>
	/// Zero is "no request", by construction: <see cref="EquipmentRequestKind.None"/> is zero and
	/// occupies the low bits, so a default replicate decodes to nothing asked whatever the other
	/// bits hold.
	/// </para>
	/// </remarks>
	public static class EquipmentReplicateInput
	{
		private const int KIND_BITS = 2;
		private const int CONTAINER_BITS = 2;
		private const int SOCKET_BITS = 4;

		private const byte KIND_MASK = (1 << KIND_BITS) - 1;
		private const byte CONTAINER_MASK = (1 << CONTAINER_BITS) - 1;
		private const byte SOCKET_MASK = (1 << SOCKET_BITS) - 1;

		/// <summary>The largest <see cref="ItemSlot"/> value the byte can carry.</summary>
		public const int MaxSocket = SOCKET_MASK;

		/// <summary>
		/// Packs a request. Returns false, and zero, when any field is outside its width.
		/// </summary>
		public static bool TryPack(EquipmentRequestKind kind, InventoryType container, ItemSlot socket, out byte packed)
		{
			packed = 0;
			if (kind == EquipmentRequestKind.None ||
				(byte)kind > KIND_MASK ||
				(container != InventoryType.Inventory && container != InventoryType.Bank) ||
				(byte)socket > SOCKET_MASK)
			{
				// Equipment-to-equipment is refused on the way in as well as on the way out.
				return false;
			}

			packed = (byte)((byte)kind | ((byte)container << KIND_BITS) | ((byte)socket << (KIND_BITS + CONTAINER_BITS)));
			return true;
		}

		/// <summary>
		/// Unpacks a request. Returns false for the no-request value and for any packed byte
		/// whose fields do not name a defined kind, container and socket.
		/// </summary>
		public static bool TryUnpack(byte packed, out EquipmentRequestKind kind, out InventoryType container, out ItemSlot socket)
		{
			kind = (EquipmentRequestKind)(packed & KIND_MASK);
			container = (InventoryType)((packed >> KIND_BITS) & CONTAINER_MASK);
			socket = (ItemSlot)((packed >> (KIND_BITS + CONTAINER_BITS)) & SOCKET_MASK);

			if (kind != EquipmentRequestKind.Equip && kind != EquipmentRequestKind.Unequip)
			{
				return false;
			}
			if (container != InventoryType.Inventory && container != InventoryType.Bank)
			{
				// Equipment-to-equipment is not a move the protocol has, and the two bits leave
				// one value undefined; neither may reach the containers.
				return false;
			}
			return System.Enum.IsDefined(typeof(ItemSlot), socket);
		}
	}
}
