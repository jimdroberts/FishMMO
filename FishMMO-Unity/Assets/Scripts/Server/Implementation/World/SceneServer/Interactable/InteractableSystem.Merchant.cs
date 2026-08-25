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
	/// Merchant purchase handling: item purchases, ability learning, and ability event learning from merchant NPCs.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles a <see cref="MerchantPurchaseBroadcast"/> from a client. Validates the merchant interactable,
		/// checks sufficient currency, and processes the purchase (item, ability, or ability event) based on the tab type.
		/// </summary>
		private void OnServerMerchantPurchaseBroadcastReceived(NetworkConnection conn, MerchantPurchaseBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();

			if (character == null ||
				!character.TryGet(out IInventoryController inventoryController) ||
				!CharacterStateValidation.CanAct(character))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{

				// validate template exists
				MerchantTemplate merchantTemplate = MerchantTemplate.Get<MerchantTemplate>(msg.ID);
				if (merchantTemplate == null)
				{
					return;
				}

				// Validate the scene the character is actually in — see CurrentSceneName.
				string currentScene = character.CurrentSceneName();
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(currentScene, out _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + currentScene);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
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
					!interactable.CanInteract(character) ||
					merchantTemplate.ID != merchant.Template.ID)
				{
					return;
				}

				switch (msg.Type)
				{
					case MerchantTabType.Item:
						if (!TryPurchaseItem(conn, character, inventoryController, merchantTemplate, msg))
						{
							return;
						}
						break;
					case MerchantTabType.Ability:
						if (merchantTemplate.Abilities != null &&
							msg.Index >= 0 &&
							msg.Index < merchantTemplate.Abilities.Count)
						{
							LearnAbilityTemplate(conn, character, merchantTemplate.Abilities[msg.Index]);
						}
						break;
					case MerchantTabType.AbilityEvent:
						if (merchantTemplate.AbilityEvents != null &&
							msg.Index >= 0 &&
							msg.Index < merchantTemplate.AbilityEvents.Count)
						{
							LearnAbilityEvent(conn, character, merchantTemplate.AbilityEvents[msg.Index]);
						}
						break;
					default: return;
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
				return false;
			}

			BaseItemTemplate itemTemplate = merchantTemplate.Items[msg.Index];
			if (itemTemplate == null || itemTemplate.Price <= 0)
			{
				return false;
			}

			if (currencyTemplate == null)
			{
				Log.Debug("InteractableSystem", "currencyTemplate is null.");
				return false;
			}
			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency))
			{
				return false;
			}

			// One stack at most, so a purchase can always be represented as a single Item.
			long maxStack = itemTemplate.MaxStackSize > 0 ? itemTemplate.MaxStackSize : 1;
			long requested = msg.Quantity <= 0 ? 1 : msg.Quantity;
			long quantity = Math.Min(requested, maxStack);

			/* Long arithmetic throughout. Price and quantity are both ints, and a client asking
			 * for int.MaxValue of a costly item would overflow the product into a negative
			 * "total" that every affordability test passes. */
			long affordable = currency.Value / itemTemplate.Price;
			quantity = Math.Min(quantity, affordable);
			if (quantity < 1)
			{
				return false;
			}

			long total = quantity * itemTemplate.Price;
			if (total > int.MaxValue || currency.Value < total)
			{
				return false;
			}

			int charge = (int)total;

			// Deduct, then snapshot: the persist reads the in-memory values as they stand now.
			currency.AddValue(-charge);

			if (!TryPersistMerchantAttributes(character))
			{
				Log.Warning("InteractableSystem", $"TryPurchaseItem: currency persist rejected for CharID={character.ID}; refunding {charge}.");
				currency.AddValue(charge);
				return false;
			}

			Item newItem = new Item(itemTemplate, (uint)quantity);
			if (!SendNewItemBroadcast(conn, character, inventoryController, newItem))
			{
				/* No room, or the add was refused. Nothing was granted, so put the money back and
				 * persist the refund — the deduction has already been enqueued and would otherwise
				 * be the only half of the transaction the database ever sees. */
				currency.AddValue(charge);
				if (!TryPersistMerchantAttributes(character))
				{
					Log.Error("InteractableSystem", $"TryPurchaseItem: refund persist rejected for CharID={character.ID}; in-memory balance is correct but the DB holds the deduction.");
				}
				return false;
			}

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

				if (currencyTemplate == null ||
					!character.TryGet(out ICharacterAttributeController attributeController) ||
					!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency))
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
					long version = removed.Version;
					int slot = msg.Slot;
					if (!EnqueuePersistence(() => DeleteMerchantSoldSlotAsync(characterID, slot, version), characterID))
					{
						/* Could not record the removal, so undo it. Selling an item whose deletion
						 * is never written means the item comes back on the next login while the
						 * payout does not — a duplication bug in the player's favour. */
						inventoryController.SetItemSlot(removed, slot);
						return;
					}

					Server.NetworkWrapper.Broadcast(conn, new InventoryRemoveItemBroadcast()
					{
						Slot = slot,
					}, true, Channel.Reliable);
				}
				else
				{
					item.Stackable.Remove((uint)quantity);
					item.Version++;

					List<CharacterInventoryData> itemsToSave = new List<CharacterInventoryData>
					{
						new CharacterInventoryData(
							id: item.ID,
							version: item.Version,
							characterID: characterID,
							templateID: item.Template.ID,
							slot: item.Slot,
							seed: item.IsGenerated ? item.Generator.Seed : 0,
							amount: item.Stackable.Amount),
					};

					if (!EnqueuePersistence(() => PersistInventoryItemsAsync(itemsToSave), characterID))
					{
						/* Put the stack back. Amount is written directly rather than through a
						 * helper because ItemStackable exposes only a saturating Remove; there is
						 * no Add, and the value being restored is one this method just took. */
						item.Stackable.Amount += (uint)quantity;
						item.Version++;
						return;
					}

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
					currency.AddValue(payout);
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
		/// Deletes an inventory slot emptied by a merchant sale.
		/// </summary>
		/// <remarks>
		/// The version is the item's own, incremented once by the caller, rather than
		/// <c>long.MaxValue</c>. Stamping the maximum makes the surviving soft-deleted row
		/// permanently unwritable, because every later write is version-gated against it — see the
		/// per-slot persistence poisoning in the audit findings. A sale must not create one.
		/// </remarks>
		private async Task DeleteMerchantSoldSlotAsync(long characterID, int slot, long version)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterInventoryService>(out var inventoryService))
				{
					await Log.Error("InteractableSystem", "DeleteMerchantSoldSlotAsync: Failed to resolve ICharacterInventoryService");
					return;
				}

				DatabaseResult result = await inventoryService.DeleteAsync(characterID, slot, version);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"DeleteMerchantSoldSlotAsync DB error (CharID={characterID}, Slot={slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"DeleteMerchantSoldSlotAsync failed (CharID={characterID}, Slot={slot}): {ex}");
			}
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
		private void LearnAbilityGeneric<TTemplate, TBroadcast>(
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
			if (template == null || character == null || !character.TryGet(out IAbilityController abilityController) || knowsFunc(abilityController, idSelector(template)))
			{
				return;
			}

			if (currencyTemplate == null)
			{
				Log.Debug("InteractableSystem", "currencyTemplate is null.");
				return;
			}
			/* Value, not FinalValue. AddValue below writes the base value; FinalValue is the base
			 * plus every modifier in force, so testing one and writing the other let a character
			 * with a currency-boosting buff spend money it did not have and go negative. Same
			 * defect, same fix, as the item purchase path. */
			int price = priceSelector(template);
			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency) ||
				currency.Value < price)
			{
				Log.Debug("InteractableSystem", "Not enough currency!");
				return;
			}

			long charID = character.ID;
			int templateID = idSelector(template);
			if (!TryEnqueueAsyncWork(() => PersistKnownAbilityAsync(charID, templateID), charID))
			{
				Log.Warning("InteractableSystem", $"LearnAbilityGeneric: Async worker rejected known-ability persist for CharID={charID}, TemplateID={templateID}.");
				return;
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

			// remove the price from the characters currency
			currency.AddValue(-price);

			/* Enqueued AFTER the deduction, because TryPersistMerchantAttributes snapshots the
			 * in-memory values as they stand when it is called. Enqueuing it first — which the
			 * item purchase path used to do — writes the pre-purchase balance and loses the
			 * deduction entirely. */
			if (!TryPersistMerchantAttributes(character))
			{
				Log.Warning("InteractableSystem", $"LearnAbilityGeneric: currency persist rejected for CharID={charID}; the deduction is in memory only until the next character save.");
			}

			// tell the client about the new ability/event
			Server.NetworkWrapper.Broadcast(conn, broadcastFactory(template), true, Channel.Reliable);
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
		public void LearnAbilityTemplate<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : BaseAbilityTemplate
		{
			LearnAbilityGeneric<BaseAbilityTemplate, KnownAbilityAddBroadcast>(
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
		public void LearnAbilityEvent<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : AbilityEvent
		{
			LearnAbilityGeneric<AbilityEvent, KnownAbilityEventAddBroadcast>(
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
			foreach (var kvp in attributeController.Attributes)
			{
				kvp.Value.Version++;
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