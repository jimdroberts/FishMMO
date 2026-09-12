using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Reads guilds, rank ladders, guild logs and parties for the operator's social board.
	/// </summary>
	/// <remarks>
	/// <para>
	/// EF throughout, every read <c>AsNoTracking</c>, and every list read runs a fixed number of
	/// statements whatever the page size — see the interface. The one pattern worth naming: a
	/// count that belongs to a listed row is projected into that row's own SELECT as a
	/// correlated subquery, and anything needing a second table for a whole page is fetched once
	/// by the IDs on the page. Neither ever becomes a query per row.
	/// </para>
	/// <para>
	/// Nothing here writes, and the class exposes no method that could. See the interface for
	/// why that is a rule and not an omission.
	/// </para>
	/// </remarks>
	public sealed class SocialBoardService : BaseService<GuildEntity>, ISocialBoardService
	{
		/// <summary>The most guilds or parties one page will return.</summary>
		private const int MaxPageSize = 100;

		/// <summary>The most log entries one page will return.</summary>
		/// <remarks>
		/// Higher than the row pages above because a log entry is one short line and an operator
		/// reading a guild's history is scanning for a moment, not studying rows.
		/// </remarks>
		private const int MaxLogPageSize = 200;

		public SocialBoardService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildAdminPage>> SearchGuildsAsync(
			string query,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			ClampPaging(ref page, ref pageSize, 25, MaxPageSize);

			// Matched against the lower-cased computed column, so the unique index on it serves
			// the prefix and no row has LOWER() run over it.
			string term = (query ?? string.Empty).Trim().ToLowerInvariant();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<GuildEntity> q = dbContext.Guilds.AsNoTracking();
				if (term.Length > 0)
				{
					q = q.Where(g => g.NameLowercase.StartsWith(term));
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				/* Statement two. Both counts ride along as correlated subqueries in this same
				 * SELECT: counting them afterwards would be two round trips per listed guild,
				 * for a page the operator has not clicked into yet. */
				var rows = await q
					.OrderBy(g => g.NameLowercase)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.Select(g => new GuildAdminData
					{
						ID = g.ID,
						Name = g.Name,
						MemberCount = g.Characters.Count(),
						RankCount = g.Ranks.Count(),
						IsRecruiting = g.IsRecruiting,
						Tags = g.Tags,
						Blurb = g.Blurb,
						TimeCreated = g.TimeCreated,
						Version = g.Version,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (rows.Count > 0)
				{
					List<long> ids = rows.Select(r => r.ID).ToList();
					await AttachLeadersAsync(dbContext, rows, ids, cancellationToken).ConfigureAwait(false);
				}

				return new GuildAdminPage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildAdminDetail>> FetchGuildAsync(
			long guildId,
			CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<GuildAdminDetail>.Failure(DatabaseErrorCodes.ValidationError, "A guild ID is required.");
			}

			return await ExecuteReadAsync<GuildAdminDetail>(async dbContext =>
			{
				GuildAdminDetail detail = await dbContext.Guilds
					.AsNoTracking()
					.Where(g => g.ID == guildId)
					.Select(g => new GuildAdminDetail
					{
						ID = g.ID,
						Name = g.Name,
						Notice = g.Notice,
						MessageOfTheDay = g.MessageOfTheDay,
						Blurb = g.Blurb,
						Tags = g.Tags,
						IsRecruiting = g.IsRecruiting,
						TimeCreated = g.TimeCreated,
						Version = g.Version,
					})
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (detail == null)
				{
					// Thrown rather than returned so the base class maps it to a NOT_FOUND
					// failure, the way every other service in this assembly reports a miss.
					throw new Exceptions.DatabaseEntityNotFoundException("Guild", guildId.ToString());
				}

				List<GuildRankAdminData> ranks = await dbContext.GuildRanks
					.AsNoTracking()
					.Where(r => r.GuildID == guildId)
					.OrderByDescending(r => r.RankOrder)
					.Select(r => new GuildRankAdminData
					{
						ID = r.ID,
						RankOrder = r.RankOrder,
						Name = r.Name,
						Permissions = r.Permissions,
						TimeCreated = r.TimeCreated,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				/* The roster joins the character row through the membership's navigation, so the
				 * names arrive with it. Reading the memberships and then looking up each name
				 * would be the N+1 this whole service is shaped to avoid. */
				List<GuildMemberAdminData> members = await dbContext.CharacterGuilds
					.AsNoTracking()
					.Where(m => m.GuildID == guildId)
					.OrderByDescending(m => m.Rank)
					.ThenBy(m => m.TimeCreated)
					.ThenBy(m => m.CharacterID)
					.Select(m => new GuildMemberAdminData
					{
						ID = m.ID,
						CharacterID = m.CharacterID,
						CharacterName = m.Character.Name,
						CharacterDeleted = m.Character.Deleted,
						SessionState = (int)m.Character.SessionState,
						SessionLeaseExpiresUtc = m.Character.SessionLeaseExpiresUtc,
						Rank = m.Rank,
						Location = m.Location,
						PublicNote = m.PublicNote,
						TimeCreated = m.TimeCreated,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<GuildApplicantAdminData> applications = await dbContext.GuildApplications
					.AsNoTracking()
					.Where(a => a.GuildID == guildId)
					.OrderBy(a => a.TimeCreated)
					.Select(a => new GuildApplicantAdminData
					{
						ID = a.ID,
						CharacterID = a.CharacterID,
						CharacterName = a.Character.Name,
						Message = a.Message,
						TimeCreated = a.TimeCreated,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				/* Rank names, per-rank counts and the leader flag are all derived from the two
				 * lists already in hand. Asking the database for any of them would be a query
				 * that answers a question the rows on this page already answer. */
				Dictionary<byte, string> rankNames = new Dictionary<byte, string>();
				foreach (GuildRankAdminData rank in ranks)
				{
					rankNames[rank.RankOrder] = rank.Name;
				}

				/* Leadership is decided the way the game server decides it in GuildAuthority:
				 * the leader's seat is the highest rung the LADDER defines, and a member leads
				 * if their stored order reaches it. Deliberately not "the highest order any
				 * member holds" — by that rule the last member of an empty-laddered guild would
				 * be shown as its leader, and a guild whose leader has left would silently
				 * promote whoever is left standing on this page and nowhere else. */
				byte ladderTop = ranks.Count > 0 ? ranks.Max(r => r.RankOrder) : (byte)0;

				foreach (GuildMemberAdminData member in members)
				{
					member.CharacterName = DisplayName(member.CharacterName, member.CharacterDeleted);
					member.RankName = rankNames.TryGetValue(member.Rank, out string name) ? name : null;
					member.IsLeader = ladderTop > 0 && member.Rank >= ladderTop;
				}

				foreach (GuildRankAdminData rank in ranks)
				{
					rank.MemberCount = members.Count(m => m.Rank == rank.RankOrder);
				}

				foreach (GuildApplicantAdminData applicant in applications)
				{
					// An applicant can have been deleted while the application still stands: the
					// application table holds no cascade from the character row.
					applicant.CharacterName = CharacterService.StripDeletedSuffix(applicant.CharacterName);
				}

				detail.Ranks = ranks;
				detail.Members = members;
				detail.Applications = applications;
				return detail;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildLogPage>> FetchGuildLogAsync(
			long guildId,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<GuildLogPage>.Failure(DatabaseErrorCodes.ValidationError, "A guild ID is required.");
			}

			ClampPaging(ref page, ref pageSize, 25, MaxLogPageSize);

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<GuildLogEntity> q = dbContext.GuildLogs
					.AsNoTracking()
					.Where(l => l.GuildID == guildId);

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					// Newest first, and by ID within the same instant: two entries written in
					// the same transaction share a timestamp, and an unstable order would let
					// one of them appear on two pages and the other on none.
					.OrderByDescending(l => l.TimeCreated)
					.ThenByDescending(l => l.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.Select(l => new GuildLogAdminData
					{
						ID = l.ID,
						GuildID = l.GuildID,
						EventType = (GuildLogEventType)l.EventType,
						ActorCharacterID = l.ActorCharacterID,
						TargetCharacterID = l.TargetCharacterID,
						Detail = l.Detail,
						TimeCreated = l.TimeCreated,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (rows.Count > 0)
				{
					/* One name lookup for the whole page. The log carries no navigation to the
					 * characters table — it is append-only and deliberately keeps no foreign key
					 * to a row that may be deleted — so the names are resolved by ID here, and a
					 * character that no longer exists simply has no name. */
					List<long> ids = rows
						.SelectMany(r => new[] { r.ActorCharacterID, r.TargetCharacterID })
						.Where(id => id != 0)
						.Distinct()
						.ToList();

					if (ids.Count > 0)
					{
						Dictionary<long, string> names = await FetchNamesAsync(dbContext, ids, cancellationToken).ConfigureAwait(false);
						foreach (GuildLogAdminData row in rows)
						{
							row.ActorName = names.TryGetValue(row.ActorCharacterID, out string actor) ? actor : null;
							row.TargetName = names.TryGetValue(row.TargetCharacterID, out string target) ? target : null;
						}
					}
				}

				return new GuildLogPage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PartyAdminPage>> FetchPartiesAsync(
			long? worldServerId,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			ClampPaging(ref page, ref pageSize, 25, MaxPageSize);

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<PartyEntity> q = dbContext.Parties.AsNoTracking();
				if (worldServerId.HasValue)
				{
					long wanted = worldServerId.Value;
					q = q.Where(p => p.WorldServerID == wanted);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					// Newest first: a party is a transient row, and the one an operator is
					// being asked about is almost always one that formed recently.
					.OrderByDescending(p => p.TimeCreated)
					.ThenByDescending(p => p.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.Select(p => new PartyAdminData
					{
						ID = p.ID,
						WorldServerID = p.WorldServerID,
						MemberCount = p.Characters.Count(),
						TimeCreated = p.TimeCreated,
						Version = p.Version,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (rows.Count > 0)
				{
					// One roster query for the whole page, keyed by the IDs on it.
					List<long> ids = rows.Select(r => r.ID).ToList();

					/* The party ID is what buckets the members, but it has no place on a member
					 * row — every member in a bucket has the same one — so it rides beside the
					 * projection in an anonymous wrapper instead of being read back separately. */
					var members = await dbContext.CharacterParties
						.AsNoTracking()
						.Where(m => ids.Contains(m.PartyID))
						.OrderByDescending(m => m.Rank)
						.ThenBy(m => m.TimeCreated)
						.ThenBy(m => m.CharacterID)
						.Select(m => new
						{
							m.PartyID,
							Member = new PartyMemberAdminData
							{
								ID = m.ID,
								CharacterID = m.CharacterID,
								CharacterName = m.Character.Name,
								CharacterDeleted = m.Character.Deleted,
								SceneName = m.Character.SceneName,
								SessionState = (int)m.Character.SessionState,
								SessionLeaseExpiresUtc = m.Character.SessionLeaseExpiresUtc,
								Rank = m.Rank,
								HealthPCT = m.HealthPCT,
								TimeCreated = m.TimeCreated,
							},
						})
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false);

					Dictionary<long, List<PartyMemberAdminData>> byParty = new Dictionary<long, List<PartyMemberAdminData>>();
					foreach (var row in members)
					{
						PartyMemberAdminData member = row.Member;
						member.CharacterName = DisplayName(member.CharacterName, member.CharacterDeleted);
						if (!byParty.TryGetValue(row.PartyID, out List<PartyMemberAdminData> bucket))
						{
							bucket = new List<PartyMemberAdminData>();
							byParty[row.PartyID] = bucket;
						}
						bucket.Add(member);
					}

					foreach (PartyAdminData party in rows)
					{
						// A party with nobody in it is an ordinary row and keeps its empty list.
						party.Members = byParty.TryGetValue(party.ID, out List<PartyMemberAdminData> roster)
							? roster
							: (IReadOnlyList<PartyMemberAdminData>)Array.Empty<PartyMemberAdminData>();
					}
				}

				return new PartyAdminPage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Fills in the leader of every guild on a page, in one query.
		/// </summary>
		/// <remarks>
		/// <para>
		/// There is no leader column. A member leads when their stored rank order reaches the
		/// highest rung their guild's ladder defines, which is exactly the test the game server
		/// applies in <c>GuildAuthority.IsLeader</c>. Expressed as two <c>EXISTS</c> clauses —
		/// the guild has a ladder, and no rung on it sits above this member — it is one
		/// statement for the whole page instead of a "max rung, then who holds it" pair of round
		/// trips per guild.
		/// </para>
		/// <para>
		/// Several members can reach the seat: <c>rank_order</c> is unique per rank row, not per
		/// member, and the game's test is "at or above" rather than "equal to". When that
		/// happens the earliest to join is named and the row is marked ambiguous, because
		/// silently picking one would show an operator a leader the guild does not agree it has.
		/// A guild with no ranks at all has no seat and so no leader.
		/// </para>
		/// </remarks>
		private static async Task AttachLeadersAsync(
			NpgsqlDbContext dbContext,
			List<GuildAdminData> rows,
			List<long> guildIds,
			CancellationToken cancellationToken)
		{
			var candidates = await dbContext.CharacterGuilds
				.AsNoTracking()
				.Where(m => guildIds.Contains(m.GuildID))
				.Where(m => dbContext.GuildRanks.Any(r => r.GuildID == m.GuildID))
				.Where(m => !dbContext.GuildRanks.Any(r => r.GuildID == m.GuildID && r.RankOrder > m.Rank))
				.OrderBy(m => m.TimeCreated)
				.ThenBy(m => m.CharacterID)
				.Select(m => new
				{
					m.GuildID,
					m.CharacterID,
					Name = m.Character.Name,
					Deleted = m.Character.Deleted,
				})
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			Dictionary<long, int> tally = new Dictionary<long, int>();
			Dictionary<long, GuildAdminData> byId = new Dictionary<long, GuildAdminData>();
			foreach (GuildAdminData row in rows)
			{
				byId[row.ID] = row;
			}

			foreach (var candidate in candidates)
			{
				tally.TryGetValue(candidate.GuildID, out int seen);
				tally[candidate.GuildID] = seen + 1;

				if (!byId.TryGetValue(candidate.GuildID, out GuildAdminData row))
				{
					continue;
				}
				if (seen == 0)
				{
					// Ordered by join time above, so the first one seen is the earliest.
					row.LeaderCharacterID = candidate.CharacterID;
					row.LeaderName = DisplayName(candidate.Name, candidate.Deleted);
				}
				else
				{
					row.LeaderIsAmbiguous = true;
				}
			}
		}

		/// <summary>Character names for a set of IDs, in one query.</summary>
		private static async Task<Dictionary<long, string>> FetchNamesAsync(
			NpgsqlDbContext dbContext,
			List<long> characterIds,
			CancellationToken cancellationToken)
		{
			var rows = await dbContext.Characters
				.AsNoTracking()
				.Where(c => characterIds.Contains(c.ID))
				.Select(c => new { c.ID, c.Name, c.Deleted })
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			Dictionary<long, string> names = new Dictionary<long, string>(rows.Count);
			foreach (var row in rows)
			{
				names[row.ID] = DisplayName(row.Name, row.Deleted);
			}
			return names;
		}

		/// <summary>
		/// The name to show for a character row, which for a deleted one is the name it had.
		/// </summary>
		/// <remarks>
		/// Deletion renames the row to free the name for reuse, so a deleted member left in a
		/// guild would otherwise be shown as <c>Bob_DELETED_2f3a…</c>. The stripping rule lives
		/// in <see cref="CharacterService"/> and is called rather than repeated.
		/// </remarks>
		private static string DisplayName(string storedName, bool deleted)
		{
			return deleted ? CharacterService.StripDeletedSuffix(storedName) : storedName;
		}

		/// <summary>Applies the 1-based page floor and the service's page-size ceiling.</summary>
		private static void ClampPaging(ref int page, ref int pageSize, int fallbackSize, int maxSize)
		{
			if (page < 1)
			{
				page = 1;
			}
			if (pageSize < 1)
			{
				pageSize = fallbackSize;
			}
			if (pageSize > maxSize)
			{
				pageSize = maxSize;
			}
		}
	}
}
