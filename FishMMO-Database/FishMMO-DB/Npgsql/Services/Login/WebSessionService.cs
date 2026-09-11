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
				var entity = new WebSessionEntity
				{
					SessionHash = sessionHash,
					AccountName = accountName,
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

			return await ExecuteWriteAsync(async context =>
			{
				var entity = await context.WebSessions
					.FirstOrDefaultAsync(s => s.SessionHash == sessionHash && !s.Revoked, cancellationToken)
					.ConfigureAwait(false);
				if (entity != null)
				{
					entity.LastSeenUtc = DateTime.UtcNow;
					await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				}
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
				var entity = await context.WebSessions
					.FirstOrDefaultAsync(s => s.SessionHash == sessionHash && !s.Revoked, cancellationToken)
					.ConfigureAwait(false);
				if (entity == null)
				{
					throw new Exceptions.DatabaseEntityNotFoundException("WebSession", sessionHash);
				}
				entity.TwoFactorSatisfied = true;
				entity.LastStepUpUtc = now;
				entity.LastSeenUtc = now;
				entity.ExpiresUtc = expiresUtc;
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
				var entity = await context.WebSessions
					.FirstOrDefaultAsync(s => s.SessionHash == sessionHash && !s.Revoked, cancellationToken)
					.ConfigureAwait(false);
				if (entity == null)
				{
					throw new Exceptions.DatabaseEntityNotFoundException("WebSession", sessionHash);
				}
				DateTime now = DateTime.UtcNow;
				entity.LastStepUpUtc = now;
				entity.LastSeenUtc = now;
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
				var entity = await context.WebSessions
					.FirstOrDefaultAsync(s => s.SessionHash == sessionHash, cancellationToken)
					.ConfigureAwait(false);
				if (entity != null)
				{
					entity.Revoked = true;
					await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				}
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
				var entity = await context.WebSessions
					.FirstOrDefaultAsync(s => s.ID == id && s.AccountName == accountName, cancellationToken)
					.ConfigureAwait(false);
				if (entity != null)
				{
					entity.Revoked = true;
					await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				}
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

			return await ExecuteWriteAsync(async context =>
			{
				var rows = await context.WebSessions
					.Where(s => s.AccountName == accountName && !s.Revoked)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				foreach (var row in rows)
				{
					row.Revoked = true;
				}
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return rows.Count;
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
			return await ExecuteReadAsync(async context =>
			{
				var rows = await context.WebSessions
					.AsNoTracking()
					.Where(s => s.AccountName == accountName && !s.Revoked && s.ExpiresUtc > now)
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
