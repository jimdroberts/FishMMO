using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Mailbox operations: fetches, sends, and deletes mail via async database calls.
	/// All operations validate that the requesting character is near a mailbox interactable.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Maximum subject length for outgoing mail.
		/// </summary>
		private const int MaxMailSubjectLength = 200;

		/// <summary>
		/// Maximum body length for outgoing mail.
		/// </summary>
		private const int MaxMailBodyLength = 4000;

		/// <summary>
		/// Handles a <see cref="MailFetchBroadcast"/> from the client.
		/// Validates the player is near a mailbox, then fetches mail from the database asynchronously
		/// and sends the results via <see cref="MailListBroadcast"/>.
		/// </summary>
		private void OnServerMailFetchBroadcastReceived(NetworkConnection conn, MailFetchBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}

			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					return;
				}

				if (!TryValidateMailbox(character, msg.InteractableID))
				{
					return;
				}

				long characterID = character.ID;

				if (TryEnqueueAsyncWork(() => FetchMailAsync(conn, character, characterID, guardKey), conn, characterID))
				{
					asyncOwnsGuard = true;
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Fetches all mail for the specified character from the database and broadcasts the result to the client.
		/// </summary>
		private async Task FetchMailAsync(NetworkConnection conn, IPlayerCharacter character, long characterID, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterMailService>(out var mailService))
				{
					return;
				}

				var result = await mailService.FetchAsync(characterID);
				if (!result.IsSuccess || result.Data == null)
				{
					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive)
						{
							return;
						}
						Server.NetworkWrapper.Broadcast(conn, new MailListBroadcast()
						{
							Entries = System.Array.Empty<MailEntryData>(),
						}, true, Channel.Reliable);
					});
					return;
				}

				List<MailEntryData> entries = new List<MailEntryData>(result.Data.Count);
				for (int i = 0; i < result.Data.Count; i++)
				{
					var mail = result.Data[i];
					entries.Add(new MailEntryData()
					{
						ID = mail.ID,
						SenderName = mail.SenderName ?? "",
						Subject = ChatHelper.Sanitize(mail.Subject) ?? "",
						Body = ChatHelper.Sanitize(mail.Body) ?? "",
						Read = mail.Read,
						ItemTemplateID = mail.ItemAttachmentTemplateID,
						CurrencyAmount = mail.CurrencyAttachment,
					});
				}

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive)
					{
						return;
					}
					Server.NetworkWrapper.Broadcast(conn, new MailListBroadcast()
					{
						Entries = entries.ToArray(),
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error fetching mail for CharID={characterID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a <see cref="MailSendBroadcast"/> from the client.
		/// Validates the player is near a mailbox, then sends mail via the database asynchronously.
		/// </summary>
		private void OnServerMailSendBroadcastReceived(NetworkConnection conn, MailSendBroadcast msg, Channel channel)
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

			if (!CharacterStateValidation.CanAct(character))
			{
				SendMailSendResult(conn, false, MailFailureReason.ServerError);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				SendMailSendResult(conn, false, MailFailureReason.ServerError);
				return;
			}

			bool asyncOwnsGuard = false;
			MailFailureReason reason = MailFailureReason.ServerError;
			try
			{
				// Validate input
				if (string.IsNullOrWhiteSpace(msg.RecipientName) ||
					!Authentication.IsAllowedCharacterName(msg.RecipientName) ||
					string.IsNullOrWhiteSpace(msg.Subject) ||
					string.IsNullOrWhiteSpace(msg.Body) ||
					msg.Subject.Length > MaxMailSubjectLength ||
					msg.Body.Length > MaxMailBodyLength)
				{
					reason = MailFailureReason.InvalidMessage;
					return;
				}

				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					return;
				}

				if (!TryValidateMailbox(character, msg.InteractableID))
				{
					reason = MailFailureReason.NoMailbox;
					return;
				}

				/* Take the attachment out of the sender BEFORE the mail exists.
				 *
				 * The failure directions are not symmetric. Escrowing first and failing to send
				 * loses the item, which is recoverable and reportable; creating the mail first and
				 * failing to escrow puts the same item in the sender's bags and in the recipient's
				 * mailbox at once, which is duplication. This is the same ordering the merchant
				 * purchase and corpse currency paths use, for the same reason. */
				if (!TryEscrowMailAttachment(conn, character, msg, out MailAttachment attachment, out reason))
				{
					return;
				}

				long senderID = character.ID;
				string senderName = character.CharacterName;
				string recipientName = msg.RecipientName;
				string subject = msg.Subject;
				string body = msg.Body;

				if (TryEnqueueAsyncWork(() => SendMailAsync(conn, senderID, senderName, recipientName, subject, body, attachment, guardKey), conn, senderID))
				{
					asyncOwnsGuard = true;
					reason = MailFailureReason.None;
				}
				else
				{
					// The worker refused it, so nothing will ever send this mail. Put the
					// attachment back rather than letting the escrow swallow it.
					RefundMailAttachment(conn, character, attachment);
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndIngressGuard(guardKey);
					SendMailSendResult(conn, false, reason);
				}
			}
		}

		/// <summary>
		/// What a send took out of the sender, so it can be attached or given back.
		/// </summary>
		private struct MailAttachment
		{
			/// <summary>Template ID of the escrowed item, or 0 when no item was attached.</summary>
			public int ItemTemplateID;
			/// <summary>Generation seed of the escrowed item.</summary>
			public int ItemSeed;
			/// <summary>Stack size escrowed.</summary>
			public uint ItemAmount;
			/// <summary>Currency escrowed.</summary>
			public int Currency;
			/// <summary>Inventory slot the item came out of, for a refund.</summary>
			public int SourceSlot;

			/// <summary>True when this escrow holds anything at all.</summary>
			public bool HasAnything => (ItemTemplateID != 0 && ItemAmount > 0) || Currency > 0;
		}

		/// <summary>
		/// Validates that the character is standing at the mailbox its request names.
		/// </summary>
		/// <remarks>
		/// Resolved through the shared rule and gated on <c>CanInteract</c> rather than a bare
		/// range test, so the mail paths agree with every other interaction about which component
		/// answers and about refusing a corpse.
		/// </remarks>
		/// <param name="character">The requesting character.</param>
		/// <param name="interactableID">The mailbox's scene object ID.</param>
		/// <returns>True when the mailbox resolves and will accept the interaction.</returns>
		private bool TryValidateMailbox(IPlayerCharacter character, long interactableID)
		{
			if (!ValidateSceneObject(interactableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return false;
			}

			IInteractable interactable = InteractableResolver.Resolve(sceneObject);
			return interactable is IMailbox && interactable.CanInteract(character);
		}

		/// <summary>
		/// Removes the requested attachment from the sender and reports what was taken.
		/// </summary>
		/// <remarks>
		/// Nothing about the attachment's identity or value comes from the client: the request
		/// names an inventory slot and a quantity, and what is actually in that slot is what gets
		/// attached. Currency is clamped to the sender's own balance.
		/// </remarks>
		/// <param name="conn">The sender's connection, for inventory updates.</param>
		/// <param name="character">The sender.</param>
		/// <param name="msg">The send request.</param>
		/// <param name="attachment">Receives what was escrowed.</param>
		/// <param name="reason">Receives why the escrow failed.</param>
		/// <returns>True when the escrow succeeded, including the no-attachment case.</returns>
		private bool TryEscrowMailAttachment(
			NetworkConnection conn,
			IPlayerCharacter character,
			MailSendBroadcast msg,
			out MailAttachment attachment,
			out MailFailureReason reason)
		{
			attachment = new MailAttachment() { SourceSlot = -1 };
			reason = MailFailureReason.ServerError;

			long characterID = character.ID;

			// ── Currency ──────────────────────────────────────────────────────
			if (msg.CurrencyAttachment > 0)
			{
				/* CharacterCurrency reads the BASE value, so a buffed character cannot mail
				 * currency it does not have. The balance is read explicitly rather than left to
				 * TrySpend because the shortfall needs its own reason code — TrySpend refuses
				 * an unaffordable spend and a refused persist identically. */
				if (currencyTemplate == null ||
					!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance))
				{
					return false;
				}

				if (balance < msg.CurrencyAttachment)
				{
					reason = MailFailureReason.NotEnoughCurrency;
					return false;
				}

				if (!CharacterCurrency.TrySpend(character, currencyTemplate, msg.CurrencyAttachment, () => TryPersistMerchantAttributes(character)))
				{
					// TrySpend has already refunded; the attachment must not claim what was never taken.
					attachment.Currency = 0;
					return false;
				}

				attachment.Currency = msg.CurrencyAttachment;
				RecordCurrencyMovement(character.ID, msg.CurrencyAttachment, CurrencyMovementReason.MailAttachment, absorbed: true);
			}

			// ── Item ──────────────────────────────────────────────────────────
			if (msg.AttachmentSlot >= 0)
			{
				if (!character.TryGet(out IInventoryController inventoryController) ||
					!inventoryController.IsValidSlot(msg.AttachmentSlot) ||
					inventoryController.IsSlotLocked(msg.AttachmentSlot) ||
					!inventoryController.TryGetItem(msg.AttachmentSlot, out Item item) ||
					item == null ||
					item.Template == null)
				{
					RefundMailAttachment(conn, character, attachment);
					attachment = new MailAttachment() { SourceSlot = -1 };
					reason = MailFailureReason.InvalidAttachment;
					return false;
				}

				long available = item.IsStackable ? item.Stackable.Amount : 1;
				long requested = msg.AttachmentQuantity <= 0 ? available : msg.AttachmentQuantity;
				long quantity = Math.Min(requested, available);
				if (quantity < 1)
				{
					RefundMailAttachment(conn, character, attachment);
					attachment = new MailAttachment() { SourceSlot = -1 };
					reason = MailFailureReason.InvalidAttachment;
					return false;
				}

				attachment.ItemTemplateID = item.Template.ID;
				attachment.ItemSeed = item.IsGenerated ? item.Generator.Seed : 0;
				attachment.ItemAmount = (uint)quantity;
				attachment.SourceSlot = msg.AttachmentSlot;

				// Whole slot or part of a stack, mirroring the merchant sell removal exactly.
				if (quantity >= available)
				{
					Item removed = inventoryController.RemoveItem(msg.AttachmentSlot);
					if (removed == null)
					{
						RefundMailAttachment(conn, character, attachment);
						attachment = new MailAttachment() { SourceSlot = -1 };
						reason = MailFailureReason.InvalidAttachment;
						return false;
					}

					removed.Version++;
					int slot = msg.AttachmentSlot;
					// Through the journalled batch, addressed by the item — see the merchant sale.
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
			}

			reason = MailFailureReason.None;
			return true;
		}

		/// <summary>
		/// Gives an escrowed attachment back to the sender. Main thread only.
		/// </summary>
		/// <remarks>
		/// The item is returned through the normal grant path rather than to the slot it came from:
		/// the slot may have been filled in the meantime, and a grant that finds no room fails
		/// loudly instead of overwriting whatever is there now.
		/// </remarks>
		/// <param name="conn">The sender's connection.</param>
		/// <param name="character">The sender.</param>
		/// <param name="attachment">What to give back.</param>
		private void RefundMailAttachment(NetworkConnection conn, IPlayerCharacter character, MailAttachment attachment)
		{
			if (!attachment.HasAnything)
			{
				return;
			}

			if (attachment.Currency > 0 &&
				currencyTemplate != null &&
				CharacterCurrency.TryAdd(character, currencyTemplate, attachment.Currency))
			{
				if (!TryPersistMerchantAttributes(character))
				{
					Log.Error("InteractableSystem", $"Mail refund: currency persist rejected for CharID={character.ID}; in-memory balance is correct but the DB holds the deduction.");
				}

				/* Balances the Absorbed row written when the mail was accepted. Without this the
				 * ledger reports every failed send as currency that left the economy, and the
				 * sink totals the table exists to produce drift upward with each one. */
				RecordCurrencyMovement(character.ID, attachment.Currency, CurrencyMovementReason.MailAttachment, absorbed: false);
			}

			if (attachment.ItemTemplateID != 0 &&
				attachment.ItemAmount > 0 &&
				character.TryGet(out IInventoryController inventoryController))
			{
				BaseItemTemplate itemTemplate = BaseItemTemplate.Get<BaseItemTemplate>(attachment.ItemTemplateID);
				if (itemTemplate != null)
				{
					// The seed rides along: a generated item refunded from a template alone would
					// reroll its attributes.
					Item restored = new Item(0, attachment.ItemSeed, itemTemplate, attachment.ItemAmount);
					if (!SendNewItemBroadcast(conn, character, inventoryController, restored))
					{
						Log.Error("InteractableSystem", $"Mail refund: could not return attachment to CharID={character.ID}; the item was lost.");
					}
				}
			}
		}

		/// <summary>
		/// Sends the one reply every exit from the send handler owes the client.
		/// </summary>
		private void SendMailSendResult(NetworkConnection conn, bool success, MailFailureReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new MailSendResultBroadcast()
			{
				Success = success,
				Reason = reason,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends mail via the database asynchronously.
		/// </summary>
		private async Task SendMailAsync(
			NetworkConnection conn,
			long senderID,
			string senderName,
			string recipientName,
			string subject,
			string body,
			MailAttachment attachment,
			long guardKey)
		{
			bool delivered = false;
			MailFailureReason reason = MailFailureReason.ServerError;

			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterMailService>(out var mailService))
				{
					return;
				}

				// Resolve recipient by name: look up the character ID from the database.
				// This prevents sending mail to non-existent characters and ensures the
				// recipientID passed to SendAsync is correct.
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					await Log.Warning("InteractableSystem", $"SendMailAsync: ICharacterService not available, cannot resolve recipient '{recipientName}'.");
					return;
				}

				DatabaseResult<CharacterData?> recipientResult = await characterService.FetchAsync(recipientName);
				if (!recipientResult.IsSuccess || recipientResult.Data == null)
				{
					await Log.Warning("InteractableSystem", $"SendMailAsync: Recipient '{recipientName}' not found (SenderID={senderID}).");
					reason = MailFailureReason.NoRecipient;
					return;
				}

				long recipientID = recipientResult.Data.Value.ID;

				DatabaseResult result = await mailService.SendAsync(
					senderID,
					senderName,
					recipientID,
					subject,
					body,
					attachment.ItemTemplateID,
					attachment.ItemSeed,
					attachment.ItemAmount,
					attachment.Currency,
					1  // version
				);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"SendMailAsync DB error (SenderID={senderID}): {result.ErrorCode} - {result.ErrorMessage}");
					return;
				}

				delivered = true;
				reason = MailFailureReason.None;
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error sending mail from CharID={senderID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);

				/* The refund and the reply are main-thread work: both touch the character's
				 * in-memory inventory and attributes, and this is a worker thread. Marshalled
				 * rather than done here for the same reason every other async path in this system
				 * hands its result back. */
				bool succeeded = delivered;
				MailFailureReason finalReason = reason;
				TryEnqueueMainThread(() =>
				{
					if (!succeeded)
					{
						IPlayerCharacter sender = conn != null && conn.IsActive && conn.FirstObject != null
							? conn.FirstObject.GetComponent<IPlayerCharacter>()
							: null;

						if (sender != null && sender.ID == senderID)
						{
							RefundMailAttachment(conn, sender, attachment);
						}
						else if (attachment.HasAnything)
						{
							/* The sender left before the send failed, so there is nobody to hand
							 * the escrow back to in memory. It is already removed from their
							 * persisted inventory, so it is gone — logged loudly because it is the
							 * one place on this path that loses a player's property. */
							Log.Error("InteractableSystem",
								$"Mail send failed for CharID={senderID} after the sender disconnected; the escrowed attachment could not be returned.");
						}
					}

					SendMailSendResult(conn, succeeded, finalReason);
				});
			}
		}

		/// <summary>
		/// Handles a <see cref="MailClaimAttachmentBroadcast"/>: hands one mail's attachment to its owner.
		/// </summary>
		/// <remarks>
		/// The attachment is read and cleared by a single database statement, so two claims racing
		/// on one mail cannot both be granted — see <c>ICharacterMailService.ClaimAttachmentAsync</c>.
		/// What this adds on top is a room check <em>before</em> the clear: the clear is
		/// irreversible, and claiming into a full inventory would destroy the item.
		/// </remarks>
		private void OnServerMailClaimAttachmentBroadcastReceived(NetworkConnection conn, MailClaimAttachmentBroadcast msg, Channel channel)
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

			if (!CharacterStateValidation.CanAct(character))
			{
				SendMailClaimResult(conn, msg.MailID, false, MailFailureReason.ServerError);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				SendMailClaimResult(conn, msg.MailID, false, MailFailureReason.ServerError);
				return;
			}

			bool asyncOwnsGuard = false;
			MailFailureReason reason = MailFailureReason.ServerError;
			try
			{
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					return;
				}

				if (!TryValidateMailbox(character, msg.InteractableID))
				{
					reason = MailFailureReason.NoMailbox;
					return;
				}

				long characterID = character.ID;
				long mailID = msg.MailID;

				if (TryEnqueueAsyncWork(() => ClaimMailAttachmentAsync(conn, character, characterID, mailID, guardKey), conn, characterID))
				{
					asyncOwnsGuard = true;
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndIngressGuard(guardKey);
					SendMailClaimResult(conn, msg.MailID, false, reason);
				}
			}
		}

		/// <summary>
		/// Reads one mail's attachment, checks the claimer has room, clears it, and grants it.
		/// </summary>
		private async Task ClaimMailAttachmentAsync(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			long mailID,
			long guardKey)
		{
			bool guardReleased = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterMailService>(out var mailService))
				{
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mailID, false, MailFailureReason.ServerError));
					return;
				}

				/* Read the attachment before claiming it.
				 *
				 * The claim is a destructive, irreversible clear, so the room check has to happen
				 * first — granting into a full inventory after the row is already zeroed would
				 * destroy the item outright. This peek is not authoritative and does not need to
				 * be: the claim's own predicate is what prevents a double grant, and only this
				 * character can claim this mail, so nothing can change between the two calls
				 * except by this same connection, which the ingress guard serialises. */
				DatabaseResult<System.Collections.Generic.IReadOnlyList<CharacterMailData>> listResult =
					await mailService.FetchAsync(characterID);
				if (!listResult.IsSuccess || listResult.Data == null)
				{
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mailID, false, MailFailureReason.ServerError));
					return;
				}

				CharacterMailData? found = null;
				for (int i = 0; i < listResult.Data.Count; ++i)
				{
					if (listResult.Data[i].ID == mailID)
					{
						found = listResult.Data[i];
						break;
					}
				}

				if (!found.HasValue ||
					(found.Value.ItemAttachmentTemplateID == 0 && found.Value.CurrencyAttachment <= 0))
				{
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mailID, false, MailFailureReason.NothingToClaim));
					return;
				}

				CharacterMailData mail = found.Value;

				/* Hand back to the main thread for the room check.
				 *
				 * The inventory is main-thread state and this is a worker, so the check cannot
				 * happen here — and it has to happen before the claim, because the claim is an
				 * irreversible clear. The continuation takes ownership of the ingress guard from
				 * this point on; releasing it here would let a second claim start while this one
				 * is still deciding.
				 */
				if (!TryEnqueueMainThread(() => ContinueMailClaim(conn, character, characterID, mail, guardKey)))
				{
					await Log.Warning("InteractableSystem", $"ClaimMailAttachmentAsync: main-thread queue rejected the room check for MailID={mailID}.");
					EndIngressGuard(guardKey);
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mailID, false, MailFailureReason.ServerError));
				}

				// The continuation owns the guard now; do not release it in the finally below.
				guardReleased = true;
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error claiming mail attachment (MailID={mailID}, CharID={characterID}): {ex}");
				TryEnqueueMainThread(() => SendMailClaimResult(conn, mailID, false, MailFailureReason.ServerError));
			}
			finally
			{
				if (!guardReleased)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Checks the claimer has room, then goes back to the database to take the attachment.
		/// Main thread only.
		/// </summary>
		/// <remarks>
		/// The middle hop of the claim. It exists because the two things the claim needs — the
		/// character's inventory and the database — live on different threads, and the order
		/// between them is not negotiable: room first, because the clear that follows cannot be
		/// undone.
		/// </remarks>
		/// <param name="conn">The claimer's connection.</param>
		/// <param name="character">The claiming character.</param>
		/// <param name="characterID">The claiming character's ID.</param>
		/// <param name="mail">The mail as it was read a moment ago.</param>
		/// <param name="guardKey">The ingress guard this hop now owns.</param>
		private void ContinueMailClaim(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			CharacterMailData mail,
			long guardKey)
		{
			bool handedOn = false;
			MailFailureReason reason = MailFailureReason.ServerError;
			try
			{
				if (conn == null || !conn.IsActive || character == null)
				{
					return;
				}

				if (!HasRoomForMailAttachment(character, mail))
				{
					reason = MailFailureReason.InventoryFull;
					return;
				}

				if (TryEnqueueAsyncWork(() => FinishMailClaimAsync(conn, character, characterID, mail, guardKey), conn, characterID))
				{
					handedOn = true;
				}
			}
			finally
			{
				if (!handedOn)
				{
					EndIngressGuard(guardKey);
					SendMailClaimResult(conn, mail.ID, false, reason);
				}
			}
		}

		/// <summary>
		/// Takes the attachment off the mail and hands the grant back to the main thread.
		/// </summary>
		private async Task FinishMailClaimAsync(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			CharacterMailData mail,
			long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterMailService>(out var mailService))
				{
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mail.ID, false, MailFailureReason.ServerError));
					return;
				}

				DatabaseResult<CharacterMailAttachmentData?> claim =
					await mailService.ClaimAttachmentAsync(mail.ID, characterID, mail.Version + 1);

				if (!claim.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"FinishMailClaimAsync DB error (MailID={mail.ID}, CharID={characterID}): {claim.ErrorCode} - {claim.ErrorMessage}");
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mail.ID, false, MailFailureReason.ServerError));
					return;
				}

				if (!claim.Data.HasValue || !claim.Data.Value.HasAnything)
				{
					// Already claimed, or nothing was attached after all.
					TryEnqueueMainThread(() => SendMailClaimResult(conn, mail.ID, false, MailFailureReason.NothingToClaim));
					return;
				}

				CharacterMailAttachmentData attachment = claim.Data.Value;
				TryEnqueueMainThread(() => GrantMailAttachment(conn, character, mail.ID, attachment));
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error finishing mail claim (MailID={mail.ID}, CharID={characterID}): {ex}");
				TryEnqueueMainThread(() => SendMailClaimResult(conn, mail.ID, false, MailFailureReason.ServerError));
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// True when the character can accept everything attached to a mail. Main thread only.
		/// </summary>
		private bool HasRoomForMailAttachment(IPlayerCharacter character, CharacterMailData mail)
		{
			if (mail.ItemAttachmentTemplateID == 0 || mail.ItemAttachmentAmount <= 0)
			{
				// Currency only. The claimer's balance is an int and the grant clamps to it, so
				// there is nothing that can fail to fit.
				return true;
			}

			if (!character.TryGet(out IInventoryController inventoryController))
			{
				return false;
			}

			BaseItemTemplate itemTemplate = BaseItemTemplate.Get<BaseItemTemplate>(mail.ItemAttachmentTemplateID);
			if (itemTemplate == null)
			{
				return false;
			}

			return inventoryController.CanAddItem(new Item(0, mail.ItemAttachmentSeed, itemTemplate, (uint)mail.ItemAttachmentAmount));
		}

		/// <summary>
		/// Hands a claimed attachment to its owner. Main thread only.
		/// </summary>
		/// <remarks>
		/// The database row is already cleared by the time this runs, so a failure here loses the
		/// attachment rather than duplicating it — the correct direction, and the reason the room
		/// check happens before the claim rather than here.
		/// </remarks>
		private void GrantMailAttachment(NetworkConnection conn, IPlayerCharacter character, long mailID, CharacterMailAttachmentData attachment)
		{
			if (conn == null || !conn.IsActive || character == null)
			{
				Log.Error("InteractableSystem", $"Mail attachment claimed for MailID={mailID} but the claimer had gone; the attachment was lost.");
				return;
			}

			bool granted = false;

			if (attachment.CurrencyAmount > 0 &&
				currencyTemplate != null &&
				CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance))
			{
				// Clamped to the headroom left in an int balance, exactly as corpse currency is.
				long capacity = (long)int.MaxValue - balance;
				int amount = (int)Math.Min(capacity, attachment.CurrencyAmount);
				if (amount > 0 &&
					CharacterCurrency.TryAdd(character, currencyTemplate, amount))
				{
					granted = true;
					if (!TryPersistMerchantAttributes(character))
					{
						Log.Error("InteractableSystem", $"Mail claim: currency persist rejected for CharID={character.ID}; the mail row is cleared but the payout is not recorded.");
					}
				}
			}

			if (attachment.ItemTemplateID != 0 &&
				attachment.ItemAmount > 0 &&
				character.TryGet(out IInventoryController inventoryController))
			{
				BaseItemTemplate itemTemplate = BaseItemTemplate.Get<BaseItemTemplate>(attachment.ItemTemplateID);
				if (itemTemplate != null)
				{
					// Rebuilt with the seed the sender's item had, so a mailed generated item keeps
					// its attributes instead of rerolling them on arrival.
					Item item = new Item(0, attachment.ItemSeed, itemTemplate, attachment.ItemAmount);
					if (SendNewItemBroadcast(conn, character, inventoryController, item))
					{
						granted = true;
					}
					else
					{
						Log.Error("InteractableSystem", $"Mail claim: could not grant the attachment on MailID={mailID} to CharID={character.ID}; the item was lost.");
					}
				}
			}

			SendMailClaimResult(conn, mailID, granted, granted ? MailFailureReason.None : MailFailureReason.ServerError);
		}

		/// <summary>
		/// Sends the one reply every exit from the claim path owes the client.
		/// </summary>
		private void SendMailClaimResult(NetworkConnection conn, long mailID, bool success, MailFailureReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new MailClaimResultBroadcast()
			{
				MailID = mailID,
				Success = success,
				Reason = reason,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Handles a <see cref="MailDeleteBroadcast"/> from the client.
		/// Validates the player is near a mailbox, then soft-deletes the mail via the database asynchronously.
		/// </summary>
		private void OnServerMailDeleteBroadcastReceived(NetworkConnection conn, MailDeleteBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}
			
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					return;
				}

				if (!TryValidateMailbox(character, msg.InteractableID))
				{
					return;
				}

				long characterID = character.ID;
				long mailID = msg.MailID;

				if (TryEnqueueAsyncWork(() => DeleteMailAsync(characterID, mailID, guardKey), conn, characterID))
				{
					asyncOwnsGuard = true;
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Soft-deletes a mail entry via the database asynchronously.
		/// </summary>
		private async Task DeleteMailAsync(long characterID, long mailID, long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterMailService>(out var mailService))
				{
					return;
				}

				DatabaseResult result = await mailService.DeleteAsync(mailID, characterID, 2);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"DeleteMailAsync DB error (MailID={mailID}, CharID={characterID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error deleting mail ID={mailID} for CharID={characterID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}
	}
}