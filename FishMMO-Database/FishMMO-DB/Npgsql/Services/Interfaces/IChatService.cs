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
		/// Persists one line bridged in from Discord, as a <see cref="ChatChannel.Discord"/> row the
		/// scene servers relay to every player in <paramref name="worldServerId"/>.
		/// </summary>
		/// <param name="worldServerId">World the Discord channel is bridged to.</param>
		/// <param name="sceneServerId">Scene server the Discord channel is bridged to.</param>
		/// <param name="authorName">The Discord author's (already sanitised) display name, for the audit columns.</param>
		/// <param name="message">The line as players will see it.</param>
		/// <param name="serverReceivedTime">When the bridge received it, UTC.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		/// <remarks>
		/// Through the same INSERT as every other chat row so it is stamped by the same clock: the
		/// bridge used to stamp <c>time_created</c> from its own host's clock, and the scene servers
		/// page the table by that column.
		/// </remarks>
		Task<DatabaseResult> PersistBridgedAsync(
			long worldServerId,
			long sceneServerId,
			string authorName,
			string message,
			DateTime serverReceivedTime,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads the chat rows one scene server should relay: new, relevant to someone it hosts,
		/// and not its own echo.
		/// </summary>
		/// <param name="query">Where to start, what to skip, and what this server hosts.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The rows in <c>(time_created, id)</c> order, the database clock at the start of the read,
		/// and whether the read caught up.
		/// </returns>
		/// <remarks>
		/// <para>
		/// This replaces a strict <c>(time_created, id)</c> cursor that lost rows for good whenever
		/// two writers committed out of stamp order, and read one 20-row page of the whole shard's
		/// chat per call however far behind it was (hot-path audit H5, H6).
		/// </para>
		/// <para>
		/// The caller reads from a start held <see cref="ChatService.PumpCommitWindowSeconds"/>
		/// behind what it has settled, and passes the IDs it has already handled in that window
		/// as <see cref="ChatPumpQuery.ExcludeIds"/>. Pages are read while they come back full, up to
		/// <see cref="ChatPumpQuery.MaxPages"/>, so a backlog is worked through instead of growing.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<ChatPumpPage>> FetchPumpAsync(ChatPumpQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads the chat rows the Discord relay has not handled yet: every game row, never a row
		/// bridged in from Discord.
		/// </summary>
		/// <param name="query">Where to start and what to skip.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The rows in <c>(time_created, id)</c> order, the database clock at the start of the read,
		/// and whether the read caught up.
		/// </returns>
		/// <remarks>
		/// <para>
		/// The same window as <see cref="FetchPumpAsync"/>, read by a <see cref="ChatReadWindow"/>.
		/// The relay used to page by <c>id &gt; last id seen</c>, and IDs are taken at the INSERT,
		/// not at the commit: a row that committed after a higher ID had been read was skipped for
		/// good. It also read everything past that ID in one unbounded query (hot-path audit H5).
		/// </para>
		/// <para>
		/// A query with no <see cref="ChatRelayQuery.FromUtc"/> starts at the database's "now", so a
		/// relay that starts does not republish history.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<ChatPumpPage>> FetchRelayAsync(ChatRelayQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Searches persisted chat for an operator, newest message first.
		/// </summary>
		/// <param name="query">The filter to apply. Null is treated as an unfiltered query.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing one page of <see cref="ChatAdminData"/> and
		/// the total row count on success, or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// <para>
		/// This is the read behind a harassment or abuse report, so it is separate from
		/// <see cref="FetchPumpAsync"/> in every respect: that one is a window the scene servers pull
		/// forward and it deliberately hides a server's own echo, while this one hides nothing and
		/// orders backwards from now.
		/// </para>
		/// <para>
		/// A message-text filter supplied with no character name, no account name and no lower time
		/// bound is REFUSED with <see cref="DatabaseErrorCodes.ValidationError"/> rather than served.
		/// The reason is in the implementation; callers should surface the message rather than
		/// retrying, because a retry of the same query is refused identically.
		/// </para>
		/// <para>
		/// Read-only: it uses AsNoTracking and writes nothing, including no audit row. Recording
		/// reads in the audit log would mean every listing of the log wrote a row of its own.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<ChatAdminPage>> SearchAdminAsync(ChatAdminQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Persists multiple chat messages in ONE transaction: every message lands, or none does.
		/// </summary>
		/// <remarks>
		/// All-or-nothing is what makes a failed call safe to retry. The rows are the chat audit
		/// log and other scene servers deliver whatever new rows they find, so a call that had
		/// committed part of a list before failing would write — and deliver — that part twice when
		/// the caller retried it.
		/// </remarks>
		/// <param name="messages">List of chat messages to persist. Each tuple contains:
		/// (characterId, characterName, accountName, worldServerId, sceneServerId, channel, message, serverReceivedTime).</param>
		/// <param name="maxBatchSize">Messages per INSERT statement inside the transaction (clamped to 500–2500).
		/// It does not change atomicity.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistBatchAsync(
			List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, ChatChannel channel, string message, DateTime serverReceivedTime)> messages,
			int maxBatchSize = 1000,
			CancellationToken cancellationToken = default);
	}
}