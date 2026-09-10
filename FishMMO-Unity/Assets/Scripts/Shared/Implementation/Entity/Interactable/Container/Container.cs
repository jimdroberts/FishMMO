using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;
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

		/// <summary>
		/// The slots this container holds.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Server state. It is deliberately NOT in the spawn payload, so no override of
		/// <c>WritePayload</c>/<c>ReadPayload</c> exists here and only <see cref="Interactable"/>'s
		/// scene-object ID travels.
		/// </para>
		/// <para>
		/// It used to: a presence bit plus a four-byte template ID and a four-byte stack amount per
		/// filled slot, written into the spawn payload of every chest, crate and wardrobe for EVERY
		/// connection that began observing it. Nothing on a client ever read it — the chest window is
		/// driven entirely by <c>ContainerOpenBroadcast</c>, which the server sends to the
		/// interacting player on open and re-sends to that player after every take — so the cost was
		/// pure, and it told every client in range what was inside every container in the world
		/// without anybody opening one. The copy was never refreshed after a take either, so what it
		/// told them went stale immediately. Corpse loot has always been scoped to registered
		/// viewers this way; containers now match.
		/// </para>
		/// </remarks>
		private readonly List<Item> items = new List<Item>();
		private HashSet<int> lockedSlots;

		public event Action<IItemContainer, Item, int> OnSlotUpdated;

		public event Action<IItemContainer, int, bool> OnSlotLockChanged;

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
				// A locked slot is mid-operation and is not available capacity — see the matching
				// guard in ItemContainer.CanAddItem.
				if (IsSlotLocked(i))
				{
					continue;
				}

				if (IsSlotEmpty(i))
				{
					return true;
				}

				if (Items[i].IsStackable &&
					!Items[i].Stackable.IsStackFull &&
					Items[i].IsMatch(item))
				{
					// Saturating — same reasoning as ItemContainer.CanAddItem. AbsoluteSubtract
					// returns |a - b|, so the ordinary "it all fits" case reported the UNUSED
					// capacity as the amount still outstanding and this container claimed to be
					// full when it was not, silently refusing loot.
					amountRemaining -= Math.Min(amountRemaining, Items[i].Stackable.RemainingCapacity);
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
						!IsSlotLocked(i) &&
						Items[i].IsStackable &&
						Items[i].Stackable.AddToStack(item))
					{
						amount = item.Stackable.Amount;

						// The donor is listed only once it has a slot of its own — see the
						// matching note in ItemContainer.TryAddItem — and the slot is repainted
						// from the stack that is now in it, not from the donor.
						modifiedItems.Add(Items[i]);

						OnSlotUpdated?.Invoke(this, Items[i], i);
					}

					if (amount < 1) return true;
				}
			}
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					// Checked, not presumed — SetItemSlot can refuse a locked slot.
					if (!SetItemSlot(item, i))
					{
						continue;
					}

					modifiedItems.Add(item);

					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Sets the item in the specified slot, refusing a locked slot.
		/// Callers must check the return value — see <see cref="ItemContainer.SetItemSlot"/>.
		/// </summary>
		public bool SetItemSlot(Item item, int slot)
		{
			if (!IsValidSlot(slot) ||
				IsSlotLocked(slot))
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

			if (lockedSlots.Add(slot))
			{
				OnSlotLockChanged?.Invoke(this, slot, true);
			}
		}

		/// <summary>
		/// Unlocks the specified slot, allowing normal manipulation again.
		/// </summary>
		/// <param name="slot">The slot index to unlock.</param>
		public void UnlockSlot(int slot)
		{
			if (lockedSlots != null && lockedSlots.Remove(slot))
			{
				OnSlotLockChanged?.Invoke(this, slot, false);
			}
		}

		/// <summary>
		/// Rolls this container's contents. Server only, once per spawn.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="OnStartServer"/> rather than <c>OnAwake</c>, for the same reason NPC ability
		/// learning and scene object registration live there: Awake runs once per pooled instance,
		/// so a container respawned from the pool would come back with whatever its previous life
		/// left behind — which, since a looted chest ends empty, means empty forever.
		/// <see cref="ResetState"/> clears and re-sizes the slots on the way into the pool and this
		/// fills them on the way out.
		/// </para>
		/// <para>
		/// The roll is server-side and the result reaches clients through
		/// <see cref="WritePayload"/>, which runs after this and before the spawn message is built.
		/// A client never rolls: it is told what is in the box.
		/// </para>
		/// </remarks>
		public override void OnStartServer()
		{
			base.OnStartServer();

			RollContents();
		}

		/// <summary>
		/// Fills this container's slots from its template's loot table.
		/// </summary>
		private void RollContents()
		{
			if (Template == null || Template.LootTable == null)
			{
				return;
			}

			// Slots are authored by OnAwake and re-created by ResetState; if neither has run for
			// this instance yet there is nowhere to put anything.
			if (items.Count < 1)
			{
				AddSlots(null, Template.SlotCount);
			}

			List<Item> rolled = new List<Item>();
			Template.LootTable.Roll(DeterministicRNG.Shared, rolled, out int _);

			for (int i = 0; i < rolled.Count; ++i)
			{
				Item item = rolled[i];
				if (item == null)
				{
					continue;
				}

				/* Placed into the first free slot rather than by index, so a table that rolls more
				 * entries than the container has slots simply fills it and stops. Dropping the
				 * remainder is the right failure: the alternative is either an item that exists in
				 * no container, or a silent write past the end of the slot list. */
				if (!TryPlaceInFreeSlot(item))
				{
					break;
				}
			}
		}

		/// <summary>
		/// Puts an item in the first empty slot.
		/// </summary>
		/// <param name="item">The item to place.</param>
		/// <returns>True when a slot was found and the item placed.</returns>
		private bool TryPlaceInFreeSlot(Item item)
		{
			for (int i = 0; i < items.Count; ++i)
			{
				if (items[i] != null)
				{
					continue;
				}
				if (SetItemSlot(item, i))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Rebuilds the slot list when this instance returns to the pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="OnAwake"/> is what sizes the container, and Unity calls Awake once per
		/// instance rather than once per spawn — so a recycled container came back holding
		/// whatever its previous life had left in it, and a chest emptied by one player would
		/// respawn already empty for the next.
		/// </para>
		/// <para>
		/// The slots are re-added rather than merely emptied, because a container whose template
		/// was assigned after Awake would otherwise come back with no slots at all.
		/// </para>
		/// </remarks>
		/// <param name="asServer">True when the reset is for the server instance.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Clear();
			items.Clear();
			lockedSlots?.Clear();

			if (Template != null)
			{
				AddSlots(null, Template.SlotCount);
			}
		}
	}
}