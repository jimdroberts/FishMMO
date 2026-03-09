using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Container interactable (chests, wardrobes, crates, etc.) that stores items and implements <see cref="IItemContainer"/>.
	/// Players can interact with it to view and take items.
	/// Configured via a <see cref="ContainerTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Container : Interactable, IContainer, IItemContainer
	{
		/// <summary>
		/// Template defining the container parameters.
		/// </summary>
		public ContainerTemplate Template;

		/// <summary>
		/// Achievement to increment when a player opens this container.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		ContainerTemplate IContainer.Template => Template;

		/// <inheritdoc />
		AchievementTemplate IContainer.AchievementTemplate => AchievementTemplate;

		private readonly List<Item> items = new List<Item>();
		private HashSet<int> lockedSlots;

		public event Action<IItemContainer, Item, int> OnSlotUpdated;

		public List<Item> Items { get { return items; } }

		private string title = "Container";

		public override string Title { get { return title; } }

		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.chocolate); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				if (!string.IsNullOrWhiteSpace(Template.Description))
				{
					title = Template.Description;
				}
				AddSlots(null, Template.SlotCount);
			}
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}
			return true;
		}

		public bool CanManipulate()
		{
			return Items.Count > 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValidSlot(int slot)
		{
			return slot > -1 &&
				   slot < Items.Count;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSlotEmpty(int slot)
		{
			return IsValidSlot(slot) &&
				   Items[slot] == null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetItem(int slot, out Item item)
		{
			if (IsValidSlot(slot))
			{
				item = Items[slot];
				return item != null;
			}
			item = null;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool ContainsItem(BaseItemTemplate itemTemplate)
		{
			for (int i = 0; i < Items.Count; ++i)
			{
				Item item = Items[i];
				if (item != null && item.Template.ID == itemTemplate.ID)
				{
					return true;
				}
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetItemCount(BaseItemTemplate itemTemplate)
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				Item item = Items[i];
				if (item != null && item.Template.ID == itemTemplate.ID)
				{
					if (item.IsStackable)
					{
						count += (int)item.Stackable.Amount;
					}
					else
					{
						count += 1;
					}
				}
			}
			return count;
		}

		public void AddSlots(List<Item> items, int amount)
		{
			if (items != null)
			{
				for (int i = 0; i < items.Count; ++i)
				{
					this.Items.Add(items[i]);
				}
				return;
			}
			for (int i = 0; i < amount; ++i)
			{
				this.Items.Add(null);
			}
		}

		public void Clear()
		{
			lockedSlots?.Clear();

			for (int i = 0; i < items.Count; ++i)
			{
				Item item = items[i];
				if (item == null)
				{
					continue;
				}
				item.Destroy();
				items[i] = null;
			}
		}

		public bool HasFreeSlot()
		{
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					return true;
				}
			}
			return false;
		}

		public int FreeSlots()
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					++count;
				}
			}
			return count;
		}

		public int FilledSlots()
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (!IsSlotEmpty(i))
				{
					++count;
				}
			}
			return count;
		}

		public bool CanAddItem(Item item)
		{
			if (item == null) return false;

			uint amountRemaining = item.IsStackable ? item.Stackable.Amount : 1;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					return true;
				}

				if (Items[i].IsStackable &&
					!Items[i].Stackable.IsStackFull &&
					Items[i].IsMatch(item))
				{
					uint remainingCapacity = Items[i].Template.MaxStackSize - Items[i].Stackable.Amount;
					amountRemaining = remainingCapacity.AbsoluteSubtract(amountRemaining);
				}

				if (amountRemaining < 1) return true;
			}
			return false;
		}

		public bool TryAddItem(Item item, out List<Item> modifiedItems)
		{
			modifiedItems = new List<Item>();

			if (!CanAddItem(item))
			{
				return false;
			}

			if (item.IsStackable)
			{
				uint amount = item.Stackable.Amount;
				for (int i = 0; i < Items.Count; ++i)
				{
					if (Items[i] != null &&
						Items[i].IsStackable &&
						Items[i].Stackable.AddToStack(item))
					{
						amount = item.Stackable.Amount;

						modifiedItems.Add(Items[i]);
						modifiedItems.Add(item);

						OnSlotUpdated?.Invoke(this, item, i);
					}

					if (amount < 1) return true;
				}
			}
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					SetItemSlot(item, i);

					modifiedItems.Add(item);

					return true;
				}
			}
			return false;
		}

		public bool SetItemSlot(Item item, int slot)
		{
			if (!IsValidSlot(slot))
			{
				return false;
			}

			Items[slot] = item;
			if (item != null)
			{
				item.Slot = slot;
			}
			OnSlotUpdated?.Invoke(this, item, slot);
			return true;
		}

		public bool SwapItemSlots(int from, int to)
		{
			return SwapItemSlots(from, to, out Item fromItem, out Item toItem);
		}

		public bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem)
		{
			if (from < 0 ||
				to < 0 ||
				from >= Items.Count ||
				to >= Items.Count ||
				IsSlotLocked(from) ||
				IsSlotLocked(to))
			{
				fromItem = null;
				toItem = null;

				return false;
			}

			fromItem = Items[from];
			toItem = Items[to];

			Items[from] = toItem;
			if (toItem != null)
			{
				toItem.Slot = from;
			}

			Items[to] = fromItem;
			if (fromItem != null)
			{
				fromItem.Slot = to;
			}

			OnSlotUpdated?.Invoke(this, toItem, from);
			OnSlotUpdated?.Invoke(this, fromItem, to);
			return true;
		}

		public Item RemoveItem(int slot)
		{
			if (!IsValidSlot(slot) ||
				IsSlotLocked(slot))
			{
				return null;
			}

			Item item = Items[slot];
			if (item == null)
			{
				return null;
			}
			item.Slot = -1;
			SetItemSlot(null, slot);
			return item;
		}

		/// <summary>
		/// Returns true if the specified slot is currently locked.
		/// Locked slots cannot be swapped, removed, or transferred until unlocked.
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is locked, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSlotLocked(int slot)
		{
			return lockedSlots != null && lockedSlots.Contains(slot);
		}

		/// <summary>
		/// Locks the specified slot, preventing it from being swapped, removed, or transferred.
		/// </summary>
		/// <param name="slot">The slot index to lock.</param>
		public void LockSlot(int slot)
		{
			if (!IsValidSlot(slot))
			{
				return;
			}

			if (lockedSlots == null)
			{
				lockedSlots = new HashSet<int>();
			}

			lockedSlots.Add(slot);
		}

		/// <summary>
		/// Unlocks the specified slot, allowing normal manipulation again.
		/// </summary>
		/// <param name="slot">The slot index to unlock.</param>
		public void UnlockSlot(int slot)
		{
			lockedSlots?.Remove(slot);
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			writer.WriteInt32(Items.Count);
			for (int i = 0; i < Items.Count; i++)
			{
				Item item = Items[i];
				if (item != null)
				{
					writer.WriteBoolean(true);
					writer.WriteInt32(item.Template.ID);
					writer.WriteUInt32(item.IsStackable ? item.Stackable.Amount : 1);
				}
				else
				{
					writer.WriteBoolean(false);
				}
			}
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);
			int slotCount = reader.ReadInt32();
			lockedSlots?.Clear();
			items.Clear();
			for (int i = 0; i < slotCount; i++)
			{
				bool hasItem = reader.ReadBoolean();
				if (hasItem)
				{
					int templateId = reader.ReadInt32();
					uint amount = reader.ReadUInt32();
					BaseItemTemplate itemTemplate = BaseItemTemplate.Get<BaseItemTemplate>(templateId);
					if (itemTemplate != null)
					{
						Item item = new Item(itemTemplate, amount);
						item.Slot = i;
						items.Add(item);
					}
					else
					{
						items.Add(null);
					}
				}
				else
				{
					items.Add(null);
				}
			}
		}
	}
}