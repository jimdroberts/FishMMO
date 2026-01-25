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
	public sealed class ChatService : IdempotentBaseService<ChatEntity>, IChatService
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
		public async Task<DatabaseResult> SaveAsync(
			long accountId,
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
			if (accountId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "AccountId must be greater than 0.");
			}

			if (worldServerId <= 0 || sceneServerId <= 0 || string.IsNullOrWhiteSpace(message))
			{
				return DatabaseResult.Failure("INVALID_PARAMETERS", "World server ID, scene server ID must be greater than zero and message must not be empty.");
			}

			if (message.Length > MaxMessageLength)
			{
				return DatabaseResult.Failure("MESSAGE_TOO_LONG", "Message exceeds maximum length.");
			}

			var normalizedCharacterName = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName;
			if (normalizedCharacterName.Length > MaxAuditNameLength)
				normalizedCharacterName = normalizedCharacterName.Substring(0, MaxAuditNameLength);

			var normalizedAccountName = string.IsNullOrWhiteSpace(accountName) ? string.Empty : accountName;
			if (normalizedAccountName.Length > MaxAuditAccountLength)
				normalizedAccountName = normalizedAccountName.Substring(0, MaxAuditAccountLength);

			var channelByte = (byte)channel;
			var requestId = Guid.NewGuid();
			var result = await ExecuteIdempotentAsync(
				requestId,
				accountId,
				"SaveChatMessage",
				async (dbContext, transaction, ct) =>
				{
					var sql = $@"INSERT INTO {TableName}
						(character_id, character_name, account_name, world_server_id, scene_server_id, server_received_time, time_created, channel, message)
						VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, CURRENT_TIMESTAMP, {{6}}, {{7}})";

					_ = await dbContext.Database.ExecuteSqlRawAsync(
						sql,
						new object[]
						{
							characterId,
							normalizedCharacterName,
							normalizedAccountName,
							worldServerId,
							sceneServerId,
							serverReceivedTime,
							channelByte,
							message
						},
						ct).ConfigureAwait(false);

					return true;
				},
				cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				// Filter out local messages for the specified scene server
				var localChannels = new byte[]
				{
					(byte)ChatChannel.Tell,
					(byte)ChatChannel.Guild,
					(byte)ChatChannel.Party,
					(byte)ChatChannel.World,
					(byte)ChatChannel.Trade
				};

				var messages = await dbContext.Chat
					.AsNoTracking()
					.Where(c => c.TimeCreated >= lastFetch &&
							   c.ID > lastPosition &&
							   !(localChannels.Contains(c.Channel) && c.SceneServerID == sceneServerId))
					.OrderBy(c => c.TimeCreated)
					.ThenBy(c => c.ID)
					.Take(amount)
					.ToListAsync(ct).ConfigureAwait(false);

				return messages.Select(MapEntityToDto).ToList();
			}, "FetchChatMessages", cancellationToken).ConfigureAwait(false);
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