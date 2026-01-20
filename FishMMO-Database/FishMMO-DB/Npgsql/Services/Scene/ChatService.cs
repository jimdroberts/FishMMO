using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class ChatService : BaseService<ChatEntity>, IChatService
	{
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
			long characterId,
			long worldServerId,
			long sceneServerId,
			ChatChannel channel,
			string message,
			DateTime serverReceivedTime,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || sceneServerId <= 0 || string.IsNullOrWhiteSpace(message))
			{
				return DatabaseResult.Failure("INVALID_PARAMETERS", "World server ID, scene server ID must be greater than zero and message must not be empty.");
			}

			var channelByte = (byte)channel;

			var result = await ExecuteSqlAsync(
				$@"INSERT INTO {TableName} 
				   (character_id, world_server_id, scene_server_id, server_received_time, time_created, channel, message)
				   VALUES ({characterId}, {worldServerId}, {sceneServerId}, {serverReceivedTime}, CURRENT_TIMESTAMP, {channelByte}, {message})",
				"SaveChatMessage",
				entityName: "Chat",
				entityId: characterId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			return await ExecuteWithStrategyAsync(async dbContext =>
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
					.ToListAsync(cancellationToken);

				return messages.Select(MapEntityToDto).ToList();
			}, "FetchChatMessages", cancellationToken);
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