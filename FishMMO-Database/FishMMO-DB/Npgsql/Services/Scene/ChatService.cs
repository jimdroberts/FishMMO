using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class ChatService : IChatService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of ChatService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public ChatService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
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

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<ChatEntity>();
					var channelByte = (byte)channel;

					// ServerReceivedTime = when server received message (app timestamp)
					// time_created = CURRENT_TIMESTAMP (DB timestamp for audit/legal purposes)
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
						   (character_id, world_server_id, scene_server_id, server_received_time, time_created, channel, message)
						   VALUES ({characterId}, {worldServerId}, {sceneServerId}, {serverReceivedTime}, CURRENT_TIMESTAMP, {channelByte}, {message})",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.Failure("SAVE_FAILED", "Failed to save chat message.");
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"SaveChatMessage",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"chat_pkey",
					"A chat message with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"chat_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SaveChatMessage",
					"Failed to save chat message.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SaveChatMessage",
					"An unexpected error occurred while saving chat message.",
					ex.Message,
					false,
					null,
					ex));
			}
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

			await using var context = dbContextFactory.CreateDbContext();

			try
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

				var messages = await context.Chat
					.AsNoTracking()
					.Where(c => c.TimeCreated >= lastFetch &&
							   c.ID > lastPosition &&
							   !(localChannels.Contains(c.Channel) && c.SceneServerID == sceneServerId))
					.OrderBy(c => c.TimeCreated)
					.ThenBy(c => c.ID)
					.Take(amount)
					.ToListAsync(cancellationToken);

				return DatabaseResult<List<ChatData>>.Success(messages.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<List<ChatData>>.FromException(new DatabaseTimeoutException(
					"FetchChatMessages",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<List<ChatData>>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"chat_pkey",
					"A chat message with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<List<ChatData>>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"chat_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<List<ChatData>>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<List<ChatData>>.FromException(new DatabaseQueryException(
					"FetchChatMessages",
					"Failed to fetch chat messages.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<List<ChatData>>.FromException(new DatabaseQueryException(
					"FetchChatMessages",
					"An unexpected error occurred while fetching chat messages.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps ChatEntity to ChatData DTO.
		/// </summary>
		/// <param name="entity">Chat entity from database.</param>
		/// <returns>Chat data DTO.</returns>
		private ChatData MapEntityToDto(ChatEntity entity)
		{
			return new ChatData
			{
				ID = entity.ID,
				CharacterID = entity.CharacterID,
				WorldServerID = entity.WorldServerID,
				SceneServerID = entity.SceneServerID,
				ServerReceivedTime = entity.ServerReceivedTime,
				TimeCreated = entity.TimeCreated,
				Channel = entity.Channel,
				Message = entity.Message
			};
		}
	}
}