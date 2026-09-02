using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Builds a character the UI panels can read from, without a network session.
	/// </summary>
	/// <remarks>
	/// <para>The real <c>PlayerCharacter</c> component is used rather than a hand-written
	/// <c>IPlayerCharacter</c>: that interface and <c>ICharacter</c> carry 76 members between them,
	/// nearly all irrelevant to rendering. <c>AddComponent</c> works in edit mode (no networking is
	/// touched), identity is settable, and <c>BaseCharacter.Behaviours</c> — the dictionary
	/// <c>TryGet&lt;T&gt;</c> reads — can be seeded by reflection. Panels therefore resolve their
	/// controllers through exactly the path they use at runtime.</para>
	/// <para>Only the controllers the panels actually ask for are faked, and only the members they
	/// call carry real behaviour; the rest satisfy the compiler.</para>
	/// </remarks>
	public static class Rig
	{
		public static PlayerCharacter Character { get; private set; }

		private static Dictionary<Type, ICharacterBehaviour> behaviours;

		/// <summary>Creates the character and registers every faked controller on it.</summary>
		public static PlayerCharacter Build(GameObject host)
		{
			Character = host.AddComponent<PlayerCharacter>();
			Set(Character, "CharacterName", "Thalorin");
			Set(Character, "ID", 1001L);

			FieldInfo field = typeof(BaseCharacter).GetField("Behaviours",
				BindingFlags.NonPublic | BindingFlags.Instance);
			behaviours = field.GetValue(Character) as Dictionary<Type, ICharacterBehaviour>;

			Register<IInventoryController>(new FakeInventory(Character));
			Register<IEquipmentController>(new FakeEquipment(Character));
			Register<IBankController>(new FakeBank(Character));
			Register<ICharacterAttributeController>(new FakeAttributes(Character));
			Register<IAchievementController>(new FakeAchievements(Character));
			Register<IFactionController>(new FakeFactions(Character));
			Register<IFriendController>(new FakeFriends(Character));
			Register<IPetController>(new FakePet(Character));

			Fixtures.Apply(Character);
			return Character;
		}

		private static void Register<T>(ICharacterBehaviour behaviour) where T : class, ICharacterBehaviour
		{
			behaviours[typeof(T)] = behaviour;
		}

		/// <summary>Writes a property, its auto-property backing field, or a plain field.</summary>
		public static bool Set(object target, string member, object value)
		{
			Type t = target.GetType();
			PropertyInfo prop = t.GetProperty(member,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (prop != null && prop.CanWrite)
			{
				try { prop.SetValue(target, value); return true; } catch { }
			}

			for (Type c = t; c != null; c = c.BaseType)
			{
				FieldInfo f = c.GetField(member,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					?? c.GetField($"<{member}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
				if (f != null)
				{
					try { f.SetValue(target, value); return true; } catch { }
				}
			}
			return false;
		}
	}

	/// <summary>
	/// Shared slot storage for the three container controllers.
	/// </summary>
	/// <remarks>
	/// Inventory, Equipment and Bank all satisfy <see cref="IItemContainer"/>, and the panels read
	/// them the same way: <c>Items</c> for the grid and <c>TryGetItem</c> per slot. Everything that
	/// mutates the container is implemented honestly rather than stubbed, because the panels call
	/// the query half of it while laying out.
	/// </remarks>
	public abstract class FakeContainer : IItemContainer, ICharacterBehaviour
	{
		public event Action<IItemContainer, Item, int> OnSlotUpdated;
		public event Action<IItemContainer, int, bool> OnSlotLockChanged;

		private readonly List<Item> items = new List<Item>();
		private readonly HashSet<int> locked = new HashSet<int>();

		protected FakeContainer(ICharacter character, int slots)
		{
			Character = character;
			for (int i = 0; i < slots; ++i) { items.Add(null); }
		}

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public List<Item> Items => items;

		public bool CanManipulate() => true;
		public bool IsValidSlot(int slot) => slot >= 0 && slot < items.Count;
		public bool IsSlotEmpty(int slot) => IsValidSlot(slot) && items[slot] == null;

		public bool TryGetItem(int slot, out Item item)
		{
			item = IsValidSlot(slot) ? items[slot] : null;
			return item != null;
		}

		public bool ContainsItem(BaseItemTemplate template) => GetItemCount(template) > 0;

		public int GetItemCount(BaseItemTemplate template)
		{
			int count = 0;
			for (int i = 0; i < items.Count; ++i)
			{
				if (items[i] == null || items[i].Template != template) { continue; }
					// Stackable is null for a non-stacking item; that is one of it, not none.
					count += items[i].Stackable != null ? (int)items[i].Stackable.Amount : 1;
			}
			return count;
		}

		public void AddSlots(List<Item> add, int amount)
		{
			for (int i = 0; i < amount; ++i) { items.Add(add != null && i < add.Count ? add[i] : null); }
		}

		public void Clear() { items.Clear(); }
		public bool HasFreeSlot() => FreeSlots() > 0;

		public int FreeSlots()
		{
			int free = 0;
			for (int i = 0; i < items.Count; ++i) { if (items[i] == null) { ++free; } }
			return free;
		}

		public int FilledSlots() => items.Count - FreeSlots();
		public bool CanAddItem(Item item) => HasFreeSlot();

		public bool TryAddItem(Item item, out List<Item> modified)
		{
			modified = new List<Item>();
			for (int i = 0; i < items.Count; ++i)
			{
				if (items[i] == null)
				{
					items[i] = item;
					modified.Add(item);
					OnSlotUpdated?.Invoke(this, item, i);
					return true;
				}
			}
			return false;
		}

		public bool SetItemSlot(Item item, int slot)
		{
			if (!IsValidSlot(slot)) { return false; }
			items[slot] = item;
			OnSlotUpdated?.Invoke(this, item, slot);
			return true;
		}

		public bool SwapItemSlots(int from, int to) => SwapItemSlots(from, to, out _, out _);

		public bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem)
		{
			fromItem = null;
			toItem = null;
			if (!IsValidSlot(from) || !IsValidSlot(to)) { return false; }
			fromItem = items[from];
			toItem = items[to];
			items[from] = toItem;
			items[to] = fromItem;
			return true;
		}

		public Item RemoveItem(int slot)
		{
			if (!IsValidSlot(slot)) { return null; }
			Item removed = items[slot];
			items[slot] = null;
			OnSlotUpdated?.Invoke(this, null, slot);
			return removed;
		}

		public bool IsSlotLocked(int slot) => locked.Contains(slot);

		public void LockSlot(int slot)
		{
			locked.Add(slot);
			OnSlotLockChanged?.Invoke(this, slot, true);
		}

		public void UnlockSlot(int slot)
		{
			locked.Remove(slot);
			OnSlotLockChanged?.Invoke(this, slot, false);
		}
	}

	public sealed class FakeInventory : FakeContainer, IInventoryController
	{
		public FakeInventory(ICharacter character) : base(character, 32) { }
		public void Activate(int index) { }
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory) => true;
	}

	public sealed class FakeBank : FakeContainer, IBankController
	{
		public FakeBank(ICharacter character) : base(character, 48) { }
		public long LastInteractableID { get; set; }
		public long Currency { get; set; } = 128450;
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory) => true;
	}

	public sealed class FakeEquipment : FakeContainer, IEquipmentController
	{
		public FakeEquipment(ICharacter character)
			: base(character, Enum.GetValues(typeof(ItemSlot)).Length) { }

		public event Action<Item, ItemSlot> OnItemEquipped;
		public event Action<Item, ItemSlot> OnItemUnequipped;

		public List<Trigger> OnEquipTriggers { get; } = new List<Trigger>();
		public List<Trigger> OnUnequipTriggers { get; } = new List<Trigger>();

		public void Activate(int index) { }

		/* The render rig has no server and no reconcile, so there is no request to remember. */
		public void NotifyEquipRequested(Item item, int inventoryIndex, InventoryType fromInventory, ItemSlot toSlot) { }
		public void NotifyUnequipRequested(ItemSlot slot, InventoryType toInventory) { }
		public void ClearPendingRequest(ItemSlot slot) { }

		public bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot)
		{
			bool ok = SetItemSlot(item, (int)toSlot);
			if (ok) { OnItemEquipped?.Invoke(item, toSlot); }
			return ok;
		}

		public bool Unequip(IItemContainer container, byte slot, out List<Item> modified)
		{
			modified = new List<Item>();
			Item removed = RemoveItem(slot);
			if (removed == null) { return false; }
			modified.Add(removed);
			OnItemUnequipped?.Invoke(removed, (ItemSlot)slot);
			return true;
		}
	}
}
