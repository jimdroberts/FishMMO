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
	/// Control Panel browser sessions, stored by hash.
	/// </summary>
	/// <remarks>
	/// Every statement goes through EF rather than raw SQL, deliberately: the model carries the
	/// configured schema via <c>HasDefaultSchema</c>, but a raw <c>UPDATE web_sessions ...</c> is
	/// resolved against the connection's <c>search_path</c> instead. On any deployment whose
	/// <c>Npgsql:Schema</c> is not the search path, those statements silently matched nothing and
	/// the failure was swallowed by callers that ignored the result — a session could be issued
	/// and then never promoted, revoked or expired.
	/// </remarks>
	public sealed class WebSessionService : BaseService<WebSessionEntity>, IWebSessionService
	{
		/// <summary>Longest a session hash can be: SHA-256 as lowercase hex.</summary>
		private const int SessionHashLength = 64;

		public WebSessionService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		private static bool IsValidHash(string sessionHash)
		{
			return !string.IsNullOrWhiteSpace(sessionHash) && sessionHash.Length == SessionHashLength;
		}

		private static WebSessionData ToData(WebSessionEntity e) => new WebSessionData(
			e.ID, e.AccountName, e.AccessLevelAtIssue, e.TwoFactorSatisfied, e.LastStepUpUtc,
			e.CreatedUtc, e.LastSeenUtc, e.ExpiresUtc, e.Revoked, e.IpAddress, e.UserAgent);

		/// <inheritdoc/>
		public async Task<DatabaseResult<WebSessionData>> IssueAsync(
			string sessionHash,
			string accountName,
			byte accessLevel,
			bool twoFactorSatisfied,
			DateTime expiresUtc,
			string ipAddress,
			string userAgent,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidHash(sessionHash))
			{
				return DatabaseResult<WebSessionData>.Failure(
					DatabaseErrorCodes.ValidationError, "Session hash must be a 64-character SHA-256 hex string.");
			}
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<WebSessionData>.Failure(
					DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async context =>
			{
				/* The session hash is a digest of a random id, so a row already holding it for this
				 * account can only be this call's own, landed by an attempt whose reply was lost. The
				 * retry used to fail on the unique index and the sign-in with it, for a session that
				 * had been created (issue #267). */
				string canonicalName = AuthTokenService.CanonicalAccountName(accountName);
				var landed = await context.WebSessions
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.SessionHash == sessionHash && s.AccountName == canonicalName, cancellationToken)
					.ConfigureAwait(false);
				if (landed != null)
				{
					return ToData(landed);
				}

				var entity = new WebSessionEntity
				{
					SessionHash = sessionHash,
					// The row's own spelling: web_sessions.account_name references accounts.name. See
					// AuthTokenService.CanonicalAccountName.
					AccountName = canonicalName,
					AccessLevelAtIssue = accessLevel,
					TwoFactorSatisfied = twoFactorSatisfied,
					// A session that has just proven a second factor has, by definition, just
					// stepped up; one that has not must not inherit a step-up window.
					LastStepUpUtc = twoFactorSatisfied ? now : (DateTime?)null,
					CreatedUtc = now,
					LastSeenUtc = now,
					ExpiresUtc = expiresUtc,
					Revoked = false,
					// Truncate rather than reject: a hostile user agent must not be able to fail
					// a login by being too long.
					IpAddress = Truncate(ipAddress, 64),
					UserAgent = Truncate(userAgent, 256),
				};
				await context.WebSessions.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return ToData(entity);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static string Truncate(string value, int max)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return value.Length <= max ? value : value.Substring(0, max);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WebSessionData>> FetchByHashAsync(
			string sessionHash,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidHash(sessionHash))
			{
				return DatabaseResult<WebSessionData>.Failure(
					DatabaseErrorCodes.ValidationError, "Session hash must be a 64-character SHA-256 hex string.");
			}

			return await ExecuteReadAsync(async context =>
			{
				var entity = await context.WebSessions
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.SessionHash == sessionHash, cancellationToken)
					.ConfigureAwait(false);
				if (entity == null)
				{
					// The hash is a lookup key, not a secret an error message could leak.
					throw new Exceptions.DatabaseEntityNotFoundException("WebSession", sessionHash);
				}
				return ToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> TouchAsync(
			string sessionHash,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidHash(sessionHash))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid session hash.");
			}

			/* One conditional UPDATE, like every write in this service. See RevokeAllForAccountAsync
			 * for why none of them may read the row through the change tracker first. */
			return await ExecuteWriteAsync(async context =>
			{
				await context.Database.ExecuteSqlRawAsync(
					$"UPDATE {TableName} SET last_seen_utc = {{0}} WHERE session_hash = {{1}} AND revoked = FALSE",
					new object[] { DateTime.UtcNow, sessionHash },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistTwoFactorSatisfiedAsync(
			string sessionHash,
			DateTime expiresUtc,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidHash(sessionHash))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid session hash.");
			}

			DateTime now = DateTime.UtcNow;
			return await ExecuteWriteAsync(async context =>
			{
				int rows = await context.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName}
						SET two_factor_satisfied = TRUE, last_step_up_utc = {{0}}, last_seen_utc = {{0}}, expires_utc = {{1}}
						WHERE session_hash = {{2}} AND revoked = FALSE",
					new object[] { now, expiresUtc, sessionHash },
					cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new Exceptions.DatabaseEntityNotFoundException("WebSession", sessionHash);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistStepUpAsync(
			string sessionHash,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidHash(sessionHash))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid session hash.");
			}

			return await ExecuteWriteAsync(async context =>
			{
				int rows = await context.Database.ExecuteSqlRawAsync(
					$"UPDATE {TableName} SET last_step_up_utc = {{0}}, last_seen_utc = {{0}} WHERE session_hash = {{1}} AND revoked = FALSE",
					new object[] { DateTime.UtcNow, sessionHash },
					cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new Exceptions.DatabaseEntityNotFoundException("WebSession", sessionHash);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RevokeByHashAsync(
			string sessionHash,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidHash(sessionHash))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid session hash.");
			}

			return await ExecuteWriteAsync(async context =>
			{
				// Idempotent: a session that is already revoked, or gone, is a success.
				await context.Database.ExecuteSqlRawAsync(
					$"UPDATE {TableName} SET revoked = TRUE WHERE session_hash = {{0}} AND revoked = FALSE",
					new object[] { sessionHash },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RevokeByIdAsync(
			string accountName,
			long id,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async context =>
			{
				// Scoped to the account on purpose: an id alone must not let one operator revoke
				// another operator's session.
				await context.Database.ExecuteSqlRawAsync(
					$"UPDATE {TableName} SET revoked = TRUE WHERE id = {{0}} AND account_name = {{1}} AND revoked = FALSE",
					new object[] { id, AuthTokenService.CanonicalAccountName(accountName) },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> RevokeAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			/* One UPDATE, not load-then-save. The entity's Version is Postgres's xmin, a concurrency
			 * token, and TouchAsync rewrites a session's row on every panel request — so a revoke
			 * that read the rows and then saved them lost the race to any request made in between:
			 * EF threw DbUpdateConcurrencyException, that mapped to STALE_STATE, and NOTHING was
			 * revoked. Here that was one touched session aborting the revocation of all of them, on
			 * the path a ban, a password change and a compromise response all take (issue #267).
			 * A single statement is applied by Postgres to the current row version, so there is no
			 * window to lose. Every other write in this service is one statement for the same
			 * reason. */
			return await ExecuteWriteAsync(async context =>
			{
				return await context.Database.ExecuteSqlRawAsync(
					$"UPDATE {TableName} SET revoked = TRUE WHERE account_name = {{0}} AND revoked = FALSE",
					// Any case in, the row's spelling out: an exact match on the caller's spelling
					// revoked nothing when it differed, and reported success.
					new object[] { AuthTokenService.CanonicalAccountName(accountName) },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<WebSessionData>>> FetchActiveForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<List<WebSessionData>>.Failure(
					DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			DateTime now = DateTime.UtcNow;
			string canonicalName = AuthTokenService.CanonicalAccountName(accountName);
			return await ExecuteReadAsync(async context =>
			{
				var rows = await context.WebSessions
					.AsNoTracking()
					.Where(s => s.AccountName == canonicalName && !s.Revoked && s.ExpiresUtc > now)
					.OrderByDescending(s => s.LastSeenUtc)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				return rows.Select(ToData).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CleanupExpiredAsync(
			DateTime cutoffUtc,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async context =>
			{
				var rows = await context.WebSessions
					.Where(s => s.ExpiresUtc < cutoffUtc)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				context.WebSessions.RemoveRange(rows);
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return rows.Count;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
