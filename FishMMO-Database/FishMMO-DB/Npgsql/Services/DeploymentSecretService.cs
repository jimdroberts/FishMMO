using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing deployment-global secrets loaded by servers at startup.
	/// Uses a compiled query for reads and a raw SQL upsert for writes.
	/// </summary>
	public sealed class DeploymentSecretService : BaseService<DeploymentSecretEntity>, IDeploymentSecretService
	{
#pragma warning disable CS8619
		/// <summary>
		/// Compiled query for fetching a secret by its key without tracking.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<DeploymentSecretEntity?>> getByKeyQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string key, CancellationToken ct) =>
				context.DeploymentSecrets
					.AsNoTracking()
					.FirstOrDefault(s => s.Key == key));
#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of DeploymentSecretService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public DeploymentSecretService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<DeploymentSecretInfo>>> FetchInventoryAsync(
			CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				/* The projection names four columns and Value is not one of them, so the secret
				 * material never leaves the database — not into this process's memory, let alone
				 * into a response. Selecting the entity and mapping afterwards would read every
				 * key into the heap for no reason. */
				var rows = await dbContext.Set<DeploymentSecretEntity>()
					.AsNoTracking()
					.OrderBy(e => e.Key)
					.Select(e => new DeploymentSecretInfo
					{
						Key = e.Key,
						ValueLength = e.Value.Length,
						CreatedUtc = e.TimeCreated,
						UpdatedUtc = e.TimeUpdated,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return (IReadOnlyList<DeploymentSecretInfo>)rows;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		public async Task<DatabaseResult<string>> FetchAsync(
			string key,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				return DatabaseResult<string>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Secret key must not be null or empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await getByKeyQuery(dbContext, key, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("DeploymentSecret", key);
				}

				return entity.Value;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpsertAsync(
			string key,
			string value,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Secret key must not be null or empty.");
			}

			if (string.IsNullOrWhiteSpace(value))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Secret value must not be null or empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Use INSERT ... ON CONFLICT to atomically insert or update
				var sql = $@"INSERT INTO {TableName} (key, value, time_created, time_updated)
					VALUES ({{0}}, {{1}}, timezone('UTC', CURRENT_TIMESTAMP), timezone('UTC', CURRENT_TIMESTAMP))
					ON CONFLICT (key) DO UPDATE SET
						value = EXCLUDED.value,
						time_updated = timezone('UTC', CURRENT_TIMESTAMP)";

				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { key, value }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
