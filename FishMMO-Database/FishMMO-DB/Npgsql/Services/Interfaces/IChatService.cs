using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for chat message operations.
	/// Provides async methods for persisting and fetching chat messages.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Persist*) in this service use execution strategies to ensure transient database
	/// failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because retry handling is done by BaseService, not by provider-level retry.
	/// BaseService uses explicit transactions only when a write requires multiple database statements.
	/// This interface describes behavior; the implementation uses BaseService execution wrappers.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// </remarks>
	public interface IChatService
	{
		/// <summary>
		/// Persists a chat message with denormalized audit fields.
		/// </summary>
		/// <param name="characterId">Character ID sending the message.</param>
		/// <param name="characterName">Character name (denormalized for audit retention).</param>
		/// <param name="accountName">Account name (denormalized for audit retention).</param>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="sceneServerId">Scene server ID.</param>
		/// <param name="channel">Chat channel.</param>
		/// <param name="message">Message content.</param>
		/// <param name="serverReceivedTime">Timestamp when server received the message (for legal audit trail).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Chat audit fields are denormalized so logs can survive character deletion.
		/// Passing the names avoids a race where the character row is deleted between lookup and insert.
		/// Uses BaseService.ExecuteWriteAsync for:
		/// - Automatic transient failure retry
		/// - Centralized exception handling and mapping
		/// - Consistent DatabaseResult pattern
		/// </remarks>
		Task<DatabaseResult> PersistAsync(
			long characterId,
			string characterName,
			string accountName,
			long worldServerId,
			long sceneServerId,
			ChatChannel channel,
			string message,
			DateTime serverReceivedTime,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches paginated chat messages excluding local messages for the specified scene server.
		/// </summary>
		/// <param name="lastFetch">Timestamp to compare messages against.</param>
		/// <param name="lastPosition">Last message ID fetched (for pagination).</param>
		/// <param name="amount">Maximum number of messages to fetch.</param>
		/// <param name="sceneServerId">Scene server ID to filter out local messages.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the list of chat message data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ with AsNoTracking for optimal read performance and automatically benefits
		/// from the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Filters out local channel messages (Tell, Guild, Party, World, Trade) from the specified scene server.
		/// Returns empty list for invalid amount.
		/// </remarks>
		Task<DatabaseResult<List<ChatData>>> FetchAsync(
			DateTime lastFetch,
			long lastPosition,
			int amount,
			long sceneServerId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Persists multiple chat messages in batches.
		/// </summary>
		/// <param name="messages">List of chat messages to persist. Each tuple contains:
		/// (characterId, characterName, accountName, worldServerId, sceneServerId, channel, message, serverReceivedTime).</param>
		/// <param name="maxBatchSize">Maximum number of messages per database round-trip (500–2500).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistBatchAsync(
			List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, ChatChannel channel, string message, DateTime serverReceivedTime)> messages,
			int maxBatchSize = 1000,
			CancellationToken cancellationToken = default);
	}
}