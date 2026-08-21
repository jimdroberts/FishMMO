using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class ChatService : BaseService<ChatEntity>, IChatService
	{
		/// <summary>
		/// Maximum allowed length for chat messages. This length should never be close to reached. Maximum server message should be 256 characters.
		/// </summary>
		public const int MaxMessageLength = 4000;

		/// <summary>
		/// Maximum allowed length for audit character name. This length should never be close to reached. Maximum character name is 32 characters.
		/// </summary>
		public const int MaxAuditNameLength = 256;

		/// <summary>
		/// Maximum allowed length for audit account name. This length should never be close to reached. Maximum account name is 32 characters.
		/// </summary>
		public const int MaxAuditAccountLength = 256;

		/// <summary>
		/// Initializes a new instance of ChatService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public ChatService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(
			long characterId,
			string characterName,
			string accountName,
			long worldServerId,
			long sceneServerId,
			ChatChannel channel,
			string message,
			DateTime serverReceivedTime,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "CharacterId must be greater than 0.");
			}

			if (!Enum.IsDefined(typeof(ChatChannel), channel))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid chat channel.");
			}

			if (worldServerId <= 0 || sceneServerId <= 0 || string.IsNullOrWhiteSpace(message))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "World server ID, scene server ID must be greater than zero and message must not be empty.");
			}

			if (message.Length > MaxMessageLength)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Message exceeds maximum length.");
			}

			var normalizedCharacterName = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName;
			if (normalizedCharacterName.Length > MaxAuditNameLength)
				normalizedCharacterName = normalizedCharacterName.Substring(0, MaxAuditNameLength);

			var normalizedAccountName = string.IsNullOrWhiteSpace(accountName) ? string.Empty : accountName;
			if (normalizedAccountName.Length > MaxAuditAccountLength)
				normalizedAccountName = normalizedAccountName.Substring(0, MaxAuditAccountLength);


				var channelByte = (byte)channel;
				// NOTE: Uses EF Core change tracker (AddAsync + SaveChanges) instead of raw SQL like most other
				// services. Version is explicitly set to 1 to match the DB default; otherwise EF would default
				// to 0 and the concurrency token check would fail on the first update.
				var result = await ExecuteWriteAsync(async dbContext =>
				{
					var entity = new ChatEntity
					{
						CharacterID = characterId,
						CharacterName = normalizedCharacterName,
						AccountName = normalizedAccountName,
						WorldServerID = worldServerId,
						SceneServerID = sceneServerId,
						ServerReceivedTime = serverReceivedTime,
						TimeCreated = DateTime.UtcNow,
						Channel = channelByte,
						Message = message,
						Version = 1
					};

				await dbContext.Chat.AddAsync(entity, cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistBatchAsync(
			List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, ChatChannel channel, string message, DateTime serverReceivedTime)> messages,
			int maxBatchSize = 1000,
			CancellationToken cancellationToken = default)
		{
			if (messages == null || messages.Count == 0)
			{
				return DatabaseResult.Success();
			}

			if (maxBatchSize < 500) maxBatchSize = 500;
			else if (maxBatchSize > 2500) maxBatchSize = 2500;

			// Pre-validate all messages before writing any.
			for (int i = 0; i < messages.Count; i++)
			{
				var m = messages[i];
				if (m.characterId <= 0)
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, $"Message at index {i}: CharacterId must be greater than 0.");
				if (!Enum.IsDefined(typeof(ChatChannel), m.channel))
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, $"Message at index {i}: Invalid chat channel.");
				if (m.worldServerId <= 0 || m.sceneServerId <= 0 || string.IsNullOrWhiteSpace(m.message))
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, $"Message at index {i}: World server ID, scene server ID must be greater than zero and message must not be empty.");
				if (m.message.Length > MaxMessageLength)
					return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, $"Message at index {i}: Message exceeds maximum length.");
			}

			for (int offset = 0; offset < messages.Count; offset += maxBatchSize)
			{
				var batchCount = Math.Min(maxBatchSize, messages.Count - offset);

				var result = await ExecuteWriteAsync(async dbContext =>
				{
					var now = DateTime.UtcNow;
					var entities = new ChatEntity[batchCount];

					for (int i = 0; i < batchCount; i++)
					{
						var m = messages[offset + i];

						var charName = string.IsNullOrWhiteSpace(m.characterName) ? string.Empty : m.characterName;
						if (charName.Length > MaxAuditNameLength)
							charName = charName.Substring(0, MaxAuditNameLength);

						var acctName = string.IsNullOrWhiteSpace(m.accountName) ? string.Empty : m.accountName;
						if (acctName.Length > MaxAuditAccountLength)
							acctName = acctName.Substring(0, MaxAuditAccountLength);

						entities[i] = new ChatEntity
						{
							CharacterID = m.characterId,
							CharacterName = charName,
							AccountName = acctName,
							WorldServerID = m.worldServerId,
							SceneServerID = m.sceneServerId,
							ServerReceivedTime = m.serverReceivedTime,
							TimeCreated = now,
							Channel = (byte)m.channel,
							Message = m.message,
							Version = 1
						};
					}

					await dbContext.Chat.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
				}, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess)
				{
					return result;
				}
			}

			return DatabaseResult.Success();
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<ChatData>>> FetchAsync(
			DateTime lastFetch,
			long lastPosition,
			int amount,
			long sceneServerId,
			CancellationToken cancellationToken = default)
		{
			if (amount <= 0)
				return DatabaseResult<List<ChatData>>.Success(new List<ChatData>());

			var result = await ExecuteReadAsync(async dbContext =>
			{
				/* Suppress a scene server's own echo of the messages it already delivered.
				 *
				 * This list does NOT mark a channel as non-global. It is only ever applied
				 * together with `c.SceneServerID == sceneServerId` below, so it filters exactly
				 * one thing: the copy the ORIGIN server is fetching back of a message it has
				 * already broadcast locally. Every other scene server still pulls it and
				 * delivers it, so a channel in this list remains fully global.
				 *
				 * World used to be omitted here, with a comment reasoning that it is a global
				 * channel and so must not be filtered — which confused those two meanings and
				 * made world chat appear TWICE for players on the sending scene server.
				 * OnWorldChat and OnTradeChat (ChatSystem.WorldChat.cs) are identical in
				 * delivery: a live message is buffered into OutboundWorldBroadcastBuffer and
				 * flushed to local players, and the pump-sourced copy is broadcast on arrival.
				 * Trade was in this list and World was not, so only World double-delivered. */
				var localChannels = new byte[]
				{
					(byte)ChatChannel.Tell,
					(byte)ChatChannel.Guild,
					(byte)ChatChannel.Party,
					(byte)ChatChannel.Trade,
					(byte)ChatChannel.World
				};

				var messages = await dbContext.Chat
					.AsNoTracking()
					.Where(c =>
						(c.TimeCreated > lastFetch || (c.TimeCreated == lastFetch && c.ID > lastPosition))
						&& !(localChannels.Contains(c.Channel) && c.SceneServerID == sceneServerId))
					.OrderBy(c => c.TimeCreated)
					.ThenBy(c => c.ID)
					.Take(amount)
					.ToListAsync(cancellationToken).ConfigureAwait(false);

				return messages.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Maps ChatEntity to ChatData DTO.
		/// </summary>
		/// <param name="entity">Chat entity from database.</param>
		/// <returns>Chat data DTO.</returns>
		private ChatData MapEntityToDto(ChatEntity entity)
		{
			return new ChatData(
				id: entity.ID,
				characterID: entity.CharacterID,
				characterName: entity.CharacterName ?? string.Empty,
				accountName: entity.AccountName ?? string.Empty,
				worldServerID: entity.WorldServerID,
				sceneServerID: entity.SceneServerID,
				channel: entity.Channel,
				message: entity.Message,
				serverReceivedTime: entity.ServerReceivedTime,
				timeCreated: entity.TimeCreated
			);
		}
	}
}