using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Beta codes and account links. Raw SQL for the writes whose WHERE clause is the concurrency
	/// control, EF for reads.
	/// </summary>
	/// <remarks>
	/// Every raw statement names its table through <c>TableName</c> or
	/// <c>GetTableName&lt;T&gt;()</c>, both schema-qualified: an unqualified name resolves against
	/// the connection's <c>search_path</c> and silently misses the model's schema where they
	/// differ.
	/// </remarks>
	public sealed class BetaCodeService : BaseService<BetaCodeEntity>, IBetaCodeService
	{
		private const int MaxMintCount = 500;
		private const int MaxUsesLimit = 100000;
		private const int DefaultPageSize = 50;
		private const int MaxPageSize = 200;
		private const int StaffNameMaxLength = 50;
		private const int NoteMaxLength = 256;

		/// <summary>
		/// How many times one slot in a batch is regenerated after a code collision before the
		/// whole mint gives up.
		/// </summary>
		private const int MaxCollisionRetries = 8;

		/// <summary>
		/// The single answer for every code that cannot be redeemed, whatever the reason.
		/// </summary>
		private const string InvalidCodeMessage = "That beta code is not valid.";

		private const string AlreadyOnAccountMessage = "That code is already on this account.";

		/// <summary>
		/// The column list every raw read of a code maps. <c>xmin</c> goes through text to bigint
		/// so nothing downstream ever holds an unbindable <c>uint</c> it might pass back.
		/// </summary>
		private const string CodeColumns =
			"id, (xmin::text)::bigint, code, program, max_uses, use_count, expires_utc, revoked_utc, revoked_by, created_by, created_utc, note";

		/// <summary>Creates the service.</summary>
		public BetaCodeService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<BetaCodeData>>> MintAsync(
			string program,
			int count,
			int maxUses,
			DateTime? expiresUtc,
			string createdBy,
			string? note,
			CancellationToken cancellationToken = default)
		{
			string normalizedProgram = BetaCodeFormat.NormalizeProgram(program);
			if (!BetaCodeFormat.IsValidProgram(normalizedProgram))
			{
				return DatabaseResult<IReadOnlyList<BetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, BetaCodeFormat.InvalidProgramError);
			}
			if (count < 1 || count > MaxMintCount)
			{
				return DatabaseResult<IReadOnlyList<BetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, $"Mint between 1 and {MaxMintCount} codes at a time.");
			}
			if (maxUses < 1 || maxUses > MaxUsesLimit)
			{
				return DatabaseResult<IReadOnlyList<BetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, $"Uses per code must be between 1 and {MaxUsesLimit}.");
			}
			if (expiresUtc.HasValue && expiresUtc.Value <= DateTime.UtcNow)
			{
				return DatabaseResult<IReadOnlyList<BetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, "Expiry must be in the future.");
			}

			string? staff = CleanStaffName(createdBy);
			if (staff == null)
			{
				return DatabaseResult<IReadOnlyList<BetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, "A staff account is required.");
			}

			string? trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
			if (trimmedNote != null && trimmedNote.Length > NoteMaxLength)
			{
				return DatabaseResult<IReadOnlyList<BetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, $"The note must be {NoteMaxLength} characters or fewer.");
			}

			return await ExecuteTransactionAsync<IReadOnlyList<BetaCodeData>>(async dbContext =>
			{
				/* created_utc is left to the column default. Inside one transaction that is the
				 * transaction's start time, so a batch shares one timestamp and sorts together. */
				var sql = $@"INSERT INTO {TableName} (code, program, max_uses, use_count, expires_utc, created_by, note)
					VALUES ({{0}}, {{1}}, {{2}}, 0, {{3}}, {{4}}, {{5}})
					ON CONFLICT (code) DO NOTHING
					RETURNING {CodeColumns}";

				var minted = new List<BetaCodeData>(count);
				DateTime readAt = DateTime.UtcNow;

				for (int i = 0; i < count; i++)
				{
					/* DO NOTHING rather than catching 23505: a unique violation aborts the whole
					 * PostgreSQL transaction, so one collision would throw away every code minted
					 * before it. A skipped insert returns no row and the slot is simply retried
					 * with a fresh code. The index sees this transaction's own rows, so a
					 * duplicate within the batch is caught the same way. */
					BetaCodeEntity? row = null;
					for (int attempt = 0; attempt < MaxCollisionRetries && row == null; attempt++)
					{
						row = await ExecuteReturningOrDefaultAsync(
							dbContext,
							sql,
							new object[]
							{
								BetaCodeFormat.Generate(),
								normalizedProgram,
								maxUses,
								expiresUtc.HasValue ? (object)expiresUtc.Value : DBNull.Value,
								staff,
								trimmedNote != null ? (object)trimmedNote : DBNull.Value,
							},
							ReadCode,
							cancellationToken).ConfigureAwait(false);
					}

					if (row == null)
					{
						throw new DatabaseException(
							"Could not generate unique beta codes. Try again.",
							errorCode: DatabaseErrorCodes.DatabaseError);
					}

					minted.Add(Map(row, readAt));
				}

				return minted;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<BetaCodePage>> SearchAsync(
			BetaCodeQuery query,
			CancellationToken cancellationToken = default)
		{
			query ??= new BetaCodeQuery();

			int page = query.Page < 1 ? 1 : query.Page;
			int pageSize = query.PageSize < 1 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);
			string? program = string.IsNullOrWhiteSpace(query.Program) ? null : BetaCodeFormat.NormalizeProgram(query.Program);
			bool includeRevoked = query.IncludeRevoked;

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<BetaCodeEntity> q = dbContext.BetaCodes.AsNoTracking();

				if (program != null)
				{
					q = q.Where(c => c.Program == program);
				}
				if (!includeRevoked)
				{
					q = q.Where(c => c.RevokedUtc == null);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					.OrderByDescending(c => c.CreatedUtc)
					.ThenByDescending(c => c.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				DateTime now = DateTime.UtcNow;
				return new BetaCodePage
				{
					Items = rows.Select(r => Map(r, now)).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<BetaProgramSummary>>> ListProgramsAsync(
			CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync<IReadOnlyList<BetaProgramSummary>>(async dbContext =>
			{
				DateTime now = DateTime.UtcNow;

				/* "Active" is "could be redeemed right now" — the same three conditions as the
				 * redemption UPDATE. A code that is merely unrevoked but used up or expired
				 * cannot admit anyone further, and counting it would tell staff a program has
				 * open places it does not have. */
				var codeTotals = await dbContext.BetaCodes
					.AsNoTracking()
					.GroupBy(c => c.Program)
					.Select(g => new
					{
						Program = g.Key,
						CodeCount = g.Count(),
						ActiveCodeCount = g.Sum(c =>
							c.RevokedUtc == null &&
							(c.ExpiresUtc == null || c.ExpiresUtc > now) &&
							c.UseCount < c.MaxUses ? 1 : 0),
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var linkTotals = await dbContext.AccountBetaCodes
					.AsNoTracking()
					.GroupBy(l => l.Program)
					.Select(g => new { Program = g.Key, Count = g.Count() })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var byProgram = new Dictionary<string, BetaProgramSummary>(StringComparer.Ordinal);
				foreach (var row in codeTotals)
				{
					byProgram[row.Program] = new BetaProgramSummary
					{
						Program = row.Program,
						CodeCount = row.CodeCount,
						ActiveCodeCount = row.ActiveCodeCount,
					};
				}
				foreach (var row in linkTotals)
				{
					if (!byProgram.TryGetValue(row.Program, out var summary))
					{
						// Unreachable while codes are never deleted, but a link must never vanish
						// from the totals because its code row did.
						summary = new BetaProgramSummary { Program = row.Program };
						byProgram[row.Program] = summary;
					}
					summary.RedemptionCount = row.Count;
				}

				return byProgram.Values
					.OrderBy(s => s.Program, StringComparer.Ordinal)
					.ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RevokeAsync(
			long id,
			string revokedBy,
			CancellationToken cancellationToken = default)
		{
			if (id <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Beta code ID must be greater than 0.");
			}

			string? staff = CleanStaffName(revokedBy);
			if (staff == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A staff account is required.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET revoked_utc = {{0}}, revoked_by = {{1}}
					WHERE id = {{2}} AND revoked_utc IS NULL";

				int affected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { DateTime.UtcNow, staff, id }, cancellationToken)
					.ConfigureAwait(false);

				if (affected == 1)
				{
					return;
				}

				/* Only to choose the right failure. The UPDATE above already decided nothing
				 * changed; this read cannot make it change anything. */
				bool exists = await dbContext.BetaCodes
					.AsNoTracking()
					.AnyAsync(c => c.ID == id, cancellationToken)
					.ConfigureAwait(false);

				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("BetaCode", id.ToString());
				}

				throw new DatabaseException("That code is already revoked.", errorCode: DatabaseErrorCodes.ValidationError);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<AccountBetaCodeData>> RedeemAsync(
			string accountName,
			string code,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<AccountBetaCodeData>.Failure(
					DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string? normalizedCode = BetaCodeFormat.Normalize(code);
			if (normalizedCode == null)
			{
				return DatabaseResult<AccountBetaCodeData>.Failure(
					DatabaseErrorCodes.ValidationError, InvalidCodeMessage);
			}

			string account = Authentication.NormalizeAccountLookup(accountName);

			return await ExecuteTransactionAsync<AccountBetaCodeData>(async dbContext =>
			{
				/* Checked before the use is taken, so re-entering a code the account already has
				 * costs the code nothing. The copied code column is equivalent to joining on
				 * beta_code_id: codes are unique and never change after minting. This read is a
				 * courtesy, not the guard — a simultaneous double submit gets past it, and the
				 * unique index on the insert below is what stops that one. */
				bool alreadyLinked = await dbContext.AccountBetaCodes
					.AsNoTracking()
					.AnyAsync(l => l.AccountName == account && l.Code == normalizedCode, cancellationToken)
					.ConfigureAwait(false);

				if (alreadyLinked)
				{
					throw new DatabaseException(AlreadyOnAccountMessage, errorCode: DatabaseErrorCodes.ValidationError);
				}

				/* The gate and the take in one statement. A read-then-increment would let two
				 * accounts both see one use left and both spend it; here the second UPDATE waits on
				 * the first's row lock, re-evaluates use_count < max_uses against the committed
				 * value, and matches nothing. */
				var claimSql = $@"UPDATE {TableName} SET use_count = use_count + 1
					WHERE code = {{0}}
					  AND revoked_utc IS NULL
					  AND (expires_utc IS NULL OR expires_utc > {{1}})
					  AND use_count < max_uses
					RETURNING id, program";

				BetaCodeEntity? claimed = await ExecuteReturningOrDefaultAsync(
					dbContext,
					claimSql,
					new object[] { normalizedCode, DateTime.UtcNow },
					reader => new BetaCodeEntity { ID = reader.GetInt64(0), Program = reader.GetString(1) },
					cancellationToken).ConfigureAwait(false);

				if (claimed == null)
				{
					/* Unknown, revoked, expired and used up are one answer on purpose — this runs
					 * on anonymous registration, where a distinguishable "expired" or "used up"
					 * would confirm to a guesser that they had found a real code. */
					throw new DatabaseEntityNotFoundException("BetaCode", "by code", InvalidCodeMessage);
				}

				string linkTable = dbContext.GetTableName<AccountBetaCodeEntity>();
				var linkSql = $@"INSERT INTO {linkTable} (account_name, beta_code_id, code, program)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}})
					ON CONFLICT (account_name, beta_code_id) DO NOTHING
					RETURNING redeemed_utc";

				DateTime? redeemedUtc = await ExecuteReturningOrDefaultAsync<DateTime?>(
					dbContext,
					linkSql,
					new object[] { account, claimed.ID, normalizedCode, claimed.Program },
					reader => reader.GetDateTime(0),
					cancellationToken).ConfigureAwait(false);

				if (redeemedUtc == null)
				{
					/* The same account won a race with itself. Throwing rolls this transaction
					 * back, returning the use the UPDATE above just took. */
					throw new DatabaseException(AlreadyOnAccountMessage, errorCode: DatabaseErrorCodes.ValidationError);
				}

				return new AccountBetaCodeData
				{
					AccountName = account,
					BetaCodeID = claimed.ID,
					Code = normalizedCode,
					Program = claimed.Program,
					RedeemedUtc = redeemedUtc.Value,
					CodeRevoked = false,
				};
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasAccessAsync(
			string accountName,
			IReadOnlyCollection<string> programs,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string[] wanted = programs == null
				? Array.Empty<string>()
				: programs
					.Select(BetaCodeFormat.NormalizeProgram)
					.Where(BetaCodeFormat.IsValidProgram)
					.Distinct(StringComparer.Ordinal)
					.ToArray();

			if (wanted.Length == 0)
			{
				return DatabaseResult<bool>.Success(false);
			}

			string account = Authentication.NormalizeAccountLookup(accountName);

			return await ExecuteReadAsync(async dbContext =>
			{
				/* Joined to the code for two reasons: revocation lives on the code row, and the
				 * code's program is the authority even though the link carries a copy. There is
				 * deliberately no expiry test — expiry closes redemption, not the door behind the
				 * people already admitted. */
				return await (
					from link in dbContext.AccountBetaCodes.AsNoTracking()
					join betaCode in dbContext.BetaCodes.AsNoTracking() on link.BetaCodeID equals betaCode.ID
					where link.AccountName == account
						&& betaCode.RevokedUtc == null
						&& wanted.Contains(betaCode.Program)
					select link.ID)
					.AnyAsync(cancellationToken)
					.ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<AccountBetaCodeData>>> FetchForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<IReadOnlyList<AccountBetaCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string account = Authentication.NormalizeAccountLookup(accountName);

			return await ExecuteReadAsync<IReadOnlyList<AccountBetaCodeData>>(async dbContext =>
			{
				return await (
					from link in dbContext.AccountBetaCodes.AsNoTracking()
					join betaCode in dbContext.BetaCodes.AsNoTracking() on link.BetaCodeID equals betaCode.ID
					where link.AccountName == account
					orderby link.RedeemedUtc, link.ID
					select new AccountBetaCodeData
					{
						AccountName = link.AccountName,
						BetaCodeID = link.BetaCodeID,
						Code = link.Code,
						Program = link.Program,
						RedeemedUtc = link.RedeemedUtc,
						CodeRevoked = betaCode.RevokedUtc != null,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static string? CleanStaffName(string? name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return null;
			}
			string trimmed = name.Trim();
			return trimmed.Length > StaffNameMaxLength ? null : trimmed;
		}

		private static BetaCodeEntity ReadCode(DbDataReader reader) => new BetaCodeEntity
		{
			ID = reader.GetInt64(0),
			Version = (uint)reader.GetInt64(1),
			Code = reader.GetString(2),
			Program = reader.GetString(3),
			MaxUses = reader.GetInt32(4),
			UseCount = reader.GetInt32(5),
			ExpiresUtc = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6),
			RevokedUtc = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
			RevokedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
			CreatedBy = reader.GetString(9),
			CreatedUtc = reader.GetDateTime(10),
			Note = reader.IsDBNull(11) ? null : reader.GetString(11),
		};

		private static BetaCodeData Map(BetaCodeEntity e, DateTime now) => new BetaCodeData
		{
			ID = e.ID,
			Version = e.Version,
			Code = e.Code,
			Program = e.Program,
			MaxUses = e.MaxUses,
			UseCount = e.UseCount,
			ExpiresUtc = e.ExpiresUtc,
			RevokedUtc = e.RevokedUtc,
			RevokedBy = e.RevokedBy,
			CreatedBy = e.CreatedBy,
			CreatedUtc = e.CreatedUtc,
			Note = e.Note,
			IsRevoked = e.RevokedUtc.HasValue,
			IsExpired = e.ExpiresUtc.HasValue && e.ExpiresUtc.Value <= now,
			RemainingUses = Math.Max(0, e.MaxUses - e.UseCount),
		};
	
		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> CheckRedeemableAsync(string code, IReadOnlyCollection<string> programs, CancellationToken cancellationToken = default)
		{
			string normalized = BetaCodeFormat.Normalize(code);
			if (normalized == null)
			{
				return DatabaseResult<bool>.Success(false);
			}

			List<string> wanted = (programs ?? Array.Empty<string>())
				.Select(BetaCodeFormat.NormalizeProgram)
				.Where(BetaCodeFormat.IsValidProgram)
				.Distinct()
				.ToList();
			DateTime now = DateTime.UtcNow;

			return await ExecuteReadAsync(async dbContext => await dbContext.BetaCodes
				.AsNoTracking()
				.AnyAsync(c => c.Code == normalized &&
							   c.RevokedUtc == null &&
							   (c.ExpiresUtc == null || c.ExpiresUtc > now) &&
							   c.UseCount < c.MaxUses &&
							   (wanted.Count == 0 || wanted.Contains(c.Program)),
					cancellationToken)
				.ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
}
}
