using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// One equipment mutation the server has just applied, described completely enough to be
	/// persisted and reported without re-reading the containers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Raised by <see cref="IEquipmentController.OnServerEquipmentChanged"/> from inside the
	/// controller's own equip and unequip, so it covers every server-side path — the owner's
	/// predicted request, an ECA action, anything a later system adds — with one subscriber.
	/// The persistence layer used to live in the broadcast handler and so only ever saw the one
	/// path that went through it; an ECA equip rearranged item ownership between containers and
	/// wrote nothing.
	/// </para>
	/// <para>
	/// Holds live <see cref="Item"/> references. It is consumed synchronously on the main thread
	/// by the subscriber, which turns it into persistence rows before anything else can run.
	/// </para>
	/// </remarks>
	public readonly struct EquipmentChange
	{
		/// <summary>Whether an item entered or left the socket.</summary>
		public readonly EquipmentRequestKind Kind;

		/// <summary>The item that moved: into <see cref="Socket"/> for an equip, out of it for an unequip.</summary>
		public readonly Item Item;

		/// <summary>The equipment socket involved.</summary>
		public readonly ItemSlot Socket;

		/// <summary>The container on the other side of the move: the source of an equip, the destination of an unequip.</summary>
		public readonly IItemContainer Container;

		/// <summary>Which container <see cref="Container"/> is, for the persistence row.</summary>
		public readonly InventoryType ContainerType;

		/// <summary>
		/// For an equip, the index the item came from — and where <see cref="DisplacedItem"/> now
		/// sits, if there was one. For an unequip, the index the item landed in.
		/// </summary>
		public readonly int ContainerIndex;

		/// <summary>For an equip that replaced an equipped item, the item that was pushed back into the container. Otherwise null.</summary>
		public readonly Item DisplacedItem;

		/// <summary>
		/// For an unequip, every container item the placement touched — the item itself, or the
		/// stacks it merged into. Null for an equip.
		/// </summary>
		public readonly List<Item> ModifiedItems;

		private EquipmentChange(EquipmentRequestKind kind, Item item, ItemSlot socket, IItemContainer container,
			InventoryType containerType, int containerIndex, Item displacedItem, List<Item> modifiedItems)
		{
			Kind = kind;
			Item = item;
			Socket = socket;
			Container = container;
			ContainerType = containerType;
			ContainerIndex = containerIndex;
			DisplacedItem = displacedItem;
			ModifiedItems = modifiedItems;
		}

		/// <summary>Describes an item that just entered a socket.</summary>
		public static EquipmentChange ForEquip(Item item, ItemSlot socket, IItemContainer container, InventoryType containerType, int sourceIndex, Item displacedItem)
		{
			return new EquipmentChange(EquipmentRequestKind.Equip, item, socket, container, containerType, sourceIndex, displacedItem, null);
		}

		/// <summary>Describes an item that just left a socket.</summary>
		public static EquipmentChange ForUnequip(Item item, ItemSlot socket, IItemContainer container, InventoryType containerType, int landedIndex, List<Item> modifiedItems)
		{
			return new EquipmentChange(EquipmentRequestKind.Unequip, item, socket, container, containerType, landedIndex, null, modifiedItems);
		}
	}
}
