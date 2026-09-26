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
		/// Maximum stored length of a character name, matching <c>chat.character_name</c>.
		/// </summary>
		/// <remarks>
		/// <b>This must equal the column width, not some comfortable number above it.</b> It was
		/// 256 against a <c>varchar(50)</c>, which meant a name between 51 and 256 characters
		/// passed the guard untouched and then failed the INSERT with a 22001 — the truncation
		/// existed precisely to prevent that and did not. Latent today only because no character
		/// name can reach 51 characters; a guard that relies on nothing ever testing it is not a
		/// guard.
		/// </remarks>
		public const int MaxAuditNameLength = 50;

		/// <summary>
		/// Maximum stored length of an account name, matching <c>chat.account_name</c>.
		/// </summary>
		/// <remarks>Same reasoning as <see cref="MaxAuditNameLength"/>.</remarks>
		public const int MaxAuditAccountLength = 50;

		/// <summary>
		/// Initializes a new instance of ChatService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public ChatService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
			insertSql = BuildInsertSql(TableName);
			string pumpFilter = BuildPumpFilterSql();
			pumpFirstPageSql = $@"SELECT c.* FROM {TableName} c
			WHERE {pumpFilter}
			ORDER BY c.time_created, c.id
			LIMIT {{7}}";
			pumpPageAfterSql = $@"SELECT c.* FROM {TableName} c
			WHERE {pumpFilter}
				AND (c.time_created, c.id) > ({{8}}, {{9}})
			ORDER BY c.time_created, c.id
			LIMIT {{7}}";
			string relayFilter = BuildRelayFilterSql();
			relayFirstPageSql = $@"SELECT c.* FROM {TableName} c
			WHERE {relayFilter}
			ORDER BY c.time_created, c.id
			LIMIT {{2}}";
			relayPageAfterSql = $@"SELECT c.* FROM {TableName} c
			WHERE {relayFilter}
				AND (c.time_created, c.id) > ({{3}}, {{4}})
			ORDER BY c.time_created, c.id
			LIMIT {{2}}";
		}

		/// <summary>
		/// Account name written on rows bridged in from Discord, which have no game account.
		/// </summary>
		public const string BridgedAccountName = "Discord";

		/// <summary>
		/// How far behind its own progress the scene servers' chat pump reads, in seconds.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A row's stamp is not its commit.</b> <c>time_created</c> is taken by the database
		/// clock when the INSERT runs (see <see cref="insertSql"/>), and the row becomes visible
		/// only when its transaction commits, some time later. Two writers can therefore commit in
		/// the opposite order to their stamps: A stamps 100.00 and commits at 100.30, B stamps
		/// 100.10 and commits at 100.12. A reader that paged by a strict <c>(time, id)</c> cursor
		/// saw B at 100.15, moved past 100.10, and never saw A — whispers, party and guild lines
		/// lost for good (hot-path audit H5).
		/// </para>
		/// <para>
		/// So the pump re-reads this far behind the newest point it has settled and skips the
		/// rows it has already handled by ID. A row is caught as long as it commits within this
		/// long of its stamp. One statement per chunk inside one transaction commits in
		/// milliseconds; ten seconds is a stall, not a slow write. It lives here rather than on
		/// the reader so the writer's side of the contract and the reader's are one number.
		/// </para>
		/// </remarks>
		public const double PumpCommitWindowSeconds = 10.0;

		/// <summary>
		/// The one INSERT every chat writer uses: the scene servers' batch flush and the Discord
		/// bridge alike.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Stamped by the DATABASE clock.</b> <c>time_created</c> used to be set from whichever
		/// process wrote the row — a scene server, or the Discord bot — and every scene server's
		/// pump pages the table by it. A writer whose clock ran behind the readers stamped rows
		/// their cursors had already passed, and those rows were never delivered anywhere.
		/// <c>clock_timestamp()</c> is one clock for every writer, read per row as the statement
		/// runs, not at transaction start. <c>AT TIME ZONE 'UTC'</c> because the column holds UTC
		/// without a zone and the server's own zone is whatever it was installed with.
		/// </para>
		/// <para>
		/// <b>Idempotent per row.</b> Every row carries a request key taken once, outside the
		/// retried delegate, and the unique index on <c>request_key</c> turns a retry after a lost
		/// COMMIT reply into a no-op for every row that already landed (issue #267). It used to
		/// probe the first row's key and assume the rest; the conflict clause asks about each.
		/// </para>
		/// <para>
		/// One statement per chunk from parallel arrays: the parameter count is fixed whatever the
		/// row count, and ORDINALITY keeps the identity values in the order the rows were handed
		/// in, so <c>(time_created, id)</c> orders a burst the way it was said.
		/// </para>
		/// </remarks>
		private readonly string insertSql;

		/// <summary>
		/// Builds <see cref="insertSql"/> against the schema-qualified table.
		/// </summary>
		private static string BuildInsertSql(string tableName) => $@"INSERT INTO {tableName} (character_id, character_name, account_name, world_server_id, scene_server_id,
				server_received_time, time_created, channel, message, version, request_key)
			SELECT b.character_id, b.character_name, b.account_name, b.world_server_id, b.scene_server_id,
				b.server_received_time, clock_timestamp() AT TIME ZONE 'UTC', b.channel, b.message, 1, b.request_key
			FROM unnest({{0}}::bigint[], {{1}}::varchar[], {{2}}::varchar[], {{3}}::bigint[], {{4}}::bigint[],
				{{5}}::timestamp[], {{6}}::smallint[], {{7}}::varchar[], {{8}}::uuid[])
				WITH ORDINALITY AS b(character_id, character_name, account_name, world_server_id, scene_server_id,
					server_received_time, channel, message, request_key, ord)
			ORDER BY b.ord
			ON CONFLICT (request_key) WHERE request_key IS NOT NULL DO NOTHING";

		/// <summary>
		/// One validated row on its way to <see cref="insertSql"/>.
		/// </summary>
		private readonly struct PendingRow
		{
			public readonly long CharacterId;
			public readonly string CharacterName;
			public readonly string AccountName;
			public readonly long WorldServerId;
			public readonly long SceneServerId;
			public readonly DateTime ServerReceivedTime;
			public readonly byte Channel;
			public readonly string Message;

			public PendingRow(long characterId, string characterName, string accountName, long worldServerId,
				long sceneServerId, DateTime serverReceivedTime, byte channel, string message)
			{
				CharacterId = characterId;
				CharacterName = TruncateAuditName(characterName, MaxAuditNameLength);
				AccountName = TruncateAuditName(accountName, MaxAuditAccountLength);
				WorldServerId = worldServerId;
				SceneServerId = sceneServerId;
				ServerReceivedTime = serverReceivedTime;
				Channel = channel;
				Message = message;
			}
		}

		/// <summary>
		/// Empties a blank audit name and cuts a long one to the column width.
		/// </summary>
		private static string TruncateAuditName(string? name, int max)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return string.Empty;
			}
			return name!.Length > max ? name.Substring(0, max) : name;
		}

		/// <summary>
		/// Writes <paramref name="count"/> rows starting at <paramref name="offset"/> in one statement.
		/// </summary>
		private Task<int> InsertRowsAsync(NpgsqlDbContext dbContext, IReadOnlyList<PendingRow> rows, Guid[] requestKeys, int offset, int count, CancellationToken cancellationToken)
		{
			var characterIds = new long[count];
			var characterNames = new string[count];
			var accountNames = new string[count];
			var worldServerIds = new long[count];
			var sceneServerIds = new long[count];
			var receivedTimes = new DateTime[count];
			// short, not byte: Npgsql maps byte[] to bytea, not to smallint[].
			var channels = new short[count];
			var messages = new string[count];
			var keys = new Guid[count];

			for (int i = 0; i < count; i++)
			{
				PendingRow row = rows[offset + i];
				characterIds[i] = row.CharacterId;
				characterNames[i] = row.CharacterName;
				accountNames[i] = row.AccountName;
				worldServerIds[i] = row.WorldServerId;
				sceneServerIds[i] = row.SceneServerId;
				receivedTimes[i] = row.ServerReceivedTime;
				channels[i] = row.Channel;
				messages[i] = row.Message;
				keys[i] = requestKeys[offset + i];
			}

			return dbContext.Database.ExecuteSqlRawAsync(
				insertSql,
				new object[] { characterIds, characterNames, accountNames, worldServerIds, sceneServerIds, receivedTimes, channels, messages, keys },
				cancellationToken);
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

			var rows = new[] { new PendingRow(characterId, characterName, accountName, worldServerId, sceneServerId, serverReceivedTime, (byte)channel, message) };

			// Taken once, outside the retried delegate. See insertSql.
			var requestKeys = new[] { Guid.NewGuid() };

			// One statement, so no explicit transaction: it commits whole or not at all by itself.
			return await ExecuteWriteAsync(
				async dbContext => { await InsertRowsAsync(dbContext, rows, requestKeys, 0, 1, cancellationToken).ConfigureAwait(false); },
				saveChanges: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistBridgedAsync(
			long worldServerId,
			long sceneServerId,
			string authorName,
			string message,
			DateTime serverReceivedTime,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || sceneServerId <= 0 || string.IsNullOrWhiteSpace(message))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "World server ID, scene server ID must be greater than zero and message must not be empty.");
			}

			if (message.Length > MaxMessageLength)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Message exceeds maximum length.");
			}

			/* Character 0: a Discord author has no character. The row is a Discord-channel line
			 * the scene servers relay to every player in its world. */
			var rows = new[] { new PendingRow(0L, authorName, BridgedAccountName, worldServerId, sceneServerId, serverReceivedTime, (byte)ChatChannel.Discord, message) };
			var requestKeys = new[] { Guid.NewGuid() };

			return await ExecuteWriteAsync(
				async dbContext => { await InsertRowsAsync(dbContext, rows, requestKeys, 0, 1, cancellationToken).ConfigureAwait(false); },
				saveChanges: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);
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
			var rows = new PendingRow[messages.Count];
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

				rows[i] = new PendingRow(m.characterId, m.characterName, m.accountName, m.worldServerId, m.sceneServerId, m.serverReceivedTime, (byte)m.channel, m.message);
			}

			/* ONE transaction around every chunk. Each chunk used to commit on its own and the
			 * first failure was reported as the failure of the whole call — so a failed call could
			 * already have committed its earlier chunks. The keys below are made per call, so the
			 * caller's retry of the same list (ChatSystem re-queues a transiently refused batch and
			 * bisects any other) carries NEW keys and the conflict clause cannot recognise those
			 * rows: duplicate rows in the chat log kept for audit, and duplicate deliveries on every
			 * other scene server (issue #267 audit). A call lands whole or not at all, and a
			 * transient failure retries the whole transaction. maxBatchSize is only the size of
			 * each INSERT statement. */
			/* One key per row, taken once, outside the retried transaction: the one reply that can
			 * be lost after everything landed is the COMMIT's, and its retry then conflicts on every
			 * row instead of writing them again (issue #267). */
			var requestKeys = new Guid[messages.Count];
			for (int i = 0; i < requestKeys.Length; i++)
			{
				requestKeys[i] = Guid.NewGuid();
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				for (int offset = 0; offset < rows.Length; offset += maxBatchSize)
				{
					int batchCount = Math.Min(maxBatchSize, rows.Length - offset);
					await InsertRowsAsync(dbContext, rows, requestKeys, offset, batchCount, cancellationToken).ConfigureAwait(false);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Most rows one pump page may ask for.
		/// </summary>
		public const int MaxPumpPageSize = 1000;

		/// <summary>
		/// Most pages one pump read may walk.
		/// </summary>
		public const int MaxPumpPages = 100;

		/// <inheritdoc/>
		public async Task<DatabaseResult<ChatPumpPage>> FetchPumpAsync(ChatPumpQuery query, CancellationToken cancellationToken = default)
		{
			if (query == null)
			{
				return DatabaseResult<ChatPumpPage>.Failure(DatabaseErrorCodes.ValidationError, "A pump query is required.");
			}

			int pageSize = Math.Max(1, Math.Min(MaxPumpPageSize, query.PageSize));
			int maxPages = Math.Max(1, Math.Min(MaxPumpPages, query.MaxPages));

			long[] excludeIds = query.ExcludeIds ?? Array.Empty<long>();
			long[] worldIds = query.WorldServerIds ?? Array.Empty<long>();
			string[] partyKeys = ToKeyText(query.PartyIds);
			string[] guildKeys = ToKeyText(query.GuildIds);
			string[] tellKeys = query.TellTargetsLowerCase ?? Array.Empty<string>();

			/* The filter's arguments, {1} to {6}; the window's start is {0} and the page size {7}.
			 * See BuildPumpFilterSql. */
			object[] filterArgs = { excludeIds, query.SceneServerId, worldIds, partyKeys, guildKeys, tellKeys };
			return await ExecuteReadAsync(
				dbContext => ReadForwardAsync(dbContext, pumpFirstPageSql, pumpPageAfterSql, query.FromUtc, filterArgs, pageSize, maxPages, cancellationToken),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ChatPumpPage>> FetchRelayAsync(ChatRelayQuery query, CancellationToken cancellationToken = default)
		{
			if (query == null)
			{
				return DatabaseResult<ChatPumpPage>.Failure(DatabaseErrorCodes.ValidationError, "A relay query is required.");
			}

			int pageSize = Math.Max(1, Math.Min(MaxPumpPageSize, query.PageSize));
			int maxPages = Math.Max(1, Math.Min(MaxPumpPages, query.MaxPages));

			// {1}: the IDs already handled. See BuildRelayFilterSql.
			object[] filterArgs = { query.ExcludeIds ?? Array.Empty<long>() };
			return await ExecuteReadAsync(
				dbContext => ReadForwardAsync(dbContext, relayFirstPageSql, relayPageAfterSql, query.FromUtc, filterArgs, pageSize, maxPages, cancellationToken),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// One windowed read forward through the chat table: from the window's start, page after
		/// page in <c>(time_created, id)</c> order while pages come back full, up to
		/// <paramref name="maxPages"/>. Shared by the scene servers' pump and the Discord relay,
		/// which differ only in their filters.
		/// </summary>
		/// <param name="dbContext">The context to read through.</param>
		/// <param name="firstPageSql">
		/// Placeholders: <c>{0}</c> the window's start, then <paramref name="filterArgs"/> in order,
		/// then the page size.
		/// </param>
		/// <param name="pageAfterSql">
		/// As <paramref name="firstPageSql"/>, followed by the previous page's last
		/// <c>(time_created, id)</c>.
		/// </param>
		/// <param name="fromUtc">The window's start, or null to start at the database's "now".</param>
		/// <param name="filterArgs">The filter's own arguments.</param>
		/// <param name="pageSize">Rows per page, already clamped.</param>
		/// <param name="maxPages">Most pages, already clamped.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The rows, the database clock at the start, and whether the read caught up.</returns>
		private async Task<ChatPumpPage> ReadForwardAsync(
			NpgsqlDbContext dbContext,
			string firstPageSql,
			string pageAfterSql,
			DateTime? fromUtc,
			object[] filterArgs,
			int pageSize,
			int maxPages,
			CancellationToken cancellationToken)
		{
			/* The database clock before the first page, so "everything committed before this
			 * instant has been read" is a statement about one clock. The reader advances its
			 * window from this, never from its own. */
			DateTime readStartedUtc = DateTime.SpecifyKind(
				await ExecuteReturningAsync(
					dbContext,
					"SELECT clock_timestamp() AT TIME ZONE 'UTC'",
					Array.Empty<object>(),
					reader => reader.GetDateTime(0),
					cancellationToken).ConfigureAwait(false),
				DateTimeKind.Utc);

			DateTime start = fromUtc ?? readStartedUtc;

			var page = new ChatPumpPage
			{
				ReadStartedUtc = readStartedUtc,
			};

			bool hasAfter = false;
			DateTime afterTime = default;
			long afterId = 0;
			int limitIndex = 1 + filterArgs.Length;

			for (int pageIndex = 0; pageIndex < maxPages; pageIndex++)
			{
				object[] parameters = new object[limitIndex + (hasAfter ? 3 : 1)];
				parameters[0] = start;
				Array.Copy(filterArgs, 0, parameters, 1, filterArgs.Length);
				parameters[limitIndex] = pageSize;
				if (hasAfter)
				{
					parameters[limitIndex + 1] = afterTime;
					parameters[limitIndex + 2] = afterId;
				}

				List<ChatEntity> rows = await dbContext.Chat
					.FromSqlRaw(hasAfter ? pageAfterSql : firstPageSql, parameters)
					.AsNoTracking()
					.ToListAsync(cancellationToken).ConfigureAwait(false);

				for (int i = 0; i < rows.Count; i++)
				{
					page.Messages.Add(MapEntityToDto(rows[i]));
				}

				if (rows.Count < pageSize)
				{
					page.Drained = true;
					break;
				}

				ChatEntity last = rows[rows.Count - 1];
				hasAfter = true;
				afterTime = last.TimeCreated;
				afterId = last.ID;
			}

			return page;
		}

		/// <summary>
		/// Party and guild IDs as the text the writers prefix their messages with.
		/// </summary>
		/// <remarks>
		/// Compared as text rather than by casting the message's first word to a number, because a
		/// cast throws on a row whose first word is not one, and one such row would fail every
		/// read of the whole pump. <c>long.ToString</c> and PostgreSQL both print a positive
		/// integer as bare digits, which is also what the writers wrote.
		/// </remarks>
		private static string[] ToKeyText(long[]? ids)
		{
			if (ids == null || ids.Length == 0)
			{
				return Array.Empty<string>();
			}
			var text = new string[ids.Length];
			for (int i = 0; i < ids.Length; i++)
			{
				text[i] = ids[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
			return text;
		}

		/// <summary>
		/// The pump's page filter, shared by the first page and every page after it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Echo.</b> The origin scene server has already delivered its own World, Trade, Party,
		/// Guild and Tell lines to its own players, so it does not read them back. This does NOT
		/// make those channels local: every other scene server still reads them. Discord rows are
		/// never an echo — no scene server wrote them.
		/// </para>
		/// <para>
		/// <b>Relevance.</b> A row is read only by a server hosting someone it concerns, keyed
		/// exactly as its handler routes it: the world column for World, Trade and Discord; the
		/// first word of the message — the party ID, the guild ID — for Party and Guild, and the
		/// target's address for a Tell. The first word is <c>split_part(message, ' ', 1)</c>,
		/// which is what <c>ChatHelper.GetWordAndTrimmed</c> takes on the way in.
		/// </para>
		/// <para>
		/// <b>A tell's address</b> is a single word, or a name in double quotes when the name has a
		/// space (<c>"Aragorn of Arnor" hello</c>); the writer quotes exactly then, so a one-word
		/// target's row is what it always was (<c>FishMMO.Shared.ChatTellAddress</c>). The quoted
		/// name is <c>substring(message from '^"([^"]+)"')</c>, which is NULL for an unquoted row,
		/// so the COALESCE falls back to the first word. Taking the first word of a quoted row would
		/// be <c>"aragorn</c>, and a whisper to a name with a space would reach nobody off the
		/// sender's server. Matched through <c>lower()</c> because the writer stores the target as
		/// the sender typed it; character names are ASCII letters and single spaces, so
		/// <c>lower()</c> and <c>ToLowerInvariant</c> agree.
		/// </para>
		/// <para>
		/// <b>Order and index.</b> <c>(time_created, id)</c> ascending on
		/// <c>ix_chat_time_created_id</c>, range-scanned from the window's start. The scan reads
		/// every row in the window and the filters discard the irrelevant ones there, which is
		/// far cheaper than shipping them to every server and discarding them after.
		/// </para>
		/// </remarks>
		private static string BuildPumpFilterSql() =>
			$@"c.time_created >= {{0}}
				AND c.id <> ALL({{1}}::bigint[])
				AND NOT (c.scene_server_id = {{2}} AND c.channel IN ({(byte)ChatChannel.World}, {(byte)ChatChannel.Trade}, {(byte)ChatChannel.Party}, {(byte)ChatChannel.Guild}, {(byte)ChatChannel.Tell}))
				AND (
					(c.channel IN ({(byte)ChatChannel.World}, {(byte)ChatChannel.Trade}, {(byte)ChatChannel.Discord}) AND c.world_server_id = ANY({{3}}::bigint[]))
					OR (c.channel = {(byte)ChatChannel.Party} AND split_part(c.message, ' ', 1) = ANY({{4}}::text[]))
					OR (c.channel = {(byte)ChatChannel.Guild} AND split_part(c.message, ' ', 1) = ANY({{5}}::text[]))
					OR (c.channel = {(byte)ChatChannel.Tell} AND lower(COALESCE(substring(c.message from '^""([^""]+)""'), split_part(c.message, ' ', 1))) = ANY({{6}}::text[]))
				)";

		/// <summary>
		/// The Discord relay's page filter: every game row in the window it has not handled.
		/// </summary>
		/// <remarks>
		/// No relevance and no echo rule: the relay scans every row for account-link codes and its
		/// own allowlist decides what it republishes. Discord rows are left out so the relay can
		/// never read back what it bridged in. Same order and index as the pump.
		/// </remarks>
		private static string BuildRelayFilterSql() =>
			$@"c.time_created >= {{0}}
				AND c.id <> ALL({{1}}::bigint[])
				AND c.channel <> {(byte)ChatChannel.Discord}";

		/// <summary>First page of a relay read: from the window's start.</summary>
		private readonly string relayFirstPageSql;

		/// <summary>Every later page of the same relay read. See <see cref="pumpPageAfterSql"/>.</summary>
		private readonly string relayPageAfterSql;

		/// <summary>First page of a pump read: from the window's start.</summary>
		private readonly string pumpFirstPageSql;

		/// <summary>
		/// Every later page of the same read: strictly after the previous page's last row. Within
		/// one read that is exact; the window between reads is what the trailing start is for.
		/// </summary>
		private readonly string pumpPageAfterSql;

		/// <inheritdoc/>
		public async Task<DatabaseResult<ChatAdminPage>> SearchAdminAsync(ChatAdminQuery query, CancellationToken cancellationToken = default)
		{
			query ??= new ChatAdminQuery();

			int page = query.Page < 1 ? 1 : query.Page;
			int pageSize = query.PageSize < 1 ? ChatAdminQuery.DefaultPageSize : query.PageSize;
			if (pageSize > ChatAdminQuery.MaxPageSize)
			{
				pageSize = ChatAdminQuery.MaxPageSize;
			}

			string characterName = string.IsNullOrWhiteSpace(query.CharacterName) ? null : query.CharacterName.Trim();
			string accountName = string.IsNullOrWhiteSpace(query.AccountName) ? null : query.AccountName.Trim();
			string text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim();

			if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.ToUtc.Value <= query.FromUtc.Value)
			{
				return DatabaseResult<ChatAdminPage>.Failure(DatabaseErrorCodes.ValidationError,
					"The end of the time range must be after its start.");
			}

			/* An unbounded message search is REFUSED, not quietly bounded.
			 *
			 * The text filter below is a substring match — a leading wildcard — and no btree can
			 * serve one. That is not a mistake to be engineered away: the report an operator is
			 * holding says "they called me <slur>", and the slur is in the middle of the sentence,
			 * so "find where they said this word" is the entire feature and it costs a scan. What
			 * a scan must not be allowed to be is a scan of the WHOLE table, on a table that grows
			 * by every line every player types, forever.
			 *
			 * So a text search has to arrive with a character name, an account name, or a lower
			 * time bound beside it. Those are the three filters that actually reduce what is read:
			 * the names cut the scan to one player's traffic, and FromUtc lets the planner start a
			 * range scan on the time_created index instead of at the beginning of history.
			 *
			 * The alternative — defaulting the window to, say, the last seven days when a caller
			 * supplies none — was rejected deliberately. It turns "I searched and there is nothing"
			 * into a lie: the operator asked the whole history for a word, got an empty table back,
			 * and has no way to see that the question they asked was silently replaced with a
			 * narrower one. A false negative here closes a harassment report on the grounds that
			 * the message does not exist. A refusal that names the three filters that would work
			 * costs one extra click and cannot be misread.
			 *
			 * ToUtc and Channel deliberately do not count as narrowing; ChatAdminQuery.HasNarrowingFilter
			 * says why. */
			if (text != null && !query.HasNarrowingFilter)
			{
				return DatabaseResult<ChatAdminPage>.Failure(DatabaseErrorCodes.ValidationError,
					"A message search has to be narrowed. Add a character name, an account name, or a date to search from.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<ChatEntity> q = dbContext.Chat.AsNoTracking();

				/* character_name and account_name carry NO index. The existing ones are
				 * (character_id, time_created), (scene_server_id, channel), (time_created),
				 * (time_created, id) and (world_server_id), so a name filter is a sequential scan
				 * with a filter on top, not a seek — an honest note rather than a comment claiming
				 * a lookup the database is not doing. It is bounded in practice because an operator
				 * pairs it with a time range, and combining it with FromUtc lets the planner work
				 * from the time_created index and test the name on the rows it finds.
				 *
				 * Matched case-insensitively. Since there is no index to forfeit, lower() on both
				 * sides is free here in a way it would not be on accounts.name_lowercase, and it
				 * removes the failure mode where a name typed with the wrong capitalisation
				 * answers "no messages" — which in an abuse investigation reads as "it never
				 * happened". */
				if (characterName != null)
				{
					string lowered = characterName.ToLowerInvariant();
					q = q.Where(e => e.CharacterName.ToLower() == lowered);
				}
				if (accountName != null)
				{
					string lowered = accountName.ToLowerInvariant();
					q = q.Where(e => e.AccountName.ToLower() == lowered);
				}
				if (query.Channel.HasValue)
				{
					byte channel = query.Channel.Value;
					q = q.Where(e => e.Channel == channel);
				}
				if (query.FromUtc.HasValue)
				{
					DateTime from = query.FromUtc.Value;
					q = q.Where(e => e.TimeCreated >= from);
				}
				if (query.ToUtc.HasValue)
				{
					DateTime to = query.ToUtc.Value;
					q = q.Where(e => e.TimeCreated < to);
				}
				if (text != null)
				{
					/* Contains over a parameter, which Npgsql renders as strpos(lower(message), $1) > 0
					 * rather than LIKE. That matters for correctness as well as speed: with LIKE, a
					 * '%' or '_' inside what the player typed would be a wildcard, so searching for
					 * the literal message "50% off" would match text it does not contain. Through
					 * strpos every character the operator pastes is literal.
					 *
					 * lower() on both sides for the same reason as the names above: a report quotes
					 * a sentence, not its capitalisation. */
					string lowered = text.ToLowerInvariant();
					q = q.Where(e => e.Message.ToLower().Contains(lowered));
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				/* Newest first, because a report is about something that just happened, with ID
				 * descending as the tie-break: TimeCreated is the database clock at the INSERT, so
				 * rows written by the Discord bridge and by two scene servers in the same instant
				 * can share a value, and without the second key the order among them is whatever
				 * the planner felt like. An exchange read back in the wrong order reads as a
				 * different conversation. */
				var rows = await q
					.OrderByDescending(e => e.TimeCreated)
					.ThenByDescending(e => e.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new ChatAdminPage
				{
					Items = rows.Select(MapEntityToAdminDto).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps ChatEntity to the operator-facing ChatAdminData DTO.
		/// </summary>
		/// <param name="entity">Chat entity from database.</param>
		/// <returns>Operator chat data DTO.</returns>
		/// <remarks>
		/// Separate from <see cref="MapEntityToDto"/> rather than sharing it: the two DTOs answer
		/// different questions, and a shared mapper is how a field added for the game's transfer
		/// shape ends up on an operator screen, or the reverse.
		/// </remarks>
		private ChatAdminData MapEntityToAdminDto(ChatEntity entity)
		{
			return new ChatAdminData
			{
				ID = entity.ID,
				CharacterID = entity.CharacterID,
				CharacterName = entity.CharacterName ?? string.Empty,
				AccountName = entity.AccountName ?? string.Empty,
				WorldServerID = entity.WorldServerID,
				SceneServerID = entity.SceneServerID,
				Channel = entity.Channel,
				Message = entity.Message ?? string.Empty,
				ServerReceivedTime = entity.ServerReceivedTime,
				TimeCreated = entity.TimeCreated,
			};
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