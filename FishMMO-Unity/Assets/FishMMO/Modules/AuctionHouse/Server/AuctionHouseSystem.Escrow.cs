using System;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>Taking what a request costs out of a player first, and giving it back when the request fails.</summary>
	public partial class AuctionHouseSystem
	{
		/// <summary>What a request took out of a player, so it can be given back.</summary>
		internal struct Escrow
		{
			/// <summary>The item's template, or 0 for none.</summary>
			public int ItemTemplateID;
			/// <summary>The item's seed.</summary>
			public int ItemSeed;
			/// <summary>How many.</summary>
			public uint ItemAmount;
			/// <summary>Currency taken.</summary>
			public long Currency;
			/// <summary>The ledger reason the currency was taken under.</summary>
			public CurrencyMovementReason Reason;

			/// <summary>True when it holds anything.</summary>
			public bool HasAnything => (ItemTemplateID != 0 && ItemAmount > 0) || Currency > 0;
		}

		/// <summary>
		/// Takes currency out of a player's purse and records it in the ledger. Main thread.
		/// </summary>
		/// <returns>False, with nothing taken, when they cannot pay or the save could not be queued.</returns>
		private bool TryTakeCurrency(IPlayerCharacter character, long amount, CurrencyMovementReason reason, ref Escrow escrow)
		{
			if (amount <= 0)
			{
				return true;
			}
			if (currencyTemplate == null ||
				!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) ||
				balance < amount ||
				!CharacterCurrency.TrySpend(character, currencyTemplate, amount, () => TryPersistCharacterAttributes(character)))
			{
				return false;
			}
			escrow.Currency = amount;
			escrow.Reason = reason;
			RecordCurrencyMovement(character.ID, amount, reason, absorbed: true);
			return true;
		}

		/// <summary>
		/// Takes <paramref name="quantity"/> out of an inventory slot (the whole slot or part of its stack), saved through
		/// the inventory system's journalled batch and shown to the owner. Main thread.
		/// </summary>
		/// <returns>False, with nothing taken, when the slot no longer holds it.</returns>
		private bool TryTakeItem(NetworkConnection conn, IPlayerCharacter character, int slot, Item item, long quantity, ref Escrow escrow)
		{
			if (!character.TryGet(out IInventoryController inventory) || item?.Template == null || quantity < 1)
			{
				return false;
			}
			long available = item.IsStackable ? item.Stackable.Amount : 1;
			if (quantity > available)
			{
				return false;
			}

			int templateID = item.Template.ID;
			int seed = item.IsGenerated ? item.Generator.Seed : 0;

			if (quantity >= available)
			{
				Item removed = inventory.RemoveItem(slot);
				if (removed == null)
				{
					return false;
				}
				removed.Version++;
				PersistInventoryChanges(character, null, new[] { new RemovedItemRecord(removed.ID, removed.Version, slot) });
				Server.NetworkWrapper.Broadcast(conn, new InventoryRemoveItemBroadcast() { Slot = slot }, true, Channel.Reliable);
			}
			else
			{
				item.Stackable.Remove((uint)quantity);
				PersistInventoryChanges(character, new[] { item }, null);
				Server.NetworkWrapper.Broadcast(conn, new InventorySetItemBroadcast()
				{
					InstanceID = item.ID,
					TemplateID = item.Template.ID,
					Slot = item.Slot,
					Seed = seed,
					StackSize = item.Stackable.Amount,
				}, true, Channel.Reliable);
			}

			escrow.ItemTemplateID = templateID;
			escrow.ItemSeed = seed;
			escrow.ItemAmount = (uint)quantity;
			return true;
		}

		/// <summary>
		/// Gives an escrow back. Main thread.
		/// </summary>
		/// <remarks>
		/// Into the purse and bags when the player is still here and the bags have room; whatever cannot go there goes by
		/// mail. A player who logged off, or filled their bags, while their request was being answered still gets back
		/// what it took — nothing on this path is ever just lost.
		/// </remarks>
		private void Refund(NetworkConnection conn, long characterID, Escrow escrow, string why)
		{
			if (!escrow.HasAnything)
			{
				return;
			}
			IPlayerCharacter character = StillHere(conn, characterID);

			long mailCurrency = 0;
			if (escrow.Currency > 0)
			{
				if (character != null && currencyTemplate != null && CharacterCurrency.TryAdd(character, currencyTemplate, escrow.Currency))
				{
					if (!TryPersistCharacterAttributes(character))
					{
						Log.Error(LOG, $"Refund: currency persist rejected for CharID={characterID}; the in-memory balance is right, the next save carries it.");
					}
					RecordCurrencyMovement(characterID, escrow.Currency, escrow.Reason, absorbed: false);
				}
				else
				{
					mailCurrency = escrow.Currency;
				}
			}

			bool mailItem = false;
			if (escrow.ItemTemplateID != 0 && escrow.ItemAmount > 0)
			{
				mailItem = true;
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(escrow.ItemTemplateID);
				if (character != null && template != null &&
					character.TryGet(out IInventoryController inventory) &&
					Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
				{
					// The seed rides along: a generated item rebuilt from its template alone would reroll.
					Item restored = new Item(0, escrow.ItemSeed, template, escrow.ItemAmount);
					if (inventory.CanAddItem(restored) && inventorySystem.TryGrantItem(character, restored, InventoryType.Inventory))
					{
						mailItem = false;
					}
				}
			}

			if (mailCurrency > 0 || mailItem)
			{
				MailRefund(characterID, mailItem ? escrow : default, mailCurrency, escrow.Reason, why);
			}
		}

		/// <summary>Hands changed or removed inventory rows to the inventory system's journalled batch.</summary>
		private void PersistInventoryChanges(IPlayerCharacter character, System.Collections.Generic.IReadOnlyList<Item> changed, System.Collections.Generic.IReadOnlyList<RemovedItemRecord> removed)
		{
			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				Log.Error(LOG, $"PersistInventoryChanges: ICharacterInventorySystem not found; an inventory change for CharID={character?.ID} was not persisted.");
				return;
			}
			inventorySystem.PersistInventoryChanges(character, changed, removed);
		}

		/// <summary>Mails what could not be handed back in person.</summary>
		private void MailRefund(long characterID, Escrow item, long currency, CurrencyMovementReason reason, string why)
		{
			int templateID = item.ItemTemplateID;
			int seed = item.ItemSeed;
			uint amount = item.ItemAmount;
			int money = (int)Math.Min(int.MaxValue, Math.Max(0, currency));

			bool queued = EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out ICharacterMailService mail))
				{
					await Log.Error(LOG, $"Refund by mail: no mail service; CharID={characterID} lost template {templateID} x{amount} and {money} currency.");
					return;
				}
				DatabaseResult sent = await mail.SendAsync(characterID, AuctionRules.MailSenderName, characterID,
					"Auction refund", $"Your auction request could not be completed ({why}). Here is what it took.",
					templateID, seed, amount, money, 1);
				if (!sent.IsSuccess)
				{
					await Log.Error(LOG, $"Refund by mail failed for CharID={characterID} (template {templateID} x{amount}, {money} currency): [{sent.ErrorCode}] {sent.ErrorMessage}");
					return;
				}
				if (money > 0)
				{
					RecordCurrencyMovement(characterID, money, reason, absorbed: false);
				}
			}, characterID);
			if (!queued)
			{
				Log.Warning(LOG, $"Refund by mail for CharID={characterID} was queued behind a saturated worker.");
			}
		}
	}
}
