using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc cref="ICharacterSessionOwnershipService"/>
	public sealed class CharacterSessionOwnershipService : BaseService<CharacterEntity>, ICharacterSessionOwnershipService
	{
		/// <summary>
		/// Outcome codes returned by the assertion statement.
		/// </summary>
		/// <remarks>
		/// The comparison is done in SQL rather than in C# on purpose: shipping the row back and
		/// deciding here would mean deciding on a copy, and the whole point of the statement is that
		/// the decision is made while the row is locked.
		/// </remarks>
		private const long OutcomeOwned = 0;
		private const long OutcomeDeleted = 1;
		private const long OutcomeUnclaimed = 2;
		private const long OutcomeDifferentOwner = 3;
		private const long OutcomeMissing = 4;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterSessionOwnershipService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		public CharacterSessionOwnershipService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> AssertOwnershipAsync(long characterId, CharacterSessionLeaseData lease, bool allowUnclaimed = false, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			if (lease.IsValid && lease.CharacterID != characterId)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Ownership triple refers to a different character than the one being asserted.");
			}

			if (!lease.IsValid && !allowUnclaimed)
			{
				// Nothing to compare against and no unclaimed fallback permitted. Saying "yes" here
				// would make the guard decorative.
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Ownership triple is missing or malformed and unclaimed writes were not permitted.");
			}

			// A row lock only means anything for as long as the transaction that took it is open.
			// Called outside a unit of work this method would take a lock, release it on the way
			// out, and hand the caller a fact about the past — which is precisely the check-then-act
			// race it exists to close. Refusing is the only honest answer.
			if (!DatabaseExecutionScope.TryGetCurrentDbContext(out var dbContext))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.InvalidOperation,
					"AssertOwnershipAsync must be called inside a unit of work; the row lock it takes is only " +
					"meaningful while that transaction is open.");
			}

			IDbContextTransaction currentTransaction = dbContext.Database.CurrentTransaction;
			if (currentTransaction == null)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.InvalidOperation,
					"AssertOwnershipAsync requires an open transaction on the ambient context.");
			}

			// An invalid lease can still be compared safely: server 0 and the empty GUID never match
			// a live claim, so the statement below falls through to Unclaimed or DifferentOwner and
			// the allowUnclaimed flag decides. No special-casing, and therefore no second code path
			// to get wrong.
			long leaseServerId = lease.IsValid ? lease.OwnerServerID : 0L;
			Guid leaseToken = lease.IsValid ? lease.OwnerToken : Guid.Empty;

			try
			{
				// The CTE is what makes FOR NO KEY UPDATE legal alongside the COALESCE: the lock is
				// taken by a plain SELECT over the one row, and the classification happens outside it.
				// COALESCE supplies the missing-row answer, because ExecuteScalar returns NULL for an
				// empty result and Convert.ToInt64(null) is 0 — which is the "owned" code, i.e. the
				// most dangerous possible default.
				//
				// FOR NO KEY UPDATE (rather than FOR UPDATE) is deliberate: it is the same lock mode
				// an ordinary UPDATE takes, so it conflicts with TryClaimAsync/ReleaseAsync exactly
				// as required, while still permitting concurrent foreign-key references to the row —
				// and every character-owned table has an FK to characters.id, so FOR UPDATE would
				// serialise unrelated inserts across the whole schema.
				string sql = $@"
					WITH locked AS (
						SELECT deleted, session_state, session_owner_server_id, session_owner_token
						FROM {TableName}
						WHERE id = {{0}}
						FOR NO KEY UPDATE
					)
					SELECT COALESCE((
						SELECT CASE
							WHEN locked.deleted THEN {OutcomeDeleted}
							WHEN locked.session_state = {{3}}
								AND locked.session_owner_server_id = {{1}}
								AND locked.session_owner_token = {{2}} THEN {OutcomeOwned}
							WHEN locked.session_state <> {{3}}
								AND locked.session_owner_server_id = 0 THEN {OutcomeUnclaimed}
							ELSE {OutcomeDifferentOwner}
						END
						FROM locked
					), {OutcomeMissing})::bigint AS value";

				long outcome = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[]
					{
						characterId,
						leaseServerId,
						leaseToken,
						(short)CharacterSessionState.Online,
					},
					cancellationToken).ConfigureAwait(false);

				switch (outcome)
				{
					case OutcomeOwned:
						return DatabaseResult.Success();

					case OutcomeUnclaimed:
						return allowUnclaimed
							? DatabaseResult.Success()
							: DatabaseResult.Failure(
								DatabaseErrorCodes.Forbidden,
								$"Character {characterId} holds no session claim; refusing a write that requires one.");

					case OutcomeMissing:
						return DatabaseResult.Failure(
							DatabaseErrorCodes.NotFound,
							$"Character {characterId} does not exist.");

					case OutcomeDeleted:
						return DatabaseResult.Failure(
							DatabaseErrorCodes.Forbidden,
							$"Character {characterId} has been deleted; refusing to write its state.");

					default:
						// A live claim held by someone else, or by us under an older token. Either way
						// another process is authoritative and this write is the stale one.
						return DatabaseResult.Failure(
							DatabaseErrorCodes.Forbidden,
							$"Character {characterId} is claimed by a different server or a newer session; refusing a write from a stale owner.");
				}
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.Canceled, "Operation was canceled.");
			}
			catch (Exception ex)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.DatabaseError,
					$"Failed to assert session ownership ({ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)}) ({ExceptionDiagnosticHelper.BuildSafeExceptionDiagnostic(ex)}).",
					isTransient: true);
			}
		}
	}
}
