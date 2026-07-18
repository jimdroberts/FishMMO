using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for validating and consuming one-time connection tokens.
	/// Connection tokens bridge the real client IP from the HTTP layer
	/// (where X-Forwarded-For is visible) to the QUIC/WebTransport layer
	/// (where the game server sees 127.0.0.1 behind an L4 UDP proxy).
	///
	/// Uses atomic DELETE ... RETURNING to guarantee one-time consumption
	/// under concurrent access — two servers processing the same token hash
	/// will never both receive the real IP.
	/// </summary>
	public sealed class ConnectionTokenService : BaseService<ConnectionTokenEntity>, IConnectionTokenService
	{
		public ConnectionTokenService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<string?>> ValidateAndConsumeAsync(
			string tokenHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult<string?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			if (tokenHash.Length != 64)
			{
				return DatabaseResult<string?>.Failure(
					DatabaseErrorCodes.ValidationError,
					$"Token hash must be 64 hex characters (got {tokenHash.Length}).");
			}

			// Atomic DELETE ... RETURNING real_ip.
			// Only one caller wins under concurrent access — the other gets no rows.
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName}
					WHERE token_hash = {{0}} AND expires_at > NOW()
					RETURNING real_ip";

				var realIp = await ExecuteReturningOrDefaultAsync<string>(
					dbContext,
					sql,
					new object[] { tokenHash },
					reader => reader.GetString(0),
					cancellationToken).ConfigureAwait(false);

				return realIp;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}