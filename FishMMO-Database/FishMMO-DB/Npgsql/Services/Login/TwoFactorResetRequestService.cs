using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Delayed two-factor reset requests. Raw SQL for the state transitions, each a single
	/// conditional UPDATE; EF for reads.
	/// </summary>
	/// <remarks>
	/// This service never reads or writes the account's two-factor columns. It records whether a
	/// reset may happen; the caller does the reset, in the same unit of work as
	/// <see cref="CompleteAsync"/>.
	/// </remarks>
	public sealed class TwoFactorResetRequestService : BaseService<TwoFactorResetRequestEntity>, ITwoFactorResetRequestService
	{
		private const int DefaultPageSize = 50;
		private const int MaxPageSize = 200;
		private const int StaffNameMaxLength = 50;
		private const int ReasonMaxLength = 256;
		private const int IpMaxLength = 64;

		/// <summary>
		/// How many times <see cref="RequestAsync"/> retries when its insert conflicts with a
		/// pending row that is resolved before it can be read back.
		/// </summary>
		private const int MaxRequestAttempts = 3;

		private const string Columns =
			"id, (xmin::text)::bigint, account_name, status, requested_utc, effective_utc, requested_ip, resolved_utc, resolved_by, shortened_utc, shortened_by, staff_reason";

		/// <summary>Creates the service.</summary>
		public TwoFactorResetRequestService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<TwoFactorResetRequestData>> RequestAsync(
			string accountName,
			TimeSpan delay,
			string? requestedIp,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<TwoFactorResetRequestData>.Failure(
					DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			DateTime now = DateTime.UtcNow;
			if (delay <= TimeSpan.Zero || delay > DateTime.MaxValue - now)
			{
				return DatabaseResult<TwoFactorResetRequestData>.Failure(
					DatabaseErrorCodes.ValidationError, "The waiting period must be longer than zero.");
			}

			string account = Authentication.NormalizeAccountLookup(accountName);
			string? ip = string.IsNullOrWhiteSpace(requestedIp) ? null : Clamp(requestedIp.Trim(), IpMaxLength);
			DateTime effectiveUtc = now + delay;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Insert-or-nothing first, not read-then-insert: two simultaneous requests would
				 * both read "none" and one would then fail on the index. The conflict target must
				 * repeat the partial index's filter, literally status = 0, for PostgreSQL to infer
				 * that index as the arbiter. */
				var sql = $@"INSERT INTO {TableName} (account_name, status, requested_utc, effective_utc, requested_ip)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					ON CONFLICT (account_name) WHERE status = 0 DO NOTHING
					RETURNING {Columns}";

				var parameters = new object[]
				{
					account,
					(int)TwoFactorResetStatus.Pending,
					now,
					effectiveUtc,
					ip != null ? (object)ip : DBNull.Value,
				};

				for (int attempt = 0; attempt < MaxRequestAttempts; attempt++)
				{
					TwoFactorResetRequestEntity? created = await ExecuteReturningOrDefaultAsync(
						dbContext, sql, parameters, ReadRequest, cancellationToken).ConfigureAwait(false);

					if (created != null)
					{
						return Map(created, DateTime.UtcNow);
					}

					/* A pending request already exists. Return it as it stands: its clock is the
					 * one that runs, whatever this caller asked for. */
					var pending = await dbContext.TwoFactorResetRequests
						.AsNoTracking()
						.FirstOrDefaultAsync(r => r.AccountName == account && r.Status == TwoFactorResetStatus.Pending, cancellationToken)
						.ConfigureAwait(false);

					if (pending != null)
					{
						return Map(pending, DateTime.UtcNow);
					}

					// Cancelled or completed between the conflict and the read; the slot is free again.
				}

				throw new DatabaseException(
					"The reset request could not be recorded. Try again.",
					errorCode: DatabaseErrorCodes.DatabaseError,
					isTransient: true);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<TwoFactorResetRequestData?>> FetchPendingAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<TwoFactorResetRequestData?>.Failure(
					DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string account = Authentication.NormalizeAccountLookup(accountName);

			return await ExecuteReadAsync<TwoFactorResetRequestData?>(async dbContext =>
			{
				var entity = await dbContext.TwoFactorResetRequests
					.AsNoTracking()
					.FirstOrDefaultAsync(r => r.AccountName == account && r.Status == TwoFactorResetStatus.Pending, cancellationToken)
					.ConfigureAwait(false);

				return entity == null ? null : Map(entity, DateTime.UtcNow);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<TwoFactorResetRequestData>> FetchAsync(
			long id,
			CancellationToken cancellationToken = default)
		{
			if (id <= 0)
			{
				return DatabaseResult<TwoFactorResetRequestData>.Failure(
					DatabaseErrorCodes.ValidationError, "Request ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await dbContext.TwoFactorResetRequests
					.AsNoTracking()
					.FirstOrDefaultAsync(r => r.ID == id, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("TwoFactorResetRequest", id.ToString());
				}

				return Map(entity, DateTime.UtcNow);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> CancelAsync(
			string accountName,
			string cancelledBy,
			string? staffReason,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string? by = CleanStaffName(cancelledBy);
			if (by == null)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Who cancelled the request is required.");
			}

			string? reason = string.IsNullOrWhiteSpace(staffReason) ? null : staffReason.Trim();
			if (reason != null && reason.Length > ReasonMaxLength)
			{
				return DatabaseResult<bool>.Failure(
					DatabaseErrorCodes.ValidationError, $"The reason must be {ReasonMaxLength} characters or fewer.");
			}

			string account = Authentication.NormalizeAccountLookup(accountName);

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Two statements rather than COALESCE on a null parameter: a cancellation by a
				 * normal sign-in carries no reason and must not erase the one staff gave when they
				 * shortened the request, and an untyped null bound into an expression is not a
				 * thing to lean on. */
				string sql = reason == null
					? $@"UPDATE {TableName}
						SET status = {{0}}, resolved_utc = {{1}}, resolved_by = {{2}}
						WHERE account_name = {{3}} AND status = {{4}}"
					: $@"UPDATE {TableName}
						SET status = {{0}}, resolved_utc = {{1}}, resolved_by = {{2}}, staff_reason = {{5}}
						WHERE account_name = {{3}} AND status = {{4}}";

				object[] parameters = reason == null
					? new object[] { (int)TwoFactorResetStatus.Cancelled, DateTime.UtcNow, by, account, (int)TwoFactorResetStatus.Pending }
					: new object[] { (int)TwoFactorResetStatus.Cancelled, DateTime.UtcNow, by, account, (int)TwoFactorResetStatus.Pending, reason };

				int affected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, parameters, cancellationToken)
					.ConfigureAwait(false);

				return affected > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ShortenAsync(
			long id,
			DateTime newEffectiveUtc,
			string shortenedBy,
			string staffReason,
			CancellationToken cancellationToken = default)
		{
			if (id <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Request ID must be greater than 0.");
			}

			string? staff = CleanStaffName(shortenedBy);
			if (staff == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A staff account is required.");
			}

			string? reason = string.IsNullOrWhiteSpace(staffReason) ? null : staffReason.Trim();
			if (reason == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Give a reason for shortening the wait.");
			}
			if (reason.Length > ReasonMaxLength)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError, $"The reason must be {ReasonMaxLength} characters or fewer.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				DateTime now = DateTime.UtcNow;
				DateTime target = newEffectiveUtc < now ? now : newEffectiveUtc;

				/* "Earlier than the current effective time" is in the WHERE clause, so a request a
				 * player cancelled, or that another staff member already brought further forward,
				 * a moment ago cannot be moved by a panel showing a stale value. The clamped
				 * target is compared, and it is never earlier than the requested time, so passing
				 * this test also means the requested time was earlier. */
				var sql = $@"UPDATE {TableName}
					SET effective_utc = {{0}}, shortened_utc = {{1}}, shortened_by = {{2}}, staff_reason = {{3}}
					WHERE id = {{4}} AND status = {{5}} AND {{0}} < effective_utc";

				int affected = await dbContext.Database
					.ExecuteSqlRawAsync(
						sql,
						new object[] { target, now, staff, reason, id, (int)TwoFactorResetStatus.Pending },
						cancellationToken)
					.ConfigureAwait(false);

				if (affected == 1)
				{
					return;
				}

				// Only to choose the right failure; nothing was changed.
				var entity = await dbContext.TwoFactorResetRequests
					.AsNoTracking()
					.FirstOrDefaultAsync(r => r.ID == id, cancellationToken)
					.ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("TwoFactorResetRequest", id.ToString());
				}
				if (entity.Status != TwoFactorResetStatus.Pending)
				{
					throw new DatabaseException("That reset request is no longer pending.", errorCode: DatabaseErrorCodes.ValidationError);
				}
				if (entity.EffectiveUtc <= now)
				{
					throw new DatabaseException("That reset request has already taken effect.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				throw new DatabaseException(
					"A reset can only be brought forward. To make the player wait longer, cancel it and have them request again.",
					errorCode: DatabaseErrorCodes.ValidationError);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> CompleteAsync(
			long id,
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (id <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Request ID must be greater than 0.");
			}
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string account = Authentication.NormalizeAccountLookup(accountName);

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Every precondition in the one statement that marks it: owned by this account,
				 * still pending, and past its effective time. A normal sign-in that cancelled it a
				 * moment earlier leaves nothing for this to match. */
				var sql = $@"UPDATE {TableName}
					SET status = {{0}}, resolved_utc = {{1}}, resolved_by = {{2}}
					WHERE id = {{3}} AND account_name = {{2}} AND status = {{4}} AND effective_utc <= {{1}}";

				int affected = await dbContext.Database
					.ExecuteSqlRawAsync(
						sql,
						new object[] { (int)TwoFactorResetStatus.Completed, DateTime.UtcNow, account, id, (int)TwoFactorResetStatus.Pending },
						cancellationToken)
					.ConfigureAwait(false);

				return affected == 1;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<TwoFactorResetRequestPage>> SearchPendingAsync(
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			int resolvedPage = page < 1 ? 1 : page;
			int resolvedPageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<TwoFactorResetRequestEntity> q = dbContext.TwoFactorResetRequests
					.AsNoTracking()
					.Where(r => r.Status == TwoFactorResetStatus.Pending);

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					.OrderBy(r => r.EffectiveUtc)
					.ThenBy(r => r.ID)
					.Skip((resolvedPage - 1) * resolvedPageSize)
					.Take(resolvedPageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				DateTime now = DateTime.UtcNow;
				return new TwoFactorResetRequestPage
				{
					Items = rows.Select(r => Map(r, now)).ToList(),
					Page = resolvedPage,
					PageSize = resolvedPageSize,
					TotalCount = total,
				};
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

		private static string Clamp(string value, int max) =>
			value.Length <= max ? value : value.Substring(0, max);

		/// <summary>
		/// Maps a stored integer to <see cref="TwoFactorResetStatus"/>. An unrecognised value reads
		/// as <see cref="TwoFactorResetStatus.Cancelled"/>: a state this build does not know must
		/// never be treated as a request that can still be completed.
		/// </summary>
		private static TwoFactorResetStatus ToStatus(int value) =>
			Enum.IsDefined(typeof(TwoFactorResetStatus), value) ? (TwoFactorResetStatus)value : TwoFactorResetStatus.Cancelled;

		private static TwoFactorResetRequestEntity ReadRequest(DbDataReader reader) => new TwoFactorResetRequestEntity
		{
			ID = reader.GetInt64(0),
			Version = (uint)reader.GetInt64(1),
			AccountName = reader.GetString(2),
			Status = ToStatus(reader.GetInt32(3)),
			RequestedUtc = reader.GetDateTime(4),
			EffectiveUtc = reader.GetDateTime(5),
			RequestedIp = reader.IsDBNull(6) ? null : reader.GetString(6),
			ResolvedUtc = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
			ResolvedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
			ShortenedUtc = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
			ShortenedBy = reader.IsDBNull(10) ? null : reader.GetString(10),
			StaffReason = reader.IsDBNull(11) ? null : reader.GetString(11),
		};

		private static TwoFactorResetRequestData Map(TwoFactorResetRequestEntity e, DateTime now) => new TwoFactorResetRequestData
		{
			ID = e.ID,
			Version = e.Version,
			AccountName = e.AccountName,
			Status = e.Status,
			RequestedUtc = e.RequestedUtc,
			EffectiveUtc = e.EffectiveUtc,
			RequestedIp = e.RequestedIp,
			ResolvedUtc = e.ResolvedUtc,
			ResolvedBy = e.ResolvedBy,
			ShortenedUtc = e.ShortenedUtc,
			ShortenedBy = e.ShortenedBy,
			StaffReason = e.StaffReason,
			IsEffective = e.Status == TwoFactorResetStatus.Pending && e.EffectiveUtc <= now,
		};
	}
}
