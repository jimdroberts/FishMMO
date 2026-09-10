using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Merchant purchase handling: item purchases, ability-template and ability-event learning, and
	/// premade-ability purchases from merchant NPCs.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles a <see cref="MerchantPurchaseBroadcast"/> from a client. Validates the merchant interactable,
		/// checks sufficient currency, and processes the purchase (item, ability, or ability event) based on the tab type.
		/// </summary>
		/// <summary>Answers a purchase request. Every exit from the handler goes through here.</summary>
		/// <remarks>
		/// The client arms a watchdog when it submits a purchase and has nothing else to clear it.
		/// A bare return therefore does not read to the player as a refusal — it reads as the
		/// server never having answered, which is a different problem with a different remedy.
		/// </remarks>
		private static void SendPurchaseResult(NetworkConnection conn, MerchantPurchaseBroadcast msg,
			MerchantPurchaseFailure failure, int quantity = 0, long charged = 0)
		{
			if (conn == null)
			{
				return;
			}

			conn.Broadcast(new MerchantPurchaseResultBroadcast()
			{
				Index = msg.Index,
				Type = msg.Type,
				Success = failure == MerchantPurchaseFailure.None,
				Failure = failure,
				Quantity = quantity,
				Charged = charged,
			});
		}

		private void OnServerMerchantPurchaseBroadcastReceived(NetworkConnection conn, MerchantPurchaseBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();

			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController) ||
				!CharacterStateValidation.CanAct(character))
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
				return;
			}

			try
			{
				// Validate the scene the character is actually in — see CurrentSceneName.
				string currentScene = character.CurrentSceneName();
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(currentScene, out _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + currentScene);
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
					return;
				}

				/* Resolve through the shared rule and ask CanInteract, not GetComponent + InRange.
				 *
				 * Two things were wrong with the old pair. GetComponent returns whichever
				 * IInteractable the component order happens to yield, and a merchant NPC carries
				 * two — the Merchant and the NPC that is its own lootable corpse — so which one
				 * answered a purchase was decided by the order somebody happened to add components
				 * to the prefab.
				 *
				 * More seriously, InRange is not CanInteract. CanInteract is where the corpse gate
				 * lives, and skipping it meant a player could kill a merchant and then keep
				 * trading with the body: opening the shop was refused, but nothing requires the
				 * shop to have been opened before a MerchantPurchaseBroadcast is accepted, so a
				 * client could simply send one. */
				IInteractable interactable = InteractableResolver.Resolve(sceneObject);
				IMerchant merchant = interactable as IMerchant;
				if (merchant == null ||
					merchant.Template == null ||
					!interactable.CanInteract(character))
				{
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
					return;
				}

				/* The merchant's own template, not one the client named.
				 *
				 * The request used to carry a template id, which this method resolved and then
				 * compared against the template on the merchant it had just resolved for itself.
				 * The comparison could only ever refuse a client that named the WRONG merchant —
				 * one that named the right one was still held to this object — so it guarded
				 * nothing that the resolve above does not already guard, and it put a second,
				 * client-supplied identity for the same shop on the wire. The guards are the two
				 * lines above: InteractableResolver.Resolve, which picks the merchant out of an
				 * object that may carry several interactables, and CanInteract, which is where the
				 * range and corpse gates live. */
				MerchantTemplate merchantTemplate = merchant.Template;

				switch (msg.Type)
				{
					case MerchantTabType.Item:
						if (!TryPurchaseItem(conn, character, inventoryController, merchantTemplate, msg))
						{
							return;
						}
						break;
					/* The learn helpers report their own outcome rather than the caller assuming one.
					 * They refuse for half a dozen reasons — already known, no ability controller,
					 * no currency, an async worker that would not take the persist — and every one
					 * of them used to be answered with "bought", because the result was sent
					 * unconditionally after the call. The player was told the purchase succeeded,
					 * saw no ability, and had no way to tell which of the two was wrong. */
					case MerchantTabType.Ability:
					{
						if (merchantTemplate.Abilities == null ||
							msg.Index < 0 ||
							msg.Index >= merchantTemplate.Abilities.Count)
						{
							SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InvalidEntry);
							return;
						}

						MerchantPurchaseFailure failure = LearnAbilityTemplate(conn, character, merchantTemplate.Abilities[msg.Index]);
						SendPurchaseResult(conn, msg, failure, failure == MerchantPurchaseFailure.None ? 1 : 0);
						if (failure != MerchantPurchaseFailure.None)
						{
							return;
						}
						break;
					}
					case MerchantTabType.AbilityEvent:
					{
						if (merchantTemplate.AbilityEvents == null ||
							msg.Index < 0 ||
							msg.Index >= merchantTemplate.AbilityEvents.Count)
						{
							SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InvalidEntry);
							return;
						}

						MerchantPurchaseFailure failure = LearnAbilityEvent(conn, character, merchantTemplate.AbilityEvents[msg.Index]);
						SendPurchaseResult(conn, msg, failure, failure == MerchantPurchaseFailure.None ? 1 : 0);
						if (failure != MerchantPurchaseFailure.None)
						{
							return;
						}
						break;
					}
					case MerchantTabType.PremadeAbility:
						if (!TryPurchasePremadeAbility(conn, character, merchantTemplate, msg))
						{
							return;
						}
						break;
					default:
						SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InvalidEntry);
						return;
				}

				// Increment achievement for any merchant interaction
				if (merchant != null &&
					merchant.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(merchant.AchievementTemplate, 1);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Buys one merchant item entry for the requesting character.
		/// </summary>
		/// <param name="conn">The buyer's connection.</param>
		/// <param name="character">The buying character.</param>
		/// <param name="inventoryController">The buyer's inventory.</param>
		/// <param name="merchantTemplate">The merchant's template, already validated against the live merchant.</param>
		/// <param name="msg">The purchase request.</param>
		/// <returns>True when the purchase completed.</returns>
		/// <remarks>
		/// <para><b>Nothing about price comes from the client.</b> The request carries an index
		/// into the merchant's own item list and a quantity; the unit price is read from the
		/// template that index resolves to, and the total is multiplied here. The quantity is
		/// clamped to one stack and then to what the character can actually pay for, so an
		/// oversized request is trimmed rather than refused.</para>
		///
		/// <para><b>Affordability is checked against <c>Value</c>, not <c>FinalValue</c>.</b>
		/// <c>AddValue</c> writes the base value, while <c>FinalValue</c> is the base plus every
		/// modifier in force. Testing one and writing the other meant a character carrying any
		/// currency-boosting buff could buy against currency it did not have and end up with a
		/// negative balance — and because the check used the larger number, the shortfall was
		/// exactly the size of the buff.</para>
		///
		/// <para><b>The currency is deducted and enqueued for persistence before the item is
		/// granted.</b> The previous order granted the item first and then bailed out with a
		/// <c>break</c> if the persist could not be enqueued, which left the player holding the
		/// item and still holding the money. It also enqueued the persist <em>before</em> the
		/// in-memory deduction, and <see cref="TryPersistMerchantAttributes"/> snapshots the
		/// current in-memory values — so the row it wrote was the pre-purchase balance and the
		/// deduction never reached the database at all. Both are fixed by deducting first,
		/// snapshotting the deducted value, and refunding if any later step fails.</para>
		///
		/// <para>The remaining window — a crash after the deduction is enqueued and before the
		/// item persist is — charges the player for an item they do not receive. That is the
		/// correct direction for an authoritative server to fail: a transient loss the player can
		/// report, rather than currency created from nothing.</para>
		/// </remarks>
		private bool TryPurchaseItem(
			NetworkConnection conn,
			IPlayerCharacter character,
			IInventoryController inventoryController,
			MerchantTemplate merchantTemplate,
			MerchantPurchaseBroadcast msg)
		{
			if (merchantTemplate.Items == null ||
				msg.Index < 0 ||
				msg.Index >= merchantTemplate.Items.Count)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InvalidEntry);
				return false;
			}

			BaseItemTemplate itemTemplate = merchantTemplate.Items[msg.Index];
			if (itemTemplate == null || itemTemplate.Price < 0)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.NotForSale);
				return false;
			}

			/* Price 0 means free, not "unpriced". It used to mean unpriced, and since Price is an
			 * int that defaults to 0 that made every unedited item template unsellable — which is
			 * every item the shipped merchants offer, so the first thing a player met was a
			 * refusal. A merchant offering something is the authoring act; the price is how much
			 * it costs, and zero is a legitimate amount. Only a NEGATIVE price is nonsense, and
			 * that is what is refused above. */
			long unitPrice = itemTemplate.Price;

			/* The whole currency lookup is skipped for a free item. Nothing is charged, so a
			 * character that somehow has no currency attribute at all still gets the goods rather
			 * than a bare "Unavailable" it can do nothing about. */
			long balance = 0;
			if (unitPrice > 0)
			{
				if (currencyTemplate == null)
				{
					Log.Debug("InteractableSystem", "currencyTemplate is null.");
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
					return false;
				}
				/* Balance, not the attribute. CharacterCurrency reads the BASE value, which is the
				 * distinction this path has to get right: FinalValue includes every modifier in
				 * force, and spending against it lets a currency buff be spent as money. */
				if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out balance))
				{
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
					return false;
				}
			}

			// One stack at most, so a purchase can always be represented as a single Item.
			long maxStack = itemTemplate.MaxStackSize > 0 ? itemTemplate.MaxStackSize : 1;
			long requested = msg.Quantity <= 0 ? 1 : msg.Quantity;
			long quantity = Math.Min(requested, maxStack);

			/* Long arithmetic throughout. Price and quantity are both ints, and a client asking
			 * for int.MaxValue of a costly item would overflow the product into a negative
			 * "total" that every affordability test passes.
			 *
			 * Guarded on unitPrice > 0 for the obvious reason as well as the design one: dividing
			 * by a free item's price is a DivideByZeroException, which would take the handler down
			 * mid-request and answer nobody. */
			if (unitPrice > 0)
			{
				long affordable = balance / unitPrice;
				quantity = Math.Min(quantity, affordable);
			}
			if (quantity < 1)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InsufficientFunds);
				return false;
			}

			long total = quantity * unitPrice;
			if (total > int.MaxValue || balance < total)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InsufficientFunds);
				return false;
			}

			int charge = (int)total;

			/* Deduct, persist, and refund if the write is refused — TrySpend owns that ordering
			 * so it cannot drift from the other paths that do the same thing.
			 *
			 * Skipped entirely at charge 0. TrySpend rejects a non-positive amount by design (so a
			 * sign error cannot quietly take money), so calling it for a free item would report
			 * "insufficient funds" for something that costs nothing. There is also nothing to
			 * persist: no balance changed. */
			if (charge > 0 &&
				!CharacterCurrency.TrySpend(character, currencyTemplate, charge, () => TryPersistMerchantAttributes(character)))
			{
				Log.Warning("InteractableSystem", $"TryPurchaseItem: charge of {charge} refused for CharID={character.ID}.");
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InsufficientFunds);
				return false;
			}

			Item newItem = new Item(itemTemplate, (uint)quantity);
			if (!SendNewItemBroadcast(conn, character, inventoryController, newItem))
			{
				/* No room, or the add was refused. Nothing was granted, so put the money back and
				 * persist the refund — the deduction has already been enqueued and would otherwise
				 * be the only half of the transaction the database ever sees.
				 *
				 * Nothing was deducted for a free item, so there is nothing to give back; TryAdd
				 * rejects a non-positive amount anyway, and the persist would write an unchanged
				 * balance. */
				if (charge > 0)
				{
					CharacterCurrency.TryAdd(character, currencyTemplate, charge);
					if (!TryPersistMerchantAttributes(character))
					{
						Log.Error("InteractableSystem", $"TryPurchaseItem: refund persist rejected for CharID={character.ID}; in-memory balance is correct but the DB holds the deduction.");
					}
					RecordCurrencyMovement(character.ID, charge, CurrencyMovementReason.MerchantPurchase, absorbed: false);
				}
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.NoRoom);
				return false;
			}

			RecordCurrencyMovement(character.ID, charge, CurrencyMovementReason.MerchantPurchase, absorbed: true);
			SendPurchaseResult(conn, msg, MerchantPurchaseFailure.None, (int)quantity, charge);
			return true;
		}

		/// <summary>
		/// Handles a <see cref="MerchantSellBroadcast"/>: sells an inventory slot to a merchant.
		/// </summary>
		/// <remarks>
		/// The mirror image of <see cref="TryPurchaseItem"/>, and authoritative in the same way.
		/// The request names an inventory slot and a quantity; the server resolves the item in
		/// that slot itself, takes the unit price from that item's own template, and applies the
		/// merchant template's <see cref="MerchantTemplate.SellPriceMultiplier"/>. No identity and
		/// no value travels from the client.
		/// <para>
		/// The ordering is the reverse of a purchase, for the same reason: the item is removed and
		/// its removal enqueued before the currency is granted, so the failure direction is a lost
		/// payout rather than an item that was sold and kept.
		/// </para>
		/// <para>
		/// Every exit sends a <see cref="MerchantSellResultBroadcast"/>. The client disables the
		/// sell control while a request is outstanding — the double-submit guard that stops one
		/// mis-timed double click selling a stack twice — and a handler that returned silently
		/// would leave that control disabled for good.
		/// </para>
		/// </remarks>
		private void OnServerMerchantSellBroadcastReceived(NetworkConnection conn, MerchantSellBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController) ||
				!CharacterStateValidation.CanAct(character))
			{
				SendSellResult(conn, msg.Slot, false, 0, 0);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				SendSellResult(conn, msg.Slot, false, 0, 0);
				return;
			}

			bool succeeded = false;
			int soldQuantity = 0;
			int payout = 0;

			try
			{
				// Validate the scene the character is actually in — see CurrentSceneName.
				string currentScene = character.CurrentSceneName();
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(currentScene, out _))
				{
					return;
				}

				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				// Resolved and gated exactly as the purchase path is — see the note there. A dead
				// merchant does not buy either.
				IInteractable interactable = InteractableResolver.Resolve(sceneObject);
				IMerchant merchant = interactable as IMerchant;
				MerchantTemplate merchantTemplate = merchant?.Template;
				if (merchantTemplate == null ||
					!merchantTemplate.BuysItems ||
					!interactable.CanInteract(character))
				{
					return;
				}

				if (!inventoryController.IsValidSlot(msg.Slot) ||
					inventoryController.IsSlotLocked(msg.Slot) ||
					!inventoryController.TryGetItem(msg.Slot, out Item item) ||
					item == null ||
					item.Template == null)
				{
					return;
				}

				/* Checked before the item is removed: a character with no currency attribute
				 * cannot be paid, and finding that out afterwards would take the item and give
				 * nothing back. TryGetBalance answers presence as well as value. */
				if (currencyTemplate == null ||
					!CharacterCurrency.TryGetBalance(character, currencyTemplate, out _))
				{
					return;
				}

				long available = item.IsStackable ? item.Stackable.Amount : 1;
				long requested = msg.Quantity <= 0 ? available : msg.Quantity;
				long quantity = Math.Min(requested, available);
				if (quantity < 1)
				{
					return;
				}

				/* Unit payout is floored before multiplying, so selling ten singles and selling a
				 * stack of ten pay the same — a per-total round would otherwise make one of the
				 * two strictly better and turn the difference into a grind. */
				long unitPayout = (long)Math.Floor(item.Template.Price * (double)merchantTemplate.SellPriceMultiplier);
				if (unitPayout < 0)
				{
					unitPayout = 0;
				}

				long total = unitPayout * quantity;
				if (total > int.MaxValue)
				{
					total = int.MaxValue;
				}

				// Remove first. A partial sale leaves the stack behind with a reduced amount.
				long characterID = character.ID;
				bool wholeSlot = quantity >= available;
				if (wholeSlot)
				{
					Item removed = inventoryController.RemoveItem(msg.Slot);
					if (removed == null)
					{
						return;
					}

					removed.Version++;
					int slot = msg.Slot;
					/* Through the journalled batch, addressed by the item. A delete issued around the
					 * journal could commit after a snapshot captured while the item still existed,
					 * and the snapshot would put the row back — the item returned on the next login
					 * while the payout stayed. The batch is never dropped, so there is no failure to
					 * undo here. */
					PersistInventoryChanges(character, null, new[] { new RemovedItemRecord(removed.ID, removed.Version, slot) });

					Server.NetworkWrapper.Broadcast(conn, new InventoryRemoveItemBroadcast()
					{
						Slot = slot,
					}, true, Channel.Reliable);
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
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.Stackable.Amount,
					}, true, Channel.Reliable);
				}

				payout = (int)total;
				if (payout > 0)
				{
					CharacterCurrency.TryAdd(character, currencyTemplate, payout);
					if (!TryPersistMerchantAttributes(character))
					{
						Log.Error("InteractableSystem", $"MerchantSell: payout persist rejected for CharID={characterID}; the item removal is recorded but the payout is not.");
					}
				}

				soldQuantity = (int)quantity;
				succeeded = true;

				// Increment achievement for any merchant interaction.
				if (merchant.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(merchant.AchievementTemplate, 1);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
				SendSellResult(conn, msg.Slot, succeeded, soldQuantity, payout);
			}
		}

		/// <summary>
		/// Sends the single reply every exit from the sell handler owes the client.
		/// </summary>
		private void SendSellResult(NetworkConnection conn, int slot, bool success, int quantity, int payout)
		{
			Server.NetworkWrapper.Broadcast(conn, new MerchantSellResultBroadcast()
			{
				Slot = slot,
				Success = success,
				Quantity = quantity,
				Payout = payout,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sells one complete, premade ability: the finished <see cref="Ability"/> a crafter would
		/// have produced from the offer's recipe, granted straight into the buyer's usable set.
		/// </summary>
		/// <param name="conn">The buyer's connection.</param>
		/// <param name="character">The buying character.</param>
		/// <param name="merchantTemplate">The merchant's template, already validated against the live merchant.</param>
		/// <param name="msg">The purchase request.</param>
		/// <returns>True when the purchase completed. Every exit answers the client.</returns>
		/// <remarks>
		/// <para><b>This is the crafting path with the recipe supplied by content.</b> The
		/// request names an index into the merchant's own list; the template, the events and the
		/// price are all read from the server's copy of that asset, so nothing about what is
		/// granted or what it costs comes from the client. The recipe is held to the same rules
		/// the crafter enforces on a player — <see cref="PremadeAbilityTemplate.Validate"/> —
		/// so a premade ability is never something a player could not have built.</para>
		///
		/// <para><b>Not gated on knowing the template.</b> That is the point of the offer: a player
		/// buys the finished ability instead of the template, the effects and a trip to the
		/// crafter. Nothing downstream needs the template known either — activation gates on
		/// <c>KnownAbilities</c>, the hotkey system validates against instance IDs, and the load
		/// path constructs abilities from their rows without consulting the known-template
		/// table.</para>
		///
		/// <para><b>Same ordering as crafting: charge, persist, then grant, and refund if the
		/// grant fails.</b> <c>TrySpend</c> owns the deduct-persist-refund sequence; a
		/// <see cref="LearnAbility"/> that returns null (the async worker refused the persist)
		/// puts the money back and records the refund, exactly as the craft handler does.</para>
		/// </remarks>
		private bool TryPurchasePremadeAbility(
			NetworkConnection conn,
			IPlayerCharacter character,
			MerchantTemplate merchantTemplate,
			MerchantPurchaseBroadcast msg)
		{
			if (merchantTemplate.PremadeAbilities == null ||
				msg.Index < 0 ||
				msg.Index >= merchantTemplate.PremadeAbilities.Count)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InvalidEntry);
				return false;
			}

			PremadeAbilityTemplate offer = merchantTemplate.PremadeAbilities[msg.Index];
			if (offer == null)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InvalidEntry);
				return false;
			}

			/* A recipe the crafter would refuse is not sold. Logged as a warning because it is a
			 * content fault — the asset's own OnValidate says the same thing in the inspector —
			 * and answered as NotForSale because, from the player's side, that is what it is. */
			if (!offer.Validate(out string reason))
			{
				Log.Warning("InteractableSystem", $"TryPurchasePremadeAbility: '{offer.AssetName}' on merchant {merchantTemplate.Name} is not sellable: {reason}");
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.NotForSale);
				return false;
			}

			AbilityTemplate template = offer.Ability;

			if (!character.TryGet(out IAbilityController abilityController))
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
				return false;
			}

			/* One usable ability per template, the rule the crafter applies. The premade route
			 * must not be a way around it — and without this, a player could buy the same offer
			 * repeatedly and fill their ability list with copies. */
			if (abilityController.KnowsLearnedAbility(template.ID))
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.AlreadyKnown);
				return false;
			}

			if (abilityController.KnownAbilities.Count >= maxAbilityCount)
			{
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.AbilityLimit);
				return false;
			}

			List<int> eventIDs = offer.BuildEventIDs();

			/* Content is trusted for WHICH events, but every id must still resolve on this
			 * server. Ability.Initialize silently skips an id it cannot resolve, which would sell
			 * — and persist — a weaker ability than the one on offer without anyone noticing. */
			for (int i = 0; i < eventIDs.Count; ++i)
			{
				int id = eventIDs[i];
				if (AbilityEvent.Get<AbilityEvent>(id) == null &&
					!(BaseAbilityTemplate.Get<BaseAbilityTemplate>(id) is AbilityTypeOverrideEventType))
				{
					Log.Warning("InteractableSystem", $"TryPurchasePremadeAbility: '{offer.AssetName}' names event {id}, which this server does not have loaded.");
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.NotForSale);
					return false;
				}
			}

			int price = offer.Price;
			if (price > 0)
			{
				if (currencyTemplate == null)
				{
					Log.Debug("InteractableSystem", "currencyTemplate is null.");
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
					return false;
				}
				// Base value, not FinalValue — see TryPurchaseItem.
				if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) ||
					balance < price)
				{
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InsufficientFunds);
					return false;
				}

				// Deduct, persist, refund on a refused write — TrySpend owns that ordering.
				if (!CharacterCurrency.TrySpend(character, currencyTemplate, price, () => TryPersistMerchantAttributes(character)))
				{
					Log.Warning("InteractableSystem", $"TryPurchasePremadeAbility: charge of {price} refused for CharID={character.ID}.");
					SendPurchaseResult(conn, msg, MerchantPurchaseFailure.InsufficientFunds);
					return false;
				}
			}

			Ability newAbility = LearnAbility(abilityController, template, eventIDs);
			if (newAbility == null)
			{
				// Nothing was learned, so put the money back and record the refund.
				if (price > 0)
				{
					CharacterCurrency.TryAdd(character, currencyTemplate, price);
					if (!TryPersistMerchantAttributes(character))
					{
						Log.Error("InteractableSystem", $"TryPurchasePremadeAbility: refund persist rejected for CharID={character.ID}; in-memory balance is correct but the DB holds the deduction.");
					}
					RecordCurrencyMovement(character.ID, price, CurrencyMovementReason.AbilityPurchase, absorbed: false);
				}
				SendPurchaseResult(conn, msg, MerchantPurchaseFailure.Unavailable);
				return false;
			}

			if (price > 0)
			{
				RecordCurrencyMovement(character.ID, price, CurrencyMovementReason.AbilityPurchase, absorbed: true);
			}

			int[] events = eventIDs.ToArray();

			Server.NetworkWrapper.Broadcast(conn, new AbilityAddBroadcast()
			{
				ID = newAbility.ID,
				TemplateID = newAbility.Template.ID,
				Events = events,
			}, true, Channel.Reliable);

			/* Observers too, for the reason the craft handler gives: their copy of this
			 * character's abilities was written when they started observing, and a cast of an
			 * ability they were never told about draws nothing on their screen. */
			ObserverBroadcastScope.BroadcastToObserversExceptOwner(character.NetworkObject, new AbilityLearnedObserverBroadcast()
			{
				CasterObjectID = character.NetworkObject.ObjectId,
				AbilityID = newAbility.ID,
				TemplateID = newAbility.Template.ID,
				Events = events,
			}, Channel.Reliable);

			SendPurchaseResult(conn, msg, MerchantPurchaseFailure.None, 1, price);
			return true;
		}

		/// <summary>
		/// Generic helper that validates a character can learn an ability or event, checks currency, persists
		/// the known ability asynchronously, applies the learned state, deducts currency, and notifies the client.
		/// </summary>
		/// <typeparam name="TTemplate">The ability or event template type.</typeparam>
		/// <typeparam name="TBroadcast">The broadcast type for notifying the client.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character purchasing the ability or event.</param>
		/// <param name="template">The template to learn.</param>
		/// <param name="knowsFunc">Function to check if the character already knows the template.</param>
		/// <param name="learnFunc">Function to apply the learned template to the ability controller.</param>
		/// <param name="idSelector">Function to extract the template ID.</param>
		/// <param name="priceSelector">Function to extract the template price.</param>
		/// <param name="broadcastFactory">Function to create the broadcast for the client.</param>
		/// <returns>
		/// <see cref="MerchantPurchaseFailure.None"/> when the template was learned, otherwise why
		/// it was not. Returned rather than logged and swallowed: the caller answers a purchase
		/// request with it, and a refusal the player is told about as a success is worse than the
		/// refusal.
		/// </returns>
		private MerchantPurchaseFailure LearnAbilityGeneric<TTemplate, TBroadcast>(
			NetworkConnection conn,
			IPlayerCharacter character,
			TTemplate template,
			Func<IAbilityController, int, bool> knowsFunc,
			Action<IAbilityController, List<TTemplate>> learnFunc,
			Func<TTemplate, int> idSelector,
			Func<TTemplate, int> priceSelector,
			Func<TTemplate, TBroadcast> broadcastFactory)
			where TTemplate : class
			where TBroadcast : struct, IBroadcast
		{
			if (template == null)
			{
				return MerchantPurchaseFailure.InvalidEntry;
			}
			if (character == null || !character.TryGet(out IAbilityController abilityController))
			{
				return MerchantPurchaseFailure.Unavailable;
			}
			if (knowsFunc(abilityController, idSelector(template)))
			{
				return MerchantPurchaseFailure.AlreadyKnown;
			}

			/* Same rule as the item path: a price of 0 is free, and a negative one is nonsense.
			 * Every ability template currently ships at 0, so this branch is the one a player
			 * actually meets. */
			int price = priceSelector(template);
			if (price < 0)
			{
				return MerchantPurchaseFailure.NotForSale;
			}

			if (price > 0)
			{
				if (currencyTemplate == null)
				{
					Log.Debug("InteractableSystem", "currencyTemplate is null.");
					return MerchantPurchaseFailure.Unavailable;
				}
				/* CharacterCurrency reads the BASE value. FinalValue is the base plus every modifier
				 * in force, and testing one while writing the other let a character with a
				 * currency-boosting buff spend money it did not have — the same defect the item
				 * purchase path had. Reading the balance explicitly rather than calling CanAfford
				 * keeps this honest about what the deduction below will actually be able to take. */
				if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) ||
					balance < price)
				{
					Log.Debug("InteractableSystem", "Not enough currency!");
					return MerchantPurchaseFailure.InsufficientFunds;
				}
			}

			long charID = character.ID;
			int templateID = idSelector(template);
			if (!TryEnqueueAsyncWork(() => PersistKnownAbilityAsync(charID, templateID), charID))
			{
				Log.Warning("InteractableSystem", $"LearnAbilityGeneric: Async worker rejected known-ability persist for CharID={charID}, TemplateID={templateID}.");
				return MerchantPurchaseFailure.Unavailable;
			}

			// RISK: The known-ability persist is enqueued as fire-and-forget. The in-memory
			// ability learn and currency deduction below happen before the DB write completes.
			// If the server crashes after the in-memory changes but before the DB persist,
			// neither the ability nor the currency change is reflected in the DB - both are
			// restored on restart, resulting in a net-neutral outcome, not an exploit.
			// The Item purchase case (TryPersistMerchantAttributes) avoids this class of risk
			// entirely by persisting the currency BEFORE in-memory changes.

			// learn the ability or event
			learnFunc(abilityController, new List<TTemplate> { template });

			/* Deducted without a persistence callback, deliberately. TrySpend would refund a
			 * refused write, and this path must not: the ability has already been granted above,
			 * so undoing the charge would hand it over for free. The warning below records the
			 * in-memory-only deduction instead, which is the trade-off the RISK note describes.
			 *
			 * Skipped for a free ability. Nothing is charged and nothing changed, so there is no
			 * attribute snapshot worth writing and no ledger row to record. */
			bool currencyPersisted = false;
			if (price > 0)
			{
				CharacterCurrency.TrySpend(character, currencyTemplate, price);

				/* Enqueued AFTER the deduction, because TryPersistMerchantAttributes snapshots the
				 * in-memory values as they stand when it is called. Enqueuing it first — which the
				 * item purchase path used to do — writes the pre-purchase balance and loses the
				 * deduction entirely. */
				currencyPersisted = TryPersistMerchantAttributes(character);
				if (!currencyPersisted)
				{
					Log.Warning("InteractableSystem", $"LearnAbilityGeneric: currency persist rejected for CharID={charID}; the deduction is in memory only until the next character save.");
				}
			}

			/* Recorded only once the deduction is enqueued, and never when it was refused. This
			 * path is the one place the ledger can disagree with the balance: the deduction here
			 * is in-memory-first by design (see the RISK note above), so a refused persist means
			 * the money is not gone and there is no movement to record. Recording it anyway would
			 * put a charge in the ledger for currency the database still shows the player
			 * holding. */
			if (currencyPersisted)
			{
				RecordCurrencyMovement(charID, price, CurrencyMovementReason.AbilityLearn, absorbed: true);
			}

			// tell the client about the new ability/event
			Server.NetworkWrapper.Broadcast(conn, broadcastFactory(template), true, Channel.Reliable);
			return MerchantPurchaseFailure.None;
		}

		/// <summary>
		/// Persists a known ability or event to the database asynchronously.
		/// </summary>
		private async Task PersistKnownAbilityAsync(long characterID, int templateID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
				{
					return;
				}

				DatabaseResult result = await knownAbilityService.PersistAsync(characterID, templateID, 1);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"PersistKnownAbilityAsync DB error (CharID={characterID}, TemplateID={templateID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error persisting known ability: {ex}");
			}
		}

		/// <summary>
		/// Learns a base ability template and synchronizes the result to the client.
		/// </summary>
		/// <typeparam name="T">Concrete base ability template type.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character learning the ability.</param>
		/// <param name="template">Ability template to learn.</param>
		/// <returns>Why the learn was refused, or <see cref="MerchantPurchaseFailure.None"/>.</returns>
		public MerchantPurchaseFailure LearnAbilityTemplate<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : BaseAbilityTemplate
		{
			return LearnAbilityGeneric<BaseAbilityTemplate, KnownAbilityAddBroadcast>(
				conn,
				character,
				template,
				(abilityController, id) => abilityController.KnowsAbility(id),
				(abilityController, list) => abilityController.LearnBaseAbilities(list.Cast<BaseAbilityTemplate>().ToList()),
				t => t.ID,
				t => t.Price,
				t => new KnownAbilityAddBroadcast { TemplateID = t.ID }
			);
		}

		/// <summary>
		/// Learns an ability event template and synchronizes the result to the client.
		/// </summary>
		/// <typeparam name="T">Concrete ability event type.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character learning the ability event.</param>
		/// <param name="template">Ability event template to learn.</param>
		/// <returns>Why the learn was refused, or <see cref="MerchantPurchaseFailure.None"/>.</returns>
		public MerchantPurchaseFailure LearnAbilityEvent<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : AbilityEvent
		{
			return LearnAbilityGeneric<AbilityEvent, KnownAbilityEventAddBroadcast>(
				conn,
				character,
				template,
				(abilityController, id) => abilityController.KnowsAbilityEvent(id),
				(abilityController, list) => abilityController.LearnAbilityEvents(list.Cast<AbilityEvent>().ToList()),
				t => t.ID,
				t => t.Price,
				t => new KnownAbilityEventAddBroadcast { TemplateID = t.ID }
			);
		}

		/// <summary>
		/// Appends a completed currency movement to the economy ledger.
		/// </summary>
		/// <remarks>
		/// Call this only once the movement has actually happened AND its balance change has been
		/// persisted. The row carries its outcome, so nothing revisits it and nothing infers a
		/// balance from it — which is what keeps a lost row a reporting gap rather than an
		/// economic one.
		///
		/// <para>This is NOT an escrow and must not be reasoned about as one. Writing the row
		/// before the deduction is durable, or writing it without the outcome, would recreate the
		/// window where a later pass has to guess whether currency was taken. There is no such
		/// pass, deliberately: the two guesses available to it are paying out money that was never
		/// taken and refunding a purchase that completed, and both create currency.</para>
		///
		/// <para>Recording is fire-and-forget and nothing waits on it. A player never pays latency
		/// for bookkeeping.</para>
		/// </remarks>
		/// <param name="characterID">The character whose balance moved.</param>
		/// <param name="amount">Amount moved. Non-positive amounts are not recorded.</param>
		/// <param name="reason">Which sink the movement belongs to.</param>
		/// <param name="absorbed">True when the currency left the economy, false when it was given back.</param>
		private void RecordCurrencyMovement(long characterID, long amount, CurrencyMovementReason reason, bool absorbed)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}

			CurrencyMovementState state = absorbed
				? CurrencyMovementState.Absorbed
				: CurrencyMovementState.Returned;

			if (!EnqueuePersistence(async () =>
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICurrencyLedgerService>(out ICurrencyLedgerService ledgerService))
				{
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(characterID, amount, (int)reason, (int)state);
				if (!record.IsSuccess)
				{
					Log.Warning("InteractableSystem", $"Currency ledger: could not record {amount} ({reason}/{state}) for CharID={characterID}. {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("InteractableSystem", $"Currency ledger: async worker was full; the record for CharID={characterID} ran on the unbounded fallback path.");
			}
		}

		/// <summary>
		/// Enqueues character attribute data (including currency) persistence for a merchant purchase.
		/// Called BEFORE in-memory deduction to ensure the DB reflects the change even if the
		/// server crashes before the in-memory state is updated.
		/// </summary>
		/// <returns>True if the persist was successfully enqueued, false otherwise.</returns>
		private bool TryPersistMerchantAttributes(IPlayerCharacter character)
		{
			if (character == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}

			long charID = character.ID;

			var dtos = new List<CharacterAttributeData>();
			/* Version++ AND MarkPersistPending, together — the pair is what makes the dirty flag
			 * work. A bump alone moves the attribute past the version a periodic save in flight
			 * recorded, so that save's confirmation clears a mark it no longer owns. */
			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
				kvp.Value.MarkPersistPending(kvp.Value.Version);
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: charID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: 0.0f
				));
			}
			foreach (var kvp in attributeController.ResourceAttributes)
			{
				kvp.Value.Version++;
				kvp.Value.MarkPersistPending(kvp.Value.Version);
				dtos.Add(new CharacterAttributeData(
					id: 0,
					version: kvp.Value.Version,
					characterID: charID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue
				));
			}

			return dtos.Count > 0 &&
				EnqueuePersistence(() => PersistMerchantAttributesToDbAsync(dtos, charID), charID);
		}

		/// <summary>
		/// Asynchronously persists attribute changes from merchant purchases to the database.
		/// </summary>
		private async System.Threading.Tasks.Task PersistMerchantAttributesToDbAsync(List<CharacterAttributeData> dtos, long charID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out var service))
				{
					await Log.Error("InteractableSystem", "PersistMerchantAttributesToDbAsync: Failed to resolve ICharacterAttributeService");
					return;
				}

				await BulkWriteReporting.ReportAsync("InteractableSystem", "Merchant attribute save",
					await service.PersistAsync(dtos), $"CharID={charID}");
			}
			catch (System.Exception ex)
			{
				await Log.Error("InteractableSystem", $"PersistMerchantAttributesToDbAsync failed (CharID={charID}): {ex}");
			}
		}
	}
}