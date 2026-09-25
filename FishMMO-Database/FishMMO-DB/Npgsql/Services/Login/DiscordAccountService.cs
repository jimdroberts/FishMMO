using System;
using System.Collections.Generic;
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
	/// The Discord bot's side of an account. Parameterized raw SQL for writes, EF for reads.
	/// </summary>
	/// <remarks>
	/// Every write names the state it expects in its WHERE, so two bot instances, or a bot racing a
	/// player who verified by email a moment ago, cannot both act: the second matches nothing. See
	/// <see cref="DiscordVerification"/> for the once-only delivery rules.
	/// </remarks>
	public sealed class DiscordAccountService : BaseService<AccountEntity>, IDiscordAccountService
	{
		/// <summary>The most deliveries one read returns, whatever is asked for.</summary>
		private const int MaxBatch = 100;

		/// <summary>Creates the service.</summary>
		public DiscordAccountService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <summary>
		/// Accounts owed a DM nobody has taken: unverified, chose Discord, have a username and an issued
		/// code, not sent, not claimed, attempts left. The claim below re-checks all of it.
		/// </summary>
		private static IQueryable<AccountEntity> Pending(NpgsqlDbContext dbContext) =>
			dbContext.Accounts
				.AsNoTracking()
				.Where(a => !a.Verified &&
					(a.VerificationChannels & (byte)AccountVerificationChannels.Discord) != 0 &&
					a.DiscordUsername != null &&
					a.DiscordVerifyCode != 0 &&
					a.DiscordDmSentAt == null &&
					a.DiscordDmClaimedAt == null &&
					a.DiscordDmAttempts < DiscordVerification.MaxSendAttempts);

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<DiscordVerificationDelivery>>> FetchPendingDeliveriesAsync(
			int limit,
			CancellationToken cancellationToken = default)
		{
			int take = Math.Clamp(limit, 1, MaxBatch);
			return await ExecuteReadAsync(async dbContext =>
			{
				/* Never-tried deliveries first. A player still waiting to join the Discord server keeps an owed
				 * row with a recorded reason, and is served by the join event; ordering purely by age let a batch
				 * full of those starve a newer registrant who is already in the server. */
				var rows = await Pending(dbContext)
					.OrderBy(a => a.DiscordDmLastError != null)
					.ThenBy(a => a.TimeCreated)
					.Take(take)
					.Select(a => new { a.Name, a.DiscordUsername, a.DiscordVerifyCode, a.DiscordDmAttempts, a.DiscordDmLastError })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				return (IReadOnlyList<DiscordVerificationDelivery>)rows
					.Select(r => new DiscordVerificationDelivery(r.Name, r.DiscordUsername!, r.DiscordVerifyCode, r.DiscordDmAttempts, r.DiscordDmLastError))
					.ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<DiscordVerificationDelivery>>> FetchPendingDeliveriesForUsernameAsync(
			string discordUsername,
			CancellationToken cancellationToken = default)
		{
			string? normalized = AccountProfileRules.NormalizeDiscordUsername(discordUsername);
			if (normalized == null)
			{
				return DatabaseResult<IReadOnlyList<DiscordVerificationDelivery>>.Success(Array.Empty<DiscordVerificationDelivery>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var rows = await Pending(dbContext)
					.Where(a => a.DiscordUsername == normalized)
					.OrderBy(a => a.TimeCreated)
					.Take(MaxBatch)
					.Select(a => new { a.Name, a.DiscordUsername, a.DiscordVerifyCode, a.DiscordDmAttempts, a.DiscordDmLastError })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				return (IReadOnlyList<DiscordVerificationDelivery>)rows
					.Select(r => new DiscordVerificationDelivery(r.Name, r.DiscordUsername!, r.DiscordVerifyCode, r.DiscordDmAttempts, r.DiscordDmLastError))
					.ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ClaimDeliveryAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			/* Taken once, outside the retried delegate, and written as the claim. A connection lost
			 * after the claim committed but before its reply arrived is retried, and the retry used
			 * to find the claim already taken and answer false: the bot then neither sent the DM nor
			 * released a claim it did not know it held, and the code was never delivered. The retry
			 * now re-takes its own claim, which carries this exact stamp (issue #267). */
			DateTime claimStampUtc = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET discord_dm_claimed_at = {{3}}
					WHERE name_lowercase = {{0}}
						AND verified = false
						AND (verification_channels & {{2}}) <> 0
						AND discord_username IS NOT NULL
						AND discord_verify_code <> 0
						AND discord_dm_sent_at IS NULL
						AND (discord_dm_claimed_at IS NULL OR discord_dm_claimed_at = {{3}})
						AND discord_dm_attempts < {{1}}";
				int rows = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
				{
					Authentication.NormalizeAccountLookup(accountName),
					DiscordVerification.MaxSendAttempts,
					(int)AccountVerificationChannels.Discord,
					claimStampUtc,
				}, cancellationToken).ConfigureAwait(false);
				return rows == 1;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MarkDeliveredAsync(string accountName, long discordUserId, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET discord_dm_sent_at = timezone('UTC', CURRENT_TIMESTAMP),
						discord_dm_user_id = {{1}},
						discord_dm_last_error = NULL
					WHERE name_lowercase = {{0}}
						AND discord_dm_claimed_at IS NOT NULL
						AND discord_dm_sent_at IS NULL";
				int rows = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
				{
					Authentication.NormalizeAccountLookup(accountName),
					discordUserId,
				}, cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Discord delivery", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ReleaseDeliveryAsync(string accountName, string reason, bool countAttempt, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			string recorded = string.IsNullOrWhiteSpace(reason) ? "Not delivered." : reason.Trim();
			if (recorded.Length > DiscordVerification.MaxErrorLength)
			{
				recorded = recorded.Substring(0, DiscordVerification.MaxErrorLength);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET discord_dm_claimed_at = NULL,
						discord_dm_last_error = {{1}},
						discord_dm_attempts = discord_dm_attempts + {{2}}
					WHERE name_lowercase = {{0}}
						AND discord_dm_sent_at IS NULL";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
				{
					Authentication.NormalizeAccountLookup(accountName),
					recorded,
					countAttempt ? 1 : 0,
				}, cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DiscordAccountLink?>> FetchLinkByDiscordUserAsync(long discordUserId, CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				var row = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.DiscordUserId == discordUserId)
					.Select(a => new { a.Name, a.DiscordUsername, a.DiscordLinkedAt })
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);
				return row == null
					? (DiscordAccountLink?)null
					: new DiscordAccountLink(row.Name, discordUserId, row.DiscordUsername, row.DiscordLinkedAt);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> LinkAsync(string accountName, long discordUserId, string? discordUsername, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidUsernameError);
			}

			// A name Discord reports that the rules do not accept is simply not recorded; the link is the id.
			string? username = AccountProfileRules.NormalizeDiscordUsername(discordUsername);

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* discord_verified is set because a link proves the Discord account as well as a DM code does:
				 * the bot's /link flow has the player type a code the bot DMed them into game chat. The unique
				 * index on discord_user_id refuses a Discord user already linked to another account. A null
				 * username is passed as null, never DBNull.Value; see PersistProfileAsync. */
				var sql = $@"UPDATE {TableName}
					SET discord_user_id = {{1}},
						discord_username = COALESCE({{2}}, discord_username),
						discord_verified = true,
						discord_linked_at = timezone('UTC', CURRENT_TIMESTAMP)
					WHERE name_lowercase = {{0}}";
				int rows = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[]
				{
					Authentication.NormalizeAccountLookup(accountName),
					discordUserId,
					(object?)username,
				}, cancellationToken).ConfigureAwait(false);
				if (rows == 0)
				{
					throw new DatabaseEntityNotFoundException("Account", accountName);
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> UnlinkAsync(long discordUserId, CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				/* The account's verified flag is untouched: it was verified once, and an unlink is not a
				 * reason to lock a player out. Only the Discord channel's own proof goes. */
				var sql = $@"UPDATE {TableName}
					SET discord_user_id = NULL,
						discord_linked_at = NULL,
						discord_verified = false
					WHERE discord_user_id = {{0}}";
				int rows = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { discordUserId }, cancellationToken)
					.ConfigureAwait(false);
				return rows > 0;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
