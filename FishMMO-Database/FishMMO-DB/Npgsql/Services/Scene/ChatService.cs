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
				 * descending as the tie-break: TimeCreated is stamped per persistence batch, so a
				 * whole burst of messages can share one value, and without the second key the
				 * order inside that burst is whatever the planner felt like. An exchange read back
				 * in the wrong order reads as a different conversation. */
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