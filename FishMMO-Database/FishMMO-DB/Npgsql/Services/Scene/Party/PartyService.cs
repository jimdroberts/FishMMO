using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class PartyService : IPartyService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of PartyService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public PartyService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ExistsAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
				return DatabaseResult<bool>.Success(false);

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				bool exists = await context.Parties
					.AsNoTracking()
					.AnyAsync(p => p.ID == partyId, cancellationToken);

				return DatabaseResult<bool>.Success(exists);
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<bool>.FromException(new DatabaseTimeoutException(
					"CheckPartyExists",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<bool>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"parties_pkey",
					"A party with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<bool>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"parties_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<bool>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<bool>.FromException(new DatabaseQueryException(
					"CheckPartyExists",
					"Failed to check if party exists.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<bool>.FromException(new DatabaseQueryException(
					"CheckPartyExists",
					"An unexpected error occurred while checking if party exists.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateAsync(CancellationToken cancellationToken = default)
		{
			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var partyId = await strategy.ExecuteAsync(async () =>
				{
					var party = new PartyEntity();
					context.Parties.Add(party);
					await context.SaveChangesAsync(cancellationToken);
					return party.ID;
				});

				return DatabaseResult<long>.Success(partyId);
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<long>.FromException(new DatabaseTimeoutException(
					"CreateParty",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<long>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"parties_pkey",
					"A party with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<long>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"parties_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<long>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<long>.FromException(new DatabaseQueryException(
					"CreateParty",
					"Failed to create party.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long>.FromException(new DatabaseQueryException(
					"CreateParty",
					"An unexpected error occurred while creating party.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<PartyEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE id = {partyId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(new DatabaseEntityNotFoundException(
						"Party",
						partyId.ToString(),
						"Party not found."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"DeleteParty",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"parties_pkey",
					"A party with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"parties_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"DeleteParty",
					"Failed to delete party.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"DeleteParty",
					"An unexpected error occurred while deleting party.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PartyData>> LoadAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<PartyData>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var party = await context.Parties
					.AsNoTracking()
					.FirstOrDefaultAsync(p => p.ID == partyId, cancellationToken);

				if (party == null)
				{
					return DatabaseResult<PartyData>.FromException(new DatabaseEntityNotFoundException(
						"Party",
						partyId.ToString(),
						"Party not found."));
				}

				return DatabaseResult<PartyData>.Success(MapEntityToDto(party));
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<PartyData>.FromException(new DatabaseTimeoutException(
					"LoadParty",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<PartyData>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"parties_pkey",
					"A party with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<PartyData>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"parties_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<PartyData>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<PartyData>.FromException(new DatabaseQueryException(
					"LoadParty",
					"Failed to load party.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<PartyData>.FromException(new DatabaseQueryException(
					"LoadParty",
					"An unexpected error occurred while loading party.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps PartyEntity to PartyData DTO.
		/// </summary>
		/// <param name="entity">Party entity from database.</param>
		/// <returns>Party data DTO.</returns>
		private PartyData MapEntityToDto(PartyEntity entity)
		{
			return new PartyData
			{
				ID = entity.ID
			};
		}
	}
}