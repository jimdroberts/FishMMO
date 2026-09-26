using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Reads and writes ownership of authored plots of land.
	/// </summary>
	public sealed class PlotService : BaseService<PlotEntity>, IPlotService
	{
		public PlotService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> RegisterAsync(long worldServerID, string sceneName, IReadOnlyList<string> plotKeys, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "World server ID must be greater than zero.");
			}
			if (string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Scene name must not be empty.");
			}
			if (plotKeys == null || plotKeys.Count < 1)
			{
				return DatabaseResult<int>.Success(0);
			}

			string[] keys = plotKeys
				.Where(key => !string.IsNullOrWhiteSpace(key))
				.Distinct(StringComparer.Ordinal)
				.ToArray();

			if (keys.Length < 1)
			{
				return DatabaseResult<int>.Success(0);
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, one row per authored key, and DO NOTHING for the ones already
				 * registered.
				 *
				 * Every scene server hosting a channel of this scene runs this on load, describing
				 * the same land, so conflicts are the normal case rather than an error. The
				 * conflict has to be a no-op rather than an update: an update would write over the
				 * ownership of a plot somebody already lives on, every time a server restarts. */
				string sql = $@"INSERT INTO {TableName} (world_server_id, scene_name, plot_key, owner_character_id, owner_guild_id, state, version, time_created)
					SELECT {{0}}, {{1}}, u.plot_key, 0, 0, 0, 1, {{2}}
					FROM UNNEST({{3}}::text[]) AS u(plot_key)
					ON CONFLICT (world_server_id, scene_name, plot_key) DO NOTHING";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerID, sceneName, now, keys },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchBySceneAsync(long worldServerID, string sceneName, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.WorldServerID == worldServerID && e.SceneName == sceneName)
					.OrderBy(e => e.PlotKey)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchByOwnerCharacterAsync(long characterID, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.OwnerCharacterID == characterID)
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchByOwnerGuildAsync(long guildID, CancellationToken cancellationToken = default)
		{
			if (guildID <= 0)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.OwnerGuildID == guildID)
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TryClaimAsync(long plotID, long ownerCharacterID, long ownerGuildID, DateTime? taxDueUtc, int claimedState = 1, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (ownerCharacterID < 0 || ownerGuildID < 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Owner identifiers must not be negative.");
			}
			if ((ownerCharacterID > 0) == (ownerGuildID > 0))
			{
				/* Both set is the contradiction PlotOwner exists to prevent. Neither set is a
				 * release wearing a claim's name, and would hand the plot back rather than over.
				 * Neither is something to store. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "A claim must name exactly one of a character or a guild.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* The WHERE clause pins the plot as unowned, so two players claiming the same
				 * foundation at once produce one winner and one caller that sees zero rows.
				 *
				 * Reading ownership first and then writing would not do this: both reads would see
				 * unowned land, both writes would succeed, and the second would silently evict the
				 * first, who has already paid. Whoever gets the 1 back is the owner; everybody else
				 * has to be told no. */
				/* The NOT EXISTS clause is the design's "one house per player", asked in the same
				 * statement that takes the plot so a claim cannot be judged against a house the
				 * character acquired a moment later.
				 *
				 * It is not the whole enforcement. Two claims on two scene servers can both read no
				 * existing house and both pass this; the partial unique index on owner_character_id
				 * is what turns the second into an error rather than a second house. This clause is
				 * here so the ordinary case — a player who simply already owns somewhere — comes
				 * back as zero rows, which the caller already knows how to report, rather than as a
				 * constraint violation it would have to translate. */
				string sql = $@"UPDATE {TableName}
					SET owner_character_id = {{1}}, owner_guild_id = {{2}}, state = {{5}}, time_claimed = {{3}}, tax_due_utc = {{4}}, tax_delinquent_since_utc = NULL, tax_next_attempt_utc = NULL, version = version + 1
					WHERE id = {{0}} AND owner_character_id = 0 AND owner_guild_id = 0
					  AND ({{1}} = 0 OR NOT EXISTS (
							SELECT 1 FROM {TableName} held WHERE held.owner_character_id = {{1}}
					  ))";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, ownerCharacterID, ownerGuildID, now, (object)taxDueUtc, claimedState },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ReleaseAsync(long plotID, long expectedOwnerCharacterID, long expectedOwnerGuildID, int releasedState = 0, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if ((expectedOwnerCharacterID > 0) == (expectedOwnerGuildID > 0))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "A release must name exactly one of a character or a guild.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the expected owner, not just to the plot. A release that was in flight
				 * while the plot changed hands would otherwise evict its new owner, and the player
				 * who sent it would never know they had done it. */
				string sql = $@"UPDATE {TableName}
					SET owner_character_id = 0, owner_guild_id = 0, state = {{3}}, time_claimed = NULL, tax_due_utc = NULL, tax_delinquent_since_utc = NULL, tax_next_attempt_utc = NULL, version = version + 1
					WHERE id = {{0}} AND owner_character_id = {{1}} AND owner_guild_id = {{2}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedOwnerCharacterID, expectedOwnerGuildID, releasedState },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TrySetStateAsync(long plotID, int expectedState, int newState, long expectedOwnerCharacterID, long expectedOwnerGuildID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (expectedState == newState)
			{
				/* A transition to the state the plot is already in would report success having done
				 * nothing, and a caller waiting on the row count to tell it whether it won a race
				 * would read that as having lost one. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "The new state must differ from the expected one.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to both the state being left and the owner leaving it, so a transition
				 * computed from a stale read cannot land.
				 *
				 * The owner matters as much as the state here. "Finish building" arriving a moment
				 * after the plot was reclaimed for unpaid tax would otherwise mark somebody else's
				 * land — or freshly abandoned land — as occupied, and the state would then describe
				 * a house that is not there. */
				string sql = $@"UPDATE {TableName}
					SET state = {{2}}, version = version + 1
					WHERE id = {{0}} AND state = {{1}} AND owner_character_id = {{3}} AND owner_guild_id = {{4}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedState, newState, expectedOwnerCharacterID, expectedOwnerGuildID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> BackfillTaxDueAsync(long worldServerID, DateTime firstDueUtc, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "World server ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* IS NULL is what makes this idempotent, and it runs on every scene resolve so that
				 * matters. A plot already being billed keeps the date it has; only land claimed
				 * while tax was off is given one. */
				string sql = $@"UPDATE {TableName}
					SET tax_due_utc = {{1}}, tax_next_attempt_utc = NULL, version = version + 1
					WHERE world_server_id = {{0}}
					  AND owner_character_id <> 0
					  AND tax_due_utc IS NULL";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerID, firstDueUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchTaxDueAsync(long worldServerID, DateTime asOfUtc, DateTime afterDueUtc, long afterPlotID, int limit, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0 || limit < 1)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				/* Paged by a (due date, id) keyset rather than by LIMIT from the head. The head is
				 * exactly where the plots that could not be settled this sweep stay — nothing moved
				 * their date — so a sweep that re-read from it after every page could never get past
				 * a page of them. The row comparison is what lets the planner seek the
				 * (world_server_id, tax_due_utc, id) index straight to the cursor.
				 *
				 * A plot deferred because its owner was playing on another server is left out until
				 * its deferral passes; see PlotEntity.TaxNextAttemptUtc. */
				string sql = $@"SELECT p.* FROM {TableName} p
					WHERE p.world_server_id = {{0}}
					  AND p.tax_due_utc IS NOT NULL
					  AND p.tax_due_utc <= {{1}}
					  AND (p.tax_next_attempt_utc IS NULL OR p.tax_next_attempt_utc <= {{1}})
					  AND (p.tax_due_utc, p.id) > ({{2}}, {{3}})
					ORDER BY p.tax_due_utc, p.id
					LIMIT {{4}}";

				List<PlotEntity> plots = await dbContext.Plots
					.FromSqlRaw(sql, worldServerID, asOfUtc, afterDueUtc, afterPlotID, limit)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchTaxDueForOwnersAsync(IReadOnlyCollection<long> characterIDs, DateTime asOfUtc, CancellationToken cancellationToken = default)
		{
			if (characterIDs == null || characterIDs.Count < 1)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			long[] ids = characterIDs.Where(id => id > 0).Distinct().ToArray();
			if (ids.Length < 1)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				/* No world filter and no deferral filter: this is the server holding these characters
				 * asking what they owe, wherever their land is. The deferral exists to stop OTHER
				 * servers re-reading a plot only this one can bill. Bounded without a LIMIT by the
				 * one-house-per-character index it seeks: at most one row per character asked about. */
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => ids.Contains(e.OwnerCharacterID) &&
								e.TaxDueUtc != null &&
								e.TaxDueUtc <= asOfUtc)
					.OrderBy(e => e.TaxDueUtc)
					.ThenBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchByIdsAsync(IReadOnlyCollection<long> plotIDs, CancellationToken cancellationToken = default)
		{
			if (plotIDs == null || plotIDs.Count < 1)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			long[] ids = plotIDs.Where(id => id > 0).Distinct().ToArray();
			if (ids.Length < 1)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => ids.Contains(e.ID))
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>How the offline charge found one owner's character row.</summary>
		private enum OfflineOwnerState : byte
		{
			/// <summary>No server holds the character: its stored balance is the real one.</summary>
			Unclaimed = 0,
			/// <summary>A server holds the character's session; its balance lives in that server's memory.</summary>
			Claimed = 1,
			/// <summary>The character is deleted, or its row is gone. It cannot pay.</summary>
			Gone = 2,
			/// <summary>Another transaction holds the row lock right now. Not attempted.</summary>
			Locked = 3,
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotTaxChargeResult>>> ChargeTaxOfflineAsync(
			IReadOnlyList<PlotTaxCharge> charges,
			int currencyTemplateID,
			DateTime ownedElsewhereRetryUtc,
			int ledgerReason,
			int ledgerState,
			CancellationToken cancellationToken = default)
		{
			if (charges == null || charges.Count < 1)
			{
				return DatabaseResult<List<PlotTaxChargeResult>>.Success(new List<PlotTaxChargeResult>());
			}
			if (ledgerState <= 0)
			{
				/* Zero is the ledger's Unsettled default: a row that reports nothing while still
				 * counting as a movement. CurrencyLedgerService refuses it for the same reason. */
				return DatabaseResult<List<PlotTaxChargeResult>>.Failure(DatabaseErrorCodes.ValidationError, "The ledger state must be a settled one.");
			}

			/* One entry per plot, and one plot per owner. The debit is keyed by character and the
			 * settlement by plot, so an owner named against two plots would pay once and have both
			 * marked paid. The one-house index makes that impossible for rows as they stand, so a
			 * batch that names an owner twice was built wrong and is refused whole. */
			List<PlotTaxCharge> batch = new List<PlotTaxCharge>(charges.Count);
			HashSet<long> seenPlots = new HashSet<long>();
			HashSet<long> seenOwners = new HashSet<long>();
			foreach (PlotTaxCharge charge in charges)
			{
				if (charge.PlotID <= 0 || charge.OwnerCharacterID <= 0 || charge.NextDueUtc <= charge.DueUtc || charge.Amount <= 0)
				{
					return DatabaseResult<List<PlotTaxChargeResult>>.Failure(DatabaseErrorCodes.ValidationError,
						"Every charge needs a plot, an owning character, a next due date later than its due date and an amount greater than zero.");
				}
				if (!seenPlots.Add(charge.PlotID))
				{
					continue;
				}
				if (!seenOwners.Add(charge.OwnerCharacterID))
				{
					return DatabaseResult<List<PlotTaxChargeResult>>.Failure(DatabaseErrorCodes.ValidationError,
						$"Character {charge.OwnerCharacterID} is named against more than one plot.");
				}
				batch.Add(charge);
			}

			/* No idempotency key is taken here, and none is needed. Every write below is pinned to
			 * the due date the sweep read, so a retry after a commit whose reply was lost wins no
			 * period, debits nobody and writes no ledger row: it reports every charge Skipped. */
			return await ExecuteTransactionAsync<List<PlotTaxChargeResult>>(async dbContext =>
			{
				string characterTable = dbContext.GetTableName<CharacterEntity>();
				string attributeTable = dbContext.GetTableName<CharacterAttributeEntity>();
				string ledgerTable = dbContext.GetTableName<CurrencyLedgerEntity>();

				long[] ownerIDs = batch.Select(c => c.OwnerCharacterID).ToArray();

				/* 1. The owners, locked in id order, skipping any row another transaction holds.
				 *
				 * The lock is FOR NO KEY UPDATE, the mode CharacterSessionOwnershipService takes, so
				 * no server can claim one of these characters (log them in) until this commits, and
				 * no server that already holds one can be mid-save underneath the debit. The
				 * "unclaimed" test is that service's: not Online AND no owning server. Keep the two in
				 * step.
				 *
				 * SKIP LOCKED rather than waiting, twice over. A row somebody holds is a login, a
				 * logout or a save in progress — or another scene server sweeping the same page — and
				 * waiting on it is the lock convoy a sweep must not cause. And a transaction that never
				 * waits for a lock cannot be one half of a deadlock. The skipped owner is simply tried
				 * on a later sweep. */
				List<(long ID, bool Deleted, bool Unclaimed)> lockedOwners = await ReadRowsAsync(
					dbContext,
					$@"SELECT c.id, c.deleted, (c.session_state <> {{1}} AND c.session_owner_server_id = 0)
						FROM {characterTable} c
						WHERE c.id = ANY({{0}})
						ORDER BY c.id
						FOR NO KEY UPDATE SKIP LOCKED",
					new object[] { ownerIDs, (short)CharacterSessionState.Online },
					reader => (reader.GetInt64(0), reader.GetBoolean(1), reader.GetBoolean(2)),
					cancellationToken).ConfigureAwait(false);

				Dictionary<long, OfflineOwnerState> ownerStates = new Dictionary<long, OfflineOwnerState>(ownerIDs.Length);
				foreach ((long id, bool deleted, bool unclaimed) in lockedOwners)
				{
					/* Deleted before claimed: a deleted character cannot pay whoever holds it, and its
					 * land must still reach the end of its grace rather than sit owned forever. */
					ownerStates[id] = deleted
						? OfflineOwnerState.Gone
						: unclaimed ? OfflineOwnerState.Unclaimed : OfflineOwnerState.Claimed;
				}

				/* An owner the lock did not return is either locked by somebody else or has no row
				 * at all. Asked apart, because the answers are opposite: one is tried again next
				 * sweep, the other can never pay. */
				long[] unreturned = ownerIDs.Where(id => !ownerStates.ContainsKey(id)).ToArray();
				if (unreturned.Length > 0)
				{
					List<long> present = await ReadRowsAsync(
						dbContext,
						$@"SELECT c.id FROM {characterTable} c WHERE c.id = ANY({{0}})",
						new object[] { unreturned },
						reader => reader.GetInt64(0),
						cancellationToken).ConfigureAwait(false);

					HashSet<long> presentSet = new HashSet<long>(present);
					foreach (long id in unreturned)
					{
						ownerStates[id] = presentSet.Contains(id) ? OfflineOwnerState.Locked : OfflineOwnerState.Gone;
					}
				}

				Dictionary<long, PlotTaxChargeOutcome> outcomes = new Dictionary<long, PlotTaxChargeOutcome>(batch.Count);
				List<PlotTaxCharge> billable = new List<PlotTaxCharge>(batch.Count);
				List<PlotTaxCharge> elsewhere = new List<PlotTaxCharge>();
				foreach (PlotTaxCharge charge in batch)
				{
					switch (ownerStates[charge.OwnerCharacterID])
					{
						case OfflineOwnerState.Unclaimed:
						case OfflineOwnerState.Gone:
							billable.Add(charge);
							break;
						case OfflineOwnerState.Claimed:
							elsewhere.Add(charge);
							outcomes[charge.PlotID] = PlotTaxChargeOutcome.OwnedElsewhere;
							break;
						default:
							outcomes[charge.PlotID] = PlotTaxChargeOutcome.Skipped;
							break;
					}
				}

				/* 2. The bills, won by locking the plots that still hold the date and owner the
				 * sweep read. Whoever holds that lock owns every period the bill covers: nobody can
				 * advance the date until this commits, and by then it has moved past them all. A plot
				 * another server is settling right now is skipped rather than waited for, and a bill
				 * somebody already charged simply does not match. */
				HashSet<long> won = new HashSet<long>();
				if (billable.Count > 0)
				{
					List<long> wonIDs = await ReadRowsAsync(
						dbContext,
						$@"SELECT p.id
							FROM {TableName} p
							JOIN UNNEST({{0}}::bigint[], {{1}}::bigint[], {{2}}::timestamp[]) AS u(plot_id, owner_id, due)
								ON p.id = u.plot_id
							WHERE p.tax_due_utc = u.due
							  AND p.owner_character_id = u.owner_id
							  AND p.owner_guild_id = 0
							ORDER BY p.id
							FOR NO KEY UPDATE OF p SKIP LOCKED",
						new object[]
						{
							billable.Select(c => c.PlotID).ToArray(),
							billable.Select(c => c.OwnerCharacterID).ToArray(),
							billable.Select(c => c.DueUtc).ToArray(),
						},
						reader => reader.GetInt64(0),
						cancellationToken).ConfigureAwait(false);
					won.UnionWith(wonIDs);
				}

				List<PlotTaxCharge> wonCharges = billable.Where(c => won.Contains(c.PlotID)).ToList();
				foreach (PlotTaxCharge charge in billable)
				{
					if (!won.Contains(charge.PlotID))
					{
						outcomes[charge.PlotID] = PlotTaxChargeOutcome.Skipped;
					}
				}

				/* 3. The money, from every owner of a won bill who is really there to pay it, and
				 * only where the balance covers the whole bill: a bill that covers several periods is
				 * paid in full or not at all, exactly as the in-memory charge for a held owner is.
				 * Version-gated as every attribute write is: the row moves to version + 1, which the
				 * owner's next login loads. The row lock from step 1 is what makes a plain in-place
				 * debit safe — nobody holds this character, and nobody can start to until this
				 * commits. One owner per batch (checked above), so each row is debited at most once. */
				HashSet<long> paidOwners = new HashSet<long>();
				List<PlotTaxCharge> payers = wonCharges
					.Where(c => ownerStates[c.OwnerCharacterID] == OfflineOwnerState.Unclaimed)
					.ToList();
				if (payers.Count > 0)
				{
					List<long> debited = await ReadRowsAsync(
						dbContext,
						$@"UPDATE {attributeTable} a
							SET value = a.value - u.amount, version = a.version + 1
							FROM UNNEST({{0}}::bigint[], {{1}}::bigint[]) AS u(character_id, amount)
							WHERE a.character_id = u.character_id
							  AND a.template_id = {{2}}
							  AND a.deleted = FALSE
							  AND a.value >= u.amount
							RETURNING a.character_id",
						new object[]
						{
							payers.Select(c => c.OwnerCharacterID).ToArray(),
							payers.Select(c => c.Amount).ToArray(),
							currencyTemplateID,
						},
						reader => reader.GetInt64(0),
						cancellationToken).ConfigureAwait(false);
					paidOwners.UnionWith(debited);
				}

				/* 4. The bills settled, paid or not, in the same commit as the money. A payment
				 * lifts any earlier missed-payment mark; a miss sets it only where there is none, so
				 * grace keeps running from the first — for a bill of several periods, from the
				 * earliest of them. Either way the date moves past every period the bill covered and
				 * any deferral is cleared. The date pin is repeated although the rows are already
				 * locked: the statement should not depend on step 2 having been the only way in. */
				if (wonCharges.Count > 0)
				{
					await dbContext.Database.ExecuteSqlRawAsync(
						$@"UPDATE {TableName} p
							SET tax_due_utc = u.next_due,
								tax_next_attempt_utc = NULL,
								tax_delinquent_since_utc = CASE WHEN u.paid THEN NULL ELSE COALESCE(p.tax_delinquent_since_utc, u.due) END,
								version = p.version + 1
							FROM UNNEST({{0}}::bigint[], {{1}}::timestamp[], {{2}}::timestamp[], {{3}}::boolean[]) AS u(plot_id, due, next_due, paid)
							WHERE p.id = u.plot_id AND p.tax_due_utc = u.due",
						new object[]
						{
							wonCharges.Select(c => c.PlotID).ToArray(),
							wonCharges.Select(c => c.DueUtc).ToArray(),
							wonCharges.Select(c => c.NextDueUtc).ToArray(),
							wonCharges.Select(c => paidOwners.Contains(c.OwnerCharacterID)).ToArray(),
						},
						cancellationToken).ConfigureAwait(false);

					foreach (PlotTaxCharge charge in wonCharges)
					{
						OfflineOwnerState state = ownerStates[charge.OwnerCharacterID];
						outcomes[charge.PlotID] = paidOwners.Contains(charge.OwnerCharacterID)
							? PlotTaxChargeOutcome.Paid
							: state == OfflineOwnerState.Gone ? PlotTaxChargeOutcome.OwnerGone : PlotTaxChargeOutcome.Unpaid;
					}
				}

				/* 5. Plots whose owner is playing somewhere else step out of the sweep's way until
				 * the deferral passes. The server holding the owner bills them; this only stops every
				 * other server re-reading the same page meanwhile. Pinned to the date read, so a plot
				 * its holder has billed since is not touched, and skipping rather than waiting for
				 * the same reason as above. */
				if (elsewhere.Count > 0)
				{
					await dbContext.Database.ExecuteSqlRawAsync(
						$@"WITH deferred AS (
								SELECT p.id
								FROM {TableName} p
								JOIN UNNEST({{0}}::bigint[], {{1}}::timestamp[]) AS u(plot_id, due) ON p.id = u.plot_id
								WHERE p.tax_due_utc = u.due
								ORDER BY p.id
								FOR NO KEY UPDATE OF p SKIP LOCKED
							)
							UPDATE {TableName} p
							SET tax_next_attempt_utc = {{2}}, version = p.version + 1
							FROM deferred
							WHERE p.id = deferred.id",
						new object[]
						{
							elsewhere.Select(c => c.PlotID).ToArray(),
							elsewhere.Select(c => c.DueUtc).ToArray(),
							ownedElsewhereRetryUtc,
						},
						cancellationToken).ConfigureAwait(false);
				}

				/* No plot_updates mark for a payment, or for a miss. The mark exists to tell other
				 * channels to redraw a plot, and they draw its owner, state, structures and guest list
				 * — none of which a tax charge touches. A mark here only made every scene server
				 * showing the plot re-read it, and its grants, to change nothing. Reclamation, which
				 * does change the owner, marks the plot where it happens. */
				List<PlotTaxCharge> paidCharges = wonCharges.Where(c => paidOwners.Contains(c.OwnerCharacterID)).ToList();
				if (paidCharges.Count > 0)
				{
					/* 6. Paid bills recorded in the economy ledger in the same commit, so a ledger row
					 * exists exactly when its debit does: one row per bill, for the whole amount.
					 * CurrencyLedgerService writes the same shape one row at a time; request_key stays
					 * null because step 2's pin already makes a retry write nothing. */
					await dbContext.Database.ExecuteSqlRawAsync(
						$@"INSERT INTO {ledgerTable} (character_id, amount, reason, state, time_created, request_key)
							SELECT u.character_id, u.amount, {{2}}, {{3}}, timezone('UTC', CURRENT_TIMESTAMP), NULL
							FROM UNNEST({{0}}::bigint[], {{1}}::bigint[]) AS u(character_id, amount)",
						new object[]
						{
							paidCharges.Select(c => c.OwnerCharacterID).ToArray(),
							paidCharges.Select(c => c.Amount).ToArray(),
							ledgerReason,
							ledgerState,
						},
						cancellationToken).ConfigureAwait(false);
				}

				List<PlotTaxChargeResult> results = new List<PlotTaxChargeResult>(batch.Count);
				foreach (PlotTaxCharge charge in batch)
				{
					results.Add(new PlotTaxChargeResult(charge.PlotID, charge.OwnerCharacterID, outcomes[charge.PlotID]));
				}
				return results;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TryAdvanceTaxAsync(long plotID, DateTime expectedDueUtc, DateTime nextDueUtc, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (nextDueUtc <= expectedDueUtc)
			{
				/* A next date that is not later would leave the plot permanently due, charging the
				 * owner on every sweep forever. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "The next tax date must be later than the one being replaced.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the date the caller read. Several scene servers may host this world and
				 * all see the plot come due at once; only the one whose expected date still matches
				 * wins, so the period produces one charge rather than one per server. */
				string sql = $@"UPDATE {TableName}
					SET tax_due_utc = {{2}}, tax_next_attempt_utc = NULL, version = version + 1
					WHERE id = {{0}} AND tax_due_utc = {{1}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedDueUtc, nextDueUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TryRestoreTaxDueAsync(long plotID, DateTime advancedToUtc, DateTime restoreToUtc, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (restoreToUtc >= advancedToUtc)
			{
				// Only ever backwards, and only to undo an advance; forwards is TryAdvanceTaxAsync.
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "The restored tax date must be earlier than the one being undone.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the date this caller advanced it to, exactly as the advance is pinned to
				 * the date it read: if another server has since won a later period, the row no longer
				 * holds advancedToUtc and this changes nothing. */
				string sql = $@"UPDATE {TableName}
					SET tax_due_utc = {{2}}, tax_next_attempt_utc = NULL, version = version + 1
					WHERE id = {{0}} AND tax_due_utc = {{1}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, advancedToUtc, restoreToUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> MarkTaxDelinquentAsync(long plotID, DateTime delinquentSinceUtc, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* IS NULL in the WHERE clause is what keeps the grace clock running from the first
				 * miss. Without it every later failure would reset the date and an owner who never
				 * pays would never run out of grace. */
				string sql = $@"UPDATE {TableName}
					SET tax_delinquent_since_utc = {{1}}, version = version + 1
					WHERE id = {{0}} AND tax_delinquent_since_utc IS NULL";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, delinquentSinceUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ClearTaxDelinquencyAsync(long plotID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"UPDATE {TableName}
					SET tax_delinquent_since_utc = NULL, version = version + 1
					WHERE id = {{0}} AND tax_delinquent_since_utc IS NOT NULL";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ReleaseAllForGuildAsync(long guildID, CancellationToken cancellationToken = default)
		{
			if (guildID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Guild ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"UPDATE {TableName}
					SET owner_guild_id = 0, state = 0, time_claimed = NULL, tax_due_utc = NULL, tax_delinquent_since_utc = NULL, tax_next_attempt_utc = NULL, version = version + 1
					WHERE owner_guild_id = {{0}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { guildID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps plot entities to their data transfer objects.
		/// </summary>
		private static List<PlotData> MapMany(List<PlotEntity> plots)
		{
			List<PlotData> results = new List<PlotData>(plots.Count);
			foreach (PlotEntity plot in plots)
			{
				results.Add(new PlotData(plot.ID, plot.WorldServerID, plot.SceneName, plot.PlotKey, plot.OwnerCharacterID, plot.OwnerGuildID, plot.TimeClaimed, plot.TaxDueUtc, plot.TaxDelinquentSinceUtc, plot.State));
			}
			return results;
		}
	}
}
