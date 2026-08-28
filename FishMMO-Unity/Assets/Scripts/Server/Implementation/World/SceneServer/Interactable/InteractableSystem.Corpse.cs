using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Corpse looting: opening a dead NPC's loot window, transferring its items and currency to a
	/// looter's inventory, and keeping every looter's view of a shared pile in step.
	/// </summary>
	/// <remarks>
	/// The corpse is the authority throughout. The client sends a scene object ID and a slot index
	/// and nothing else — never an item ID, a template, or an amount — so the worst a forged
	/// request can do is name a slot that is empty or a corpse it has no rights to, both of which
	/// are refused here.
	/// </remarks>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Ingress-guard operation code for corpse loot transfers.
		/// </summary>
		/// <remarks>
		/// A code of its own, separate from the general interaction guard on operation 0.
		/// Looting shares that guard's need to serialise concurrent requests from one connection —
		/// two takes racing on the same slot is the duplication bug this prevents — but not its
		/// one-second debounce, which would limit a player to a single item per second and make
		/// emptying a corpse slower than the corpse's own decay.
		/// </remarks>
		private const byte CorpseLootOperation = 1;

		/// <summary>
		/// Minimum milliseconds between corpse loot transfers from one connection.
		/// </summary>
		/// <remarks>
		/// Short enough to be imperceptible when clicking through a corpse, long enough that a
		/// scripted client cannot turn the take path into a tight loop against the database.
		/// </remarks>
		private const int CorpseLootDebounceMilliseconds = 50;

		/// <summary>
		/// Corpses this system has subscribed to, so the subscription can be dropped again.
		/// </summary>
		/// <remarks>
		/// A corpse can be looted by several players and can therefore be opened many times; the
		/// expiry subscription must be added exactly once per corpse or decaying one body would
		/// send a close message per open rather than per viewer.
		/// </remarks>
		private readonly HashSet<ILootableCorpse> watchedCorpses = new HashSet<ILootableCorpse>();

		/// <summary>
		/// Opens a corpse's loot window for a player who has just interacted with it.
		/// </summary>
		/// <remarks>
		/// Called from the general interaction handler rather than driven by an ECA trigger.
		/// Looting is intrinsic to every NPC that can die, and requiring a trigger list on each
		/// prefab would make an unconfigured NPC silently unlootable — a content bug with no
		/// symptom other than missing loot.
		/// </remarks>
		/// <param name="conn">The looting connection.</param>
		/// <param name="character">The looting character.</param>
		/// <param name="corpse">The corpse being opened.</param>
		private void OpenCorpseLoot(NetworkConnection conn, IPlayerCharacter character, ILootableCorpse corpse)
		{
			if (conn == null || character == null || corpse == null)
			{
				return;
			}

			// Eligibility is re-tested here and not taken from CanInteract. CanInteract answers
			// differently on client and server by design, and this is the server's answer.
			if (!corpse.IsCorpse || !corpse.IsEligibleLooter(character.ID))
			{
				SendCorpseLootResult(conn, corpse.ID, -1, false,
					corpse.IsCorpse ? CorpseLootFailureReason.NotEligible : CorpseLootFailureReason.NoCorpse);
				return;
			}

			if (!corpse.HasLoot)
			{
				SendCorpseLootResult(conn, corpse.ID, -1, false, CorpseLootFailureReason.AlreadyTaken);
				return;
			}

			WatchCorpse(corpse);
			corpse.AddLootViewer(conn);
			SendCorpseLootContents(conn, corpse);
		}

		/// <summary>
		/// Subscribes to a corpse's expiry once, so open windows are closed before it despawns.
		/// </summary>
		/// <param name="corpse">The corpse to watch.</param>
		private void WatchCorpse(ILootableCorpse corpse)
		{
			if (!watchedCorpses.Add(corpse))
			{
				return;
			}
			corpse.OnCorpseExpired += Corpse_OnExpired;
		}

		/// <summary>
		/// Closes every open window on a corpse that is about to leave the world.
		/// </summary>
		/// <remarks>
		/// Runs while the corpse's scene object ID still resolves. After the despawn, a client
		/// holding an open window would keep submitting requests against an ID that no longer
		/// exists — and, once the pooled instance is reused, against an ID that resolves to a
		/// completely different creature.
		/// </remarks>
		/// <param name="corpse">The expiring corpse.</param>
		private void Corpse_OnExpired(ILootableCorpse corpse)
		{
			if (corpse == null)
			{
				return;
			}

			corpse.OnCorpseExpired -= Corpse_OnExpired;
			watchedCorpses.Remove(corpse);

			CloseCorpseWindows(corpse);
		}

		/// <summary>
		/// Tells every viewer of a corpse to close its loot window, and clears the viewer list.
		/// </summary>
		/// <param name="corpse">The corpse whose windows should close.</param>
		private void CloseCorpseWindows(ILootableCorpse corpse)
		{
			IReadOnlyCollection<NetworkConnection> viewers = corpse.LootViewers;
			if (viewers == null || viewers.Count < 1)
			{
				return;
			}

			// Copied before iterating: RemoveLootViewer below mutates the collection being walked.
			List<NetworkConnection> snapshot = new List<NetworkConnection>(viewers);
			for (int i = 0; i < snapshot.Count; ++i)
			{
				NetworkConnection viewer = snapshot[i];
				if (viewer == null || !viewer.IsActive)
				{
					continue;
				}
				Server.NetworkWrapper.Broadcast(viewer, new CorpseLootCloseWindowBroadcast()
				{
					InteractableID = corpse.ID,
				}, true, Channel.Reliable);
			}

			for (int i = 0; i < snapshot.Count; ++i)
			{
				corpse.RemoveLootViewer(snapshot[i]);
			}
		}

		/// <summary>
		/// Sends one connection the corpse's current contents.
		/// </summary>
		/// <param name="conn">The connection to send to.</param>
		/// <param name="corpse">The corpse being described.</param>
		private void SendCorpseLootContents(NetworkConnection conn, ILootableCorpse corpse)
		{
			if (conn == null || !conn.IsActive || corpse == null)
			{
				return;
			}

			IReadOnlyList<Item> items = corpse.LootItems;
			List<CorpseLootSlotData> slots = new List<CorpseLootSlotData>(items != null ? items.Count : 0);

			if (items != null)
			{
				for (int i = 0; i < items.Count; ++i)
				{
					Item item = items[i];
					if (item == null || item.Template == null)
					{
						// Emptied slots are omitted rather than sent as holes; each entry carries
						// its own index so the client can still address the ones that remain.
						continue;
					}
					slots.Add(new CorpseLootSlotData()
					{
						Slot = i,
						TemplateID = item.Template.ID,
						Amount = item.IsStackable ? item.Stackable.Amount : 1,
					});
				}
			}

			Server.NetworkWrapper.Broadcast(conn, new CorpseLootBroadcast()
			{
				InteractableID = corpse.ID,
				CorpseName = corpse.Name,
				Items = slots.ToArray(),
				Currency = corpse.LootCurrency,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Re-sends a corpse's contents to everyone currently viewing it.
		/// </summary>
		/// <remarks>
		/// The pile is shared, so one player's take changes what every other viewer is looking at.
		/// Pushing the whole contents rather than a "slot N is gone" delta means a viewer who
		/// missed an earlier update still converges on the truth.
		/// </remarks>
		/// <param name="corpse">The corpse whose viewers should be refreshed.</param>
		private void RefreshCorpseViewers(ILootableCorpse corpse)
		{
			IReadOnlyCollection<NetworkConnection> viewers = corpse.LootViewers;
			if (viewers == null || viewers.Count < 1)
			{
				return;
			}

			List<NetworkConnection> snapshot = new List<NetworkConnection>(viewers);
			for (int i = 0; i < snapshot.Count; ++i)
			{
				SendCorpseLootContents(snapshot[i], corpse);
			}
		}

		/// <summary>
		/// Sends the reply that releases the client's pending lock on a slot.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="interactableID">The corpse the request named.</param>
		/// <param name="slot">The slot the request named, or -1.</param>
		/// <param name="success">Whether anything was transferred.</param>
		/// <param name="reason">Why the request was refused.</param>
		private void SendCorpseLootResult(NetworkConnection conn, long interactableID, int slot, bool success, CorpseLootFailureReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new CorpseLootResultBroadcast()
			{
				InteractableID = interactableID,
				Slot = slot,
				Success = success,
				Reason = reason,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Resolves and validates everything a corpse loot request needs.
		/// </summary>
		/// <remarks>
		/// Range is re-tested on every take rather than only on open, because a loot window stays
		/// open while the player walks — without this, opening a corpse once would license looting
		/// it from any distance for as long as it survived.
		/// </remarks>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="interactableID">The corpse's scene object ID.</param>
		/// <param name="character">Receives the looting character.</param>
		/// <param name="inventoryController">Receives the looter's inventory.</param>
		/// <param name="corpse">Receives the corpse.</param>
		/// <param name="reason">Receives why validation failed.</param>
		/// <returns>True when the request may proceed.</returns>
		private bool TryResolveCorpseRequest(
			NetworkConnection conn,
			long interactableID,
			out IPlayerCharacter character,
			out IInventoryController inventoryController,
			out ILootableCorpse corpse,
			out CorpseLootFailureReason reason)
		{
			character = null;
			inventoryController = null;
			corpse = null;
			reason = CorpseLootFailureReason.ServerError;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				!character.TryGet(out inventoryController) ||
				!CharacterStateValidation.CanAct(character))
			{
				return false;
			}

			if (!ValidateSceneObject(interactableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				reason = CorpseLootFailureReason.NoCorpse;
				return false;
			}

			corpse = ResolveInteractable(sceneObject) as ILootableCorpse;
			if (corpse == null || !corpse.IsCorpse)
			{
				reason = CorpseLootFailureReason.NoCorpse;
				return false;
			}

			if (!corpse.IsEligibleLooter(character.ID))
			{
				reason = CorpseLootFailureReason.NotEligible;
				return false;
			}

			if (!corpse.InRange(character.Transform))
			{
				reason = CorpseLootFailureReason.OutOfRange;
				return false;
			}

			reason = CorpseLootFailureReason.None;
			return true;
		}

		/// <summary>
		/// Closes the corpse's windows once nothing is left on it.
		/// </summary>
		/// <remarks>
		/// The corpse is deliberately NOT despawned here. Its decay timer is what returns it to
		/// the pool and schedules the spawner's respawn, and short-circuiting that would make a
		/// creature's respawn depend on how quickly its killer emptied the body.
		/// </remarks>
		/// <param name="corpse">The corpse to check.</param>
		private void CloseCorpseIfEmpty(ILootableCorpse corpse)
		{
			if (corpse.HasLoot)
			{
				return;
			}
			CloseCorpseWindows(corpse);
		}

		/// <summary>
		/// Acquires the corpse-loot ingress guard, replying on refusal so no slot stays locked.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="guardKey">Receives the guard key to release.</param>
		/// <returns>True when the guard was acquired.</returns>
		private bool TryBeginCorpseLootGuard(NetworkConnection conn, out long guardKey)
		{
			guardKey = 0;
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(conn.ClientId, CorpseLootOperation, CorpseLootDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Handles a request to take a single item from a corpse.
		/// </summary>
		private void OnServerCorpseLootTakeItemBroadcastReceived(NetworkConnection conn, CorpseLootTakeItemBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginCorpseLootGuard(conn, out long guardKey))
			{
				// Answered rather than dropped: the client holds the slot pending until it hears
				// back, so a silent refusal would lock that slot for the life of the window.
				SendCorpseLootResult(conn, msg.InteractableID, msg.Slot, false, CorpseLootFailureReason.ServerError);
				return;
			}

			try
			{
				if (!TryResolveCorpseRequest(conn, msg.InteractableID, out IPlayerCharacter character,
					out IInventoryController inventoryController, out ILootableCorpse corpse, out CorpseLootFailureReason reason))
				{
					SendCorpseLootResult(conn, msg.InteractableID, msg.Slot, false, reason);
					return;
				}

				if (!corpse.TryTakeLootItem(msg.Slot, out Item item))
				{
					// Empty slot: almost always another looter reaching it first on a shared pile.
					SendCorpseLootResult(conn, msg.InteractableID, msg.Slot, false, CorpseLootFailureReason.AlreadyTaken);
					SendCorpseLootContents(conn, corpse);
					return;
				}

				if (!SendNewItemBroadcast(conn, character, inventoryController, item))
				{
					/* Put it back. The corpse has already given the item up, and without this the
					 * only reference to it is the local variable — a full inventory would destroy
					 * the drop rather than leave it on the body. */
					if (!corpse.ReturnLootItem(item, msg.Slot))
					{
						Log.Error("InteractableSystem",
							$"Corpse {corpse.ID}: could not return item to slot {msg.Slot} after a failed grant to CharID={character.ID}; the item was lost.");
					}
					SendCorpseLootResult(conn, msg.InteractableID, msg.Slot, false, CorpseLootFailureReason.InventoryFull);
					return;
				}

				SendCorpseLootResult(conn, msg.InteractableID, msg.Slot, true, CorpseLootFailureReason.None);
				RefreshCorpseViewers(corpse);
				CloseCorpseIfEmpty(corpse);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a request to take a corpse's currency.
		/// </summary>
		private void OnServerCorpseLootTakeCurrencyBroadcastReceived(NetworkConnection conn, CorpseLootTakeCurrencyBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginCorpseLootGuard(conn, out long guardKey))
			{
				SendCorpseLootResult(conn, msg.InteractableID, -1, false, CorpseLootFailureReason.ServerError);
				return;
			}

			try
			{
				if (!TryResolveCorpseRequest(conn, msg.InteractableID, out IPlayerCharacter character,
					out IInventoryController _, out ILootableCorpse corpse, out CorpseLootFailureReason reason))
				{
					SendCorpseLootResult(conn, msg.InteractableID, -1, false, reason);
					return;
				}

				if (!TryGrantCorpseCurrency(character, corpse, out CorpseLootFailureReason currencyReason))
				{
					SendCorpseLootResult(conn, msg.InteractableID, -1, false, currencyReason);
					return;
				}

				SendCorpseLootResult(conn, msg.InteractableID, -1, true, CorpseLootFailureReason.None);
				RefreshCorpseViewers(corpse);
				CloseCorpseIfEmpty(corpse);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Moves a corpse's currency onto a character's currency attribute.
		/// </summary>
		/// <remarks>
		/// Taken off the corpse first, then persisted, then refunded to the corpse if the persist
		/// is refused — the same ordering the merchant purchase path uses, and for the same
		/// reason: the only acceptable failure direction is the player not receiving currency that
		/// still exists, never currency existing in two places at once.
		/// </remarks>
		/// <param name="character">The looting character.</param>
		/// <param name="corpse">The corpse being looted.</param>
		/// <param name="reason">Receives why the grant failed.</param>
		/// <returns>True when currency was transferred.</returns>
		private bool TryGrantCorpseCurrency(IPlayerCharacter character, ILootableCorpse corpse, out CorpseLootFailureReason reason)
		{
			reason = CorpseLootFailureReason.ServerError;

			if (currencyTemplate == null)
			{
				Log.Warning("InteractableSystem", "Corpse currency cannot be granted: currencyTemplate is not assigned.");
				return false;
			}

			if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance))
			{
				return false;
			}

			/* The character's currency attribute is an int, so the take is capped by the room
			 * left in it. Adding first and checking afterwards would wrap the balance, which is
			 * currency duplication rather than merely a lost drop. */
			long capacity = (long)int.MaxValue - balance;
			if (capacity < 1)
			{
				Log.Warning("InteractableSystem", $"CharID={character.ID} cannot accept corpse currency: balance is already at the maximum.");
				return false;
			}

			if (!corpse.TryTakeLootCurrency(capacity, out long amount))
			{
				reason = CorpseLootFailureReason.AlreadyTaken;
				return false;
			}

			if (!CharacterCurrency.TryAdd(character, currencyTemplate, amount))
			{
				// The take already came off the corpse, so it has to go back.
				corpse.ReturnLootCurrency(amount);
				return false;
			}

			if (!TryPersistMerchantAttributes(character))
			{
				Log.Warning("InteractableSystem", $"Corpse currency persist rejected for CharID={character.ID}; returning {amount} to corpse {corpse.ID}.");

				/* Undoing the grant, not a purchase. TrySpend is the deduct path and the balance
				 * demonstrably covers it, having just been increased by this amount.
				 *
				 * The corpse is only credited if that deduct actually happened. Returning the
				 * currency to the corpse after a refused deduct would leave it on the character
				 * AND on the corpse, which is the duplication this whole ordering exists to
				 * prevent — a drop stranded on a character is the acceptable failure, so the
				 * refused case keeps it there and says so. */
				if (!CharacterCurrency.TrySpend(character, currencyTemplate, amount))
				{
					Log.Error("InteractableSystem", $"Corpse currency rollback refused for CharID={character.ID}; {amount} stays on the character and is NOT returned to corpse {corpse.ID}, because crediting both would duplicate it.");
					return false;
				}

				corpse.ReturnLootCurrency(amount);
				return false;
			}

			reason = CorpseLootFailureReason.None;
			return true;
		}

		/// <summary>
		/// Handles a request to take everything from a corpse at once.
		/// </summary>
		/// <remarks>
		/// A partial result is a success: a player with two free slots looting a five-item corpse
		/// should get the two and be told the rest is still there, not be refused outright.
		/// </remarks>
		private void OnServerCorpseLootTakeAllBroadcastReceived(NetworkConnection conn, CorpseLootTakeAllBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (!TryBeginCorpseLootGuard(conn, out long guardKey))
			{
				SendCorpseLootResult(conn, msg.InteractableID, -1, false, CorpseLootFailureReason.ServerError);
				return;
			}

			try
			{
				if (!TryResolveCorpseRequest(conn, msg.InteractableID, out IPlayerCharacter character,
					out IInventoryController inventoryController, out ILootableCorpse corpse, out CorpseLootFailureReason reason))
				{
					SendCorpseLootResult(conn, msg.InteractableID, -1, false, reason);
					return;
				}

				bool tookAnything = false;
				CorpseLootFailureReason lastFailure = CorpseLootFailureReason.AlreadyTaken;

				// Read once, before the grant mutates it, so the outcome is judged against what
				// the corpse held when the request arrived.
				if (corpse.LootCurrency > 0)
				{
					if (TryGrantCorpseCurrency(character, corpse, out CorpseLootFailureReason currencyReason))
					{
						tookAnything = true;
					}
					else
					{
						lastFailure = currencyReason;
					}
				}

				/* Descending, because taking a slot empties it in place and the loop must not be
				 * disturbed by the corpse's own bookkeeping. Ascending would work equally well
				 * today; descending is stable against any future compaction. */
				IReadOnlyList<Item> items = corpse.LootItems;
				for (int i = items.Count - 1; i >= 0; --i)
				{
					if (items[i] == null)
					{
						continue;
					}

					if (!corpse.TryTakeLootItem(i, out Item item))
					{
						continue;
					}

					if (!SendNewItemBroadcast(conn, character, inventoryController, item))
					{
						// Out of room. Put it back and stop: everything after this would fail the
						// same way, and each attempt is a wasted container walk.
						if (!corpse.ReturnLootItem(item, i))
						{
							Log.Error("InteractableSystem",
								$"Corpse {corpse.ID}: could not return item to slot {i} during take-all for CharID={character.ID}; the item was lost.");
						}
						lastFailure = CorpseLootFailureReason.InventoryFull;
						break;
					}

					tookAnything = true;
				}

				SendCorpseLootResult(conn, msg.InteractableID, -1, tookAnything,
					tookAnything ? CorpseLootFailureReason.None : lastFailure);

				RefreshCorpseViewers(corpse);
				CloseCorpseIfEmpty(corpse);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a client closing a corpse's loot window.
		/// </summary>
		/// <remarks>
		/// Deliberately does no state validation beyond resolving the corpse. A player who is
		/// dead, out of range, or no longer eligible must still be able to stop being a viewer —
		/// refusing the close would leave them receiving refresh broadcasts for a window they can
		/// no longer see.
		/// </remarks>
		private void OnServerCorpseLootCloseBroadcastReceived(NetworkConnection conn, CorpseLootCloseBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return;
			}

			if (ResolveInteractable(sceneObject) is ILootableCorpse corpse)
			{
				corpse.RemoveLootViewer(conn);
			}
		}

		/// <summary>
		/// Drops every corpse subscription this system holds.
		/// </summary>
		private void ClearCorpseSubscriptions()
		{
			foreach (ILootableCorpse corpse in watchedCorpses)
			{
				if (corpse != null)
				{
					corpse.OnCorpseExpired -= Corpse_OnExpired;
				}
			}
			watchedCorpses.Clear();
		}
	}
}
