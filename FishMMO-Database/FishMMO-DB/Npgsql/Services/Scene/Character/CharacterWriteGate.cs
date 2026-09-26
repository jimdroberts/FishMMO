using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// What the ownership gate decided about a write as a whole.
	/// </summary>
	public enum CharacterWriteGateDecision : byte
	{
		/// <summary>At least one named character may be written; write its rows.</summary>
		Write = 0,
		/// <summary>No named character exists (or every one is deleted). <see cref="DatabaseErrorCodes.NotFound"/>.</summary>
		NotFound = 1,
		/// <summary>Named characters exist but the writer holds none of their claims. <see cref="DatabaseErrorCodes.Forbidden"/>.</summary>
		NotOwned = 2,
	}

	/// <summary>
	/// The characters one gated write may touch, decided inside the write's own transaction.
	/// </summary>
	/// <remarks>
	/// For an owned write every character in <see cref="Writable"/> is held under a share lock until
	/// the transaction ends, so nothing that changes its claim — a release, a claim, the row save,
	/// the lease refresh — can commit in between: the decision stays true for as long as the rows it
	/// admitted are being written.
	/// </remarks>
	public sealed class CharacterWriteAdmission
	{
		private readonly HashSet<long> writable;
		private readonly HashSet<long> unowned;

		internal CharacterWriteAdmission(HashSet<long> writable, HashSet<long> unowned)
		{
			this.writable = writable ?? new HashSet<long>();
			this.unowned = unowned ?? new HashSet<long>();
		}

		/// <summary>Characters whose rows may be written.</summary>
		public IReadOnlyCollection<long> Writable => writable;

		/// <summary>Live characters whose rows are refused because the writer does not hold their claim.</summary>
		public IReadOnlyCollection<long> Unowned => unowned;

		/// <summary>Whether a row of this character may be written.</summary>
		public bool Admits(long characterId) => writable.Contains(characterId);

		/// <summary>How many of <paramref name="rows"/> belong to a character refused for want of its claim.</summary>
		public int CountUnowned<T>(IEnumerable<T> rows, Func<T, long> characterOf)
		{
			if (rows == null || unowned.Count == 0)
			{
				return 0;
			}
			int count = 0;
			foreach (T row in rows)
			{
				if (unowned.Contains(characterOf(row)))
				{
					++count;
				}
			}
			return count;
		}
	}

	/// <summary>
	/// The one ownership check every per-character sub-entity write passes through: a row lands only
	/// while its character's session claim is still held by the writer that captured it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The character row's own write has always carried the ownership triple
	/// (<c>ICharacterService.PersistOwnedAsync</c>), but its sub-entity tables were guarded only by
	/// their version, which does not identify the writer. So a write captured by a session that was
	/// released a moment later — a periodic pass still on the worker, a retry — could land after the
	/// release: over an offline tax debit, over the last trade-settlement residual, or over the next
	/// owner's first save, which restarts its versions from the rows it loaded and so loses the race
	/// it should win. A server whose lease lapsed kept doing it for a whole refresh interval.
	/// </para>
	/// <para>
	/// <b>How.</b> Inside the write's own transaction, one statement takes a <c>FOR SHARE</c> lock on
	/// every named character that is live, <c>Online</c> and claimed under exactly the triple the
	/// writer quotes, in ascending id order, and the write then touches only those characters' rows.
	/// <c>FOR SHARE</c> is the weakest mode an ordinary <c>UPDATE</c> of the character row has to
	/// wait behind, so a release, a claim, the lease refresh and the row save all queue behind the
	/// write rather than interleaving with it; two sub-entity writes for the same character do not
	/// block each other; and the <c>FOR KEY SHARE</c> every character-owned foreign key takes is
	/// unaffected. A release that commits first is seen: under READ COMMITTED the locking read
	/// re-checks the predicate against the version it waited for, so the released row simply stops
	/// matching. Ascending order matches every other multi-row locker of the character table
	/// (<c>CharacterService.LockCharacterRowsAscendingAsync</c>), so no cycle can form.
	/// </para>
	/// <para>
	/// <b>What a refusal looks like.</b> Rows of a live character whose claim the writer does not hold
	/// are not attempted and are reported as <see cref="BulkWriteResult.Unowned"/>, which is part of
	/// <see cref="BulkWriteResult.Filtered"/> — so a caller that clears its dirty marks only when
	/// nothing was filtered keeps them. A write that could touch none of its characters fails: with
	/// <see cref="DatabaseErrorCodes.NotFound"/> when none exists, and with
	/// <see cref="DatabaseErrorCodes.Forbidden"/> when they exist but none is the writer's, the code a
	/// refused character-row save already uses.
	/// </para>
	/// <para>
	/// Passing no claims at all is the ungated write, kept for writers that hold none by design —
	/// character creation and deletion on the login server — and for writes already inside a unit of
	/// work that has asserted ownership itself (the item layer's). It checks only that the character
	/// exists, as every service did before.
	/// </para>
	/// </remarks>
	public static class CharacterWriteGate
	{
		/// <summary>
		/// The whole-write decision, from how many characters the write named, how many of those are
		/// live, and how many of those the writer may write. Pure.
		/// </summary>
		/// <param name="named">Distinct characters the write's rows name.</param>
		/// <param name="live">How many of them exist and are not deleted.</param>
		/// <param name="writable">How many of the live ones the writer may write.</param>
		/// <returns>The decision.</returns>
		public static CharacterWriteGateDecision Decide(int named, int live, int writable)
		{
			if (named <= 0 || writable > 0)
			{
				return CharacterWriteGateDecision.Write;
			}
			return live > 0 ? CharacterWriteGateDecision.NotOwned : CharacterWriteGateDecision.NotFound;
		}

		/// <summary>
		/// Decides which of <paramref name="characterIds"/> this write may touch, and holds that
		/// decision for the rest of the caller's transaction. Throws when it may touch none.
		/// </summary>
		/// <param name="dbContext">The write's context. With claims, its transaction must be open.</param>
		/// <param name="characterIds">Every character the write's rows name. Duplicates are ignored.</param>
		/// <param name="claims">
		/// The claim the writer holds for each character, or null for the ungated write (see the
		/// remarks on <see cref="CharacterWriteGate"/>). A character with no claim here is never
		/// writable by an owned write.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The admission.</returns>
		/// <exception cref="DatabaseEntityNotFoundException">No named character exists.</exception>
		/// <exception cref="DatabaseException">Characters exist but none is claimed by the writer (<see cref="DatabaseErrorCodes.Forbidden"/>).</exception>
		internal static async Task<CharacterWriteAdmission> AdmitAsync(
			NpgsqlDbContext dbContext,
			IEnumerable<long> characterIds,
			IReadOnlyCollection<CharacterSessionLeaseData>? claims,
			CancellationToken cancellationToken)
		{
			var named = new List<long>();
			var seen = new HashSet<long>();
			if (characterIds != null)
			{
				foreach (long id in characterIds)
				{
					if (seen.Add(id))
					{
						named.Add(id);
					}
				}
			}

			if (named.Count == 0)
			{
				return new CharacterWriteAdmission(new HashSet<long>(), new HashSet<long>());
			}

			string characters = dbContext.GetTableName<CharacterEntity>();
			HashSet<long> writable;
			HashSet<long> unowned = new HashSet<long>();
			int live;

			if (claims == null)
			{
				writable = await ReadIdsAsync(dbContext,
					$"SELECT id FROM {characters} WHERE id = ANY(@p0::bigint[]) AND deleted = FALSE",
					new object[] { named.ToArray() },
					cancellationToken).ConfigureAwait(false);
				live = writable.Count;
			}
			else
			{
				/* The lock is only a guarantee while the transaction that took it is open. Outside one
				 * it would be released on the way out and the write would run on a fact about the past,
				 * which is the race this exists to close. */
				if (dbContext.Database.CurrentTransaction == null)
				{
					throw new DatabaseException(
						"An ownership-gated write must run inside a transaction.",
						errorCode: DatabaseErrorCodes.InvalidOperation);
				}

				// One claim per character, and only for characters the rows name.
				var byCharacter = new Dictionary<long, CharacterSessionLeaseData>(named.Count);
				foreach (CharacterSessionLeaseData claim in claims)
				{
					if (claim.IsValid && seen.Contains(claim.CharacterID) && !byCharacter.ContainsKey(claim.CharacterID))
					{
						byCharacter.Add(claim.CharacterID, claim);
					}
				}

				writable = new HashSet<long>();
				if (byCharacter.Count > 0)
				{
					var ids = new long[byCharacter.Count];
					var servers = new long[byCharacter.Count];
					var tokens = new Guid[byCharacter.Count];
					int i = 0;
					foreach (CharacterSessionLeaseData claim in byCharacter.Values)
					{
						ids[i] = claim.CharacterID;
						servers[i] = claim.OwnerServerID;
						tokens[i] = claim.OwnerToken;
						++i;
					}

					/* The predicate and the lock in one statement, and that is safe here although it
					 * is not for a count (see "lock then count"): the rows being tested are the rows
					 * being locked, and a locking read re-evaluates its predicate against the version
					 * it waited for. ORDER BY is applied before the lock, so rows are locked ascending. */
					writable = await ReadIdsAsync(dbContext,
						$@"SELECT c.id
							FROM {characters} AS c
							JOIN UNNEST(@p0::bigint[], @p1::bigint[], @p2::uuid[]) AS u(id, owner_server_id, owner_token)
								ON u.id = c.id
							WHERE c.deleted = FALSE
								AND c.session_state = @p3
								AND c.session_owner_server_id = u.owner_server_id
								AND c.session_owner_token = u.owner_token
							ORDER BY c.id
							FOR SHARE OF c",
						new object[] { ids, servers, tokens, (short)CharacterSessionState.Online },
						cancellationToken).ConfigureAwait(false);
				}

				live = writable.Count;
				if (writable.Count < named.Count)
				{
					// Only to say why the rest were refused: live but not ours, or gone.
					var rest = new List<long>(named.Count - writable.Count);
					foreach (long id in named)
					{
						if (!writable.Contains(id))
						{
							rest.Add(id);
						}
					}
					unowned = await ReadIdsAsync(dbContext,
						$"SELECT id FROM {characters} WHERE id = ANY(@p0::bigint[]) AND deleted = FALSE",
						new object[] { rest.ToArray() },
						cancellationToken).ConfigureAwait(false);
					live += unowned.Count;
				}
			}

			switch (Decide(named.Count, live, writable.Count))
			{
				case CharacterWriteGateDecision.NotFound:
					throw new DatabaseEntityNotFoundException("Character", named[0].ToString(), "Character not found or deleted.");
				case CharacterWriteGateDecision.NotOwned:
					throw new DatabaseException(
						"This server no longer holds the character's session claim; refusing to write its state.",
						errorCode: DatabaseErrorCodes.Forbidden);
				default:
					return new CharacterWriteAdmission(writable, unowned);
			}
		}

		/// <summary>
		/// <see cref="AdmitAsync"/> for a write about one character. Throws unless it may be written.
		/// </summary>
		/// <param name="dbContext">The write's context. With a claim, its transaction must be open.</param>
		/// <param name="characterId">The character.</param>
		/// <param name="claim">The claim the writer holds, or null for the ungated write.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		internal static Task AdmitOneAsync(
			NpgsqlDbContext dbContext,
			long characterId,
			CharacterSessionLeaseData? claim,
			CancellationToken cancellationToken)
		{
			return AdmitAsync(
				dbContext,
				new[] { characterId },
				claim.HasValue ? new[] { claim.Value } : null,
				cancellationToken);
		}

		/// <summary>
		/// Admits a write that UNDOES one of the writer's own rows: while the writer still holds
		/// <paramref name="claim"/>, and also while the character holds no claim at all — released,
		/// and not claimed again yet. Refused once any other session holds it. Throws unless admitted.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Only for a compensating write.</b> The case it exists for is the ability grant's revoke:
		/// the grant's row is written under the claim, and settled (paid for) on the main thread once
		/// it lands. A player who logs out inside that round trip is released on their ordered lane
		/// before the settlement can run, so the revoke that removes the unpaid row always arrives
		/// after the release. Admitted only under the claim, it would be refused and the character
		/// would own an ability nobody paid for from their next login.
		/// </para>
		/// <para>
		/// Removing the writer's own row from a character nobody holds is safe for the reason the
		/// offline tax debit is: no scene server has it loaded, so nothing in memory is left
		/// describing a row that is gone, and the share lock holds off a claim until the delete
		/// commits. A character another session has claimed has loaded the row, and deleting it
		/// under that session would leave its memory and the table disagreeing — that stays refused,
		/// like every other write that does not hold the claim. "No claim" is the ownership
		/// assertion's own definition: not <c>Online</c>, and no owner server recorded.
		/// </para>
		/// </remarks>
		/// <param name="dbContext">The write's context. Its transaction must be open.</param>
		/// <param name="claim">The claim the undone row was written under.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <exception cref="DatabaseEntityNotFoundException">The character does not exist.</exception>
		/// <exception cref="DatabaseException">Another session holds the character (<see cref="DatabaseErrorCodes.Forbidden"/>), or no transaction is open.</exception>
		internal static async Task AdmitOwnOrReleasedAsync(
			NpgsqlDbContext dbContext,
			CharacterSessionLeaseData claim,
			CancellationToken cancellationToken)
		{
			if (!claim.IsValid)
			{
				throw new DatabaseException(
					"An ownership-gated write needs the claim it was captured under; none was supplied.",
					errorCode: DatabaseErrorCodes.ValidationError);
			}
			if (dbContext.Database.CurrentTransaction == null)
			{
				throw new DatabaseException(
					"An ownership-gated write must run inside a transaction.",
					errorCode: DatabaseErrorCodes.InvalidOperation);
			}

			string characters = dbContext.GetTableName<CharacterEntity>();

			/* The same locking read as AdmitAsync, with the unclaimed state as a second way in. A claim
			 * that commits while this waits is seen: the predicate is re-checked against the row it
			 * waited for, and a row now Online under someone else's triple matches neither branch. */
			HashSet<long> writable = await ReadIdsAsync(dbContext,
				$@"SELECT c.id
					FROM {characters} AS c
					WHERE c.id = @p0
						AND c.deleted = FALSE
						AND ((c.session_state = @p3
								AND c.session_owner_server_id = @p1
								AND c.session_owner_token = @p2)
							OR (c.session_state <> @p3
								AND c.session_owner_server_id = 0))
					FOR SHARE OF c",
				new object[] { claim.CharacterID, claim.OwnerServerID, claim.OwnerToken, (short)CharacterSessionState.Online },
				cancellationToken).ConfigureAwait(false);

			int live = writable.Count;
			if (live == 0)
			{
				live = (await ReadIdsAsync(dbContext,
					$"SELECT id FROM {characters} WHERE id = @p0 AND deleted = FALSE",
					new object[] { claim.CharacterID },
					cancellationToken).ConfigureAwait(false)).Count;
			}

			switch (Decide(1, live, writable.Count))
			{
				case CharacterWriteGateDecision.NotFound:
					throw new DatabaseEntityNotFoundException("Character", claim.CharacterID.ToString(), "Character not found or deleted.");
				case CharacterWriteGateDecision.NotOwned:
					throw new DatabaseException(
						"Another session holds the character; refusing to undo a row under it.",
						errorCode: DatabaseErrorCodes.Forbidden);
			}
		}

		/// <summary>
		/// Checks the one claim a single-row owned write was handed, against the character it
		/// writes, before any work starts. Pure.
		/// </summary>
		/// <param name="claim">The claim.</param>
		/// <param name="characterId">The character the write is about.</param>
		/// <returns>Null when usable; otherwise why not.</returns>
		public static string? ValidateClaim(CharacterSessionLeaseData claim, long characterId)
		{
			if (!claim.IsValid)
			{
				return "An ownership-gated write needs the claim it was captured under; none was supplied.";
			}
			if (claim.CharacterID != characterId)
			{
				return "The claim names a different character than the row being written.";
			}
			return null;
		}

		/// <summary>
		/// Checks the claims an owned write was handed before any work starts. Pure.
		/// </summary>
		/// <returns>Null when usable; otherwise why not.</returns>
		public static string? ValidateClaims(IReadOnlyCollection<CharacterSessionLeaseData>? claims)
		{
			if (claims == null || claims.Count == 0)
			{
				return "An ownership-gated write needs the claim it was captured under; none was supplied.";
			}
			return null;
		}

		/// <summary>Reads one bigint column into a set, inside the context's current transaction.</summary>
		private static async Task<HashSet<long>> ReadIdsAsync(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			CancellationToken cancellationToken)
		{
			DbConnection connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			}

			using DbCommand command = connection.CreateCommand();
			command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
			command.CommandText = sql;
			for (int i = 0; i < parameters.Length; i++)
			{
				DbParameter parameter = command.CreateParameter();
				parameter.ParameterName = "@p" + i;
				parameter.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(parameter);
			}

			var ids = new HashSet<long>();
			using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				ids.Add(reader.GetInt64(0));
			}
			return ids;
		}
	}
}
