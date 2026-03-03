using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Shared.Core;
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

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					return;
				}

				// Validate mailbox scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				// Validate interactable is a mailbox in range
				IMailbox mailbox = sceneObject.GameObject.GetComponent<IMailbox>();
				if (mailbox == null || !mailbox.InRange(character.Transform))
				{
					return;
				}

				long characterID = character.ID;

				if (TryEnqueueAsyncWork(() => FetchMailAsync(conn, character, characterID, guardKey), characterID))
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
							Entries = new List<MailEntryData>(),
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
						Subject = mail.Subject ?? "",
						Body = mail.Body ?? "",
						Read = mail.Read,
						ItemTemplateID = mail.ItemAttachment,
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
						Entries = entries,
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

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate input
				if (string.IsNullOrWhiteSpace(msg.RecipientName) ||
					string.IsNullOrWhiteSpace(msg.Subject) ||
					string.IsNullOrWhiteSpace(msg.Body))
				{
					return;
				}

				if (msg.Subject.Length > MaxMailSubjectLength ||
					msg.Body.Length > MaxMailBodyLength)
				{
					return;
				}

				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					return;
				}

				// Validate mailbox scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				IMailbox mailbox = sceneObject.GameObject.GetComponent<IMailbox>();
				if (mailbox == null || !mailbox.InRange(character.Transform))
				{
					return;
				}

				long senderID = character.ID;
				string recipientName = msg.RecipientName;
				string subject = msg.Subject;
				string body = msg.Body;

				if (TryEnqueueAsyncWork(() => SendMailAsync(senderID, recipientName, subject, body, guardKey), senderID))
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
		/// Sends mail via the database asynchronously.
		/// </summary>
		private async Task SendMailAsync(long senderID, string recipientName, string subject, string body, long guardKey)
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

				// Resolve recipient by name — requires a character lookup service.
				// For now, we pass 0 as recipientID; the service validates internally.
				// A future enhancement can resolve the recipient name to ID here.
				await mailService.SendAsync(
					senderID,
					0, // recipientID — service should resolve by name or this needs a character lookup
					subject,
					body,
					0, // itemAttachmentTemplateID
					0, // itemAttachmentSeed
					0, // itemAttachmentAmount
					1  // version
				);
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error sending mail from CharID={senderID}: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
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

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					return;
				}

				// Validate mailbox scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				IMailbox mailbox = sceneObject.GameObject.GetComponent<IMailbox>();
				if (mailbox == null || !mailbox.InRange(character.Transform))
				{
					return;
				}

				long characterID = character.ID;
				long mailID = msg.MailID;

				if (TryEnqueueAsyncWork(() => DeleteMailAsync(characterID, mailID, guardKey), characterID))
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

				await mailService.DeleteAsync(mailID, characterID, 1);
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