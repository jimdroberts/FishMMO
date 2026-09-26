using System;
using System.Collections.Generic;
using System.Globalization;
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
	/// Maintenance windows. See <see cref="IMaintenanceService"/> for what durability means here.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The two per-tier services are injected and are the only things that write a server's
	/// control columns. This service reads <c>world_servers</c> and <c>scene_servers</c>
	/// directly to <em>observe</em> them, the way the server board does, but it never writes
	/// them: a second implementation of "schedule a shutdown" that drifted from the first would
	/// be a shard that half stops.
	/// </para>
	/// </remarks>
	public sealed class MaintenanceService : BaseService<MaintenanceOperationEntity>, IMaintenanceService
	{
		/// <summary>Longest drain that may be scheduled: 24 hours.</summary>
		/// <remarks>
		/// The same ceiling the single-server shutdown enforces, and for the same reason: it
		/// bounds a typo rather than a policy. A mistyped drain should be refused, not lock a
		/// shard for eleven weeks.
		/// </remarks>
		public const int MaxDrainSeconds = 86_400;

		/// <summary>Most servers in one window.</summary>
		/// <remarks>
		/// Generous for any real shard — a dozen is a big one — and low enough that a malformed
		/// request cannot ask for ten thousand writes.
		/// </remarks>
		public const int MaxTargets = 64;

		/// <summary>How long without a pulse before a server counts as silent.</summary>
		/// <remarks>Matches the server board, so the two surfaces never disagree about a server.</remarks>
		public const int StaleAfterSeconds = 60;

		/// <summary>
		/// Seconds from <paramref name="stampUtc"/> to <paramref name="databaseNowUtc"/>, never
		/// negative.
		/// </summary>
		/// <remarks>
		/// Both instants must come from the database clock: a stamp it wrote, and a "now" read from
		/// it (<see cref="BaseService{T}.ReadDatabaseUtcNowAsync"/>). Clamped at zero because a pulse
		/// that commits between the clock read and the row read is a few milliseconds newer than
		/// the instant it is measured against, as on the server board.
		/// </remarks>
		public static double AgeSeconds(DateTime databaseNowUtc, DateTime stampUtc)
		{
			return Math.Max(0.0, (databaseNowUtc - stampUtc).TotalSeconds);
		}

		/// <summary>Whether a server whose last pulse is this many seconds old counts as silent.</summary>
		/// <remarks>
		/// Strictly past <see cref="StaleAfterSeconds"/>: a pulse exactly that old is still a
		/// pulsing server. The one rule behind a target's PulsingAtStart, the derivation's "silent"
		/// and the panel's not-pulsing badge.
		/// </remarks>
		public static bool IsSilent(double pulseAgeSeconds)
		{
			return pulseAgeSeconds > StaleAfterSeconds;
		}

		/// <summary>
		/// How long past the deadline a server may still be pulsing before the target is Failed.
		/// </summary>
		/// <remarks>
		/// A healthy server acts on its deadline within a pulse. Two minutes is room for a slow
		/// save and a missed beat; past that, something is wrong and saying "shutting down" would
		/// be a guess dressed as a status.
		/// </remarks>
		public const int ShutdownGraceSeconds = 120;

		/// <summary>The most windows one listing will return.</summary>
		private const int MaxListLimit = 200;

		/// <summary>
		/// How far a server's deadline may differ from the one this window wrote and still count
		/// as the same deadline.
		/// </summary>
		/// <remarks>
		/// A round trip through PostgreSQL is exact; the tolerance is here so a future change of
		/// column precision cannot turn every target into "somebody rescheduled this".
		/// </remarks>
		private const double DeadlineToleranceSeconds = 1.0;

		private readonly IWorldServerService worldServers;
		private readonly ISceneServerService sceneServers;

		public MaintenanceService(
			INpgsqlDbContextFactory dbContextFactory,
			IWorldServerService worldServers,
			ISceneServerService sceneServers) : base(dbContextFactory)
		{
			this.worldServers = worldServers;
			this.sceneServers = sceneServers;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<MaintenanceOperationData>> StartAsync(
			string name,
			string startedBy,
			string reason,
			int drainSeconds,
			IReadOnlyList<MaintenanceTargetRequest> targets,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reason))
			{
				return Failure("A reason is required.");
			}
			if (string.IsNullOrWhiteSpace(startedBy))
			{
				return Failure("The operator account is required.");
			}
			if (drainSeconds < 0)
			{
				return Failure("The drain cannot be negative. Use 0 to stop at the next pulse.");
			}
			if (drainSeconds > MaxDrainSeconds)
			{
				return Failure($"The drain cannot exceed {MaxDrainSeconds} seconds (24 hours).");
			}
			if (targets == null || targets.Count == 0)
			{
				return Failure("A maintenance window needs at least one server.");
			}
			if (targets.Count > MaxTargets)
			{
				return Failure($"A maintenance window takes at most {MaxTargets} servers.");
			}

			// Normalized and de-duplicated up front: the same server named twice would otherwise
			// violate the unique index halfway through and lose the whole window.
			var requested = new List<MaintenanceTargetRequest>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var target in targets)
			{
				if (target == null || !MaintenanceTargetKinds.TryNormalize(target.Kind, out string kind))
				{
					return Failure("A server is either 'world' or 'scene'.");
				}
				if (target.ServerID <= 0)
				{
					return Failure("A server id must be positive.");
				}
				if (seen.Add(kind + "/" + target.ServerID.ToString(CultureInfo.InvariantCulture)))
				{
					requested.Add(new MaintenanceTargetRequest { Kind = kind, ServerID = target.ServerID });
				}
			}

			string windowName = string.IsNullOrWhiteSpace(name) ? "Maintenance" : Clamp(name.Trim(), 128);

			// Taken once, outside the retried delegate. See the probe below.
			Guid requestKey = Guid.NewGuid();

			/* The plan is committed before a single server is touched. If this process dies
			 * between the two, what is left is a window whose targets say "nothing written yet",
			 * which the advance pass finishes — rather than a shard that is locked with no record
			 * of who locked it or why. */
			var created = await ExecuteTransactionAsync(async dbContext =>
			{
				/* A retry after a reply lost past the commit answers with the window its first
				 * attempt planned. It used to plan again, find that window's own targets live, and
				 * refuse with "already in maintenance window N" — telling the operator a window
				 * they had just started had failed, while the advance pass went on to lock and
				 * stop every server in it. First, before the clock read and the clash check, so
				 * the answer does not depend on anything that has moved since. */
				long planned = await dbContext.Set<MaintenanceOperationEntity>()
					.AsNoTracking()
					.Where(o => o.RequestKey == requestKey)
					.Select(o => o.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);
				if (planned > 0)
				{
					return new PlanResult { OperationID = planned };
				}

				/* The deadline is the database's time plus the drain, never this process's. Every
				 * server counts its shutdown down against the database clock (ServerControlSql), so
				 * a deadline built on the panel's DateTime.UtcNow moved every stop in the window by
				 * the panel host's skew — a fifteen-minute drain planned on a panel two minutes slow
				 * gave players thirteen — and PulsingAtStart compared a pulse the database stamped
				 * with the panel's clock. ServerControlSql.ScheduleIn does the same for one server;
				 * here it stays an absolute instant, because one deadline covers the whole window. */
				var ids = requested.Select(t => t.ServerID).Distinct().ToList();

				/* Lock every named server row before the clash check below reads the live windows.
				 * Without it two starts naming the same server could both read "no live window" and
				 * both commit, and the server would sit in two windows, each owning its deadline.
				 * Under READ COMMITTED a statement counts from its own snapshot, so the lock is taken
				 * in one statement and the live windows are read in a later one, which sees a window a
				 * concurrent start committed while this one waited. World rows then scene rows, each in
				 * id order, so two starts over overlapping servers queue instead of deadlocking. */
				long[] lockIds = ids.OrderBy(id => id).ToArray();
				string worldTable = dbContext.GetTableName<WorldServerEntity>();
				string sceneTable = dbContext.GetTableName<SceneServerEntity>();
				await ExecuteScalarLongAsync(dbContext,
					$"SELECT COUNT(*) FROM (SELECT id FROM {worldTable} WHERE id = ANY({{0}}) ORDER BY id FOR UPDATE) locked",
					new object[] { lockIds },
					cancellationToken).ConfigureAwait(false);
				await ExecuteScalarLongAsync(dbContext,
					$"SELECT COUNT(*) FROM (SELECT id FROM {sceneTable} WHERE id = ANY({{0}}) ORDER BY id FOR UPDATE) locked",
					new object[] { lockIds },
					cancellationToken).ConfigureAwait(false);

				// Read after the locks were granted, which may have been a while.
				DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);
				DateTime deadline = now.AddSeconds(drainSeconds);

				var worldRows = await dbContext.WorldServers
					.AsNoTracking()
					.Where(s => ids.Contains(s.ID))
					.Select(s => new { s.ID, s.Name, s.LastPulse })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var sceneRows = await dbContext.SceneServers
					.AsNoTracking()
					.Where(s => ids.Contains(s.ID))
					.Select(s => new { s.ID, s.Name, s.LastPulse })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var worldByID = worldRows.ToDictionary(s => s.ID);
				var sceneByID = sceneRows.ToDictionary(s => s.ID);

				/* A server already inside a live window is refused. Two windows over one server
				 * would each own the deadline column, and cancelling one would silently call off
				 * the other. */
				var liveTargets = await dbContext.Set<MaintenanceTargetEntity>()
					.AsNoTracking()
					.Where(t => ids.Contains(t.ServerID) &&
						(t.Status == MaintenanceStatus.Draining || t.Status == MaintenanceStatus.ShuttingDown))
					.Select(t => new { t.Kind, t.ServerID, t.OperationID, t.ServerName })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var operation = new MaintenanceOperationEntity
				{
					Name = windowName,
					StartedBy = Clamp(startedBy.Trim(), 100),
					Reason = Clamp(reason.Trim(), 1024),
					Status = MaintenanceStatus.Draining,
					DrainSeconds = drainSeconds,
					StartedUtc = now,
					DeadlineUtc = deadline,
					Targets = new List<MaintenanceTargetEntity>(),
					RequestKey = requestKey,
				};

				foreach (var target in requested)
				{
					bool isWorld = target.Kind == MaintenanceTargetKinds.World;
					string serverName;
					DateTime lastPulse;

					if (isWorld)
					{
						if (!worldByID.TryGetValue(target.ServerID, out var row))
						{
							return new PlanResult { Refusal = $"World server {target.ServerID} is not registered." };
						}
						serverName = row.Name;
						lastPulse = row.LastPulse;
					}
					else
					{
						if (!sceneByID.TryGetValue(target.ServerID, out var row))
						{
							return new PlanResult { Refusal = $"Scene server {target.ServerID} is not registered." };
						}
						serverName = row.Name;
						lastPulse = row.LastPulse;
					}

					var clash = liveTargets.FirstOrDefault(t => t.Kind == target.Kind && t.ServerID == target.ServerID);
					if (clash != null)
					{
						return new PlanResult
						{
							Refusal = $"{serverName} is already in maintenance window {clash.OperationID}. Cancel that one first.",
						};
					}

					operation.Targets.Add(new MaintenanceTargetEntity
					{
						Kind = target.Kind,
						ServerID = target.ServerID,
						ServerName = Clamp(serverName, 100),
						Status = MaintenanceStatus.Draining,
						ScheduledShutdownUtc = deadline,
						// Recorded now, because after the window it is unknowable: a server that
						// was already silent and one that stopped because of this window look the
						// same once it is over.
						PulsingAtStart = !IsSilent(AgeSeconds(now, lastPulse)),
						ObservedRegistered = true,
						ObservedLastPulseUtc = lastPulse,
					});
				}

				/* Nothing is added until every target has been checked, so a refusal above is a
				 * transaction that wrote nothing: a window naming eleven good servers and one that
				 * has gone leaves no half-written plan behind. */
				await dbContext.Set<MaintenanceOperationEntity>().AddAsync(operation, cancellationToken).ConfigureAwait(false);
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return new PlanResult { OperationID = operation.ID };
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!created.IsSuccess)
			{
				return DatabaseResult<MaintenanceOperationData>.Failure(
					created.ErrorCode, created.ErrorMessage, created.IsTransient);
			}
			if (created.Data.Refusal != null)
			{
				/* Refused by returning rather than throwing, so the operator is told which server
				 * and why: the layer that maps exceptions sanitizes their messages, which is right
				 * for a database fault and useless for "world-02 is already in window 14". */
				return DatabaseResult<MaintenanceOperationData>.Failure(
					DatabaseErrorCodes.ValidationError, created.Data.Refusal);
			}

			/* Now the servers are actually locked and scheduled, and their statuses derived. This
			 * is the same pass that heals a half-written window, so there is one code path that
			 * writes a target's control columns rather than two that must agree. */
			await AdvanceAsync(cancellationToken).ConfigureAwait(false);

			return await FetchAsync(created.Data.OperationID, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<MaintenanceOperationData>> CancelAsync(
			long operationId,
			string cancelledBy,
			string reason,
			CancellationToken cancellationToken = default)
		{
			if (operationId <= 0)
			{
				return Failure("A maintenance window id is required.");
			}
			if (string.IsNullOrWhiteSpace(cancelledBy))
			{
				return Failure("The operator account is required.");
			}
			if (string.IsNullOrWhiteSpace(reason))
			{
				return Failure("A reason is required.");
			}

			var loaded = await ExecuteReadAsync(async dbContext =>
				await dbContext.Set<MaintenanceOperationEntity>()
					.AsNoTracking()
					.Include(o => o.Targets)
					.FirstOrDefaultAsync(o => o.ID == operationId, cancellationToken)
					.ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!loaded.IsSuccess)
			{
				return DatabaseResult<MaintenanceOperationData>.Failure(
					loaded.ErrorCode, loaded.ErrorMessage, loaded.IsTransient);
			}
			if (loaded.Data == null)
			{
				return DatabaseResult<MaintenanceOperationData>.Failure(
					DatabaseErrorCodes.NotFound, $"There is no maintenance window {operationId}.");
			}
			if (IsTerminal(loaded.Data.Status))
			{
				return DatabaseResult<MaintenanceOperationData>.Failure(
					DatabaseErrorCodes.InvalidOperation,
					$"Maintenance window {operationId} already finished ({loaded.Data.Status}). There is nothing left to cancel.");
			}

			/* One transaction: lock the window's targets, clear each deadline, record the window
			 * cancelled. The clears used to run first, each committed on its own, and a pending
			 * write from an advance pass could land between a clear and the record — scheduling a
			 * server again under a window that then read Cancelled. Under the targets' row locks a
			 * write already in flight finishes first and is cleared here, and a later one finds
			 * Cancelled and does not start: the advance pass's write step re-checks the status
			 * under the same lock (issue #267 audit). A crash part-way rolls the whole cancel
			 * back, leaving the window live and its deadlines standing, which is what it reads. */
			var updated = await ExecuteTransactionAsync(async dbContext =>
			{
				string targetTable = dbContext.GetTableName<MaintenanceTargetEntity>();
				await ExecuteScalarLongAsync(dbContext,
					$"SELECT COUNT(*) FROM (SELECT id FROM {targetTable} WHERE operation_id = {{0}} ORDER BY id FOR UPDATE) locked",
					new object[] { operationId },
					cancellationToken).ConfigureAwait(false);

				// Taken after the locks were granted, which may have been a while, and by the
				// database clock the deadline was planned on.
				DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);

				var operation = await dbContext.Set<MaintenanceOperationEntity>()
					.Include(o => o.Targets)
					.FirstOrDefaultAsync(o => o.ID == operationId, cancellationToken)
					.ConfigureAwait(false);

				if (operation == null || IsTerminal(operation.Status))
				{
					// Somebody else finished it between the read and here; theirs stands.
					return false;
				}

				/* Clearing the deadline, one target at a time, through the same primitive the
				 * single-server control uses, joined to this transaction. It nulls
				 * shutdown_at_utc and TOUCHES NOTHING ELSE — the lock that scheduling set stays
				 * set. That is deliberate upstream (halting a shutdown and reopening a world are
				 * separate decisions) and it is the single most misunderstood thing about this
				 * feature, so every layer above repeats it. */
				foreach (var target in operation.Targets.Where(t => !IsTerminal(t.Status)))
				{
					/* Cleared even for a target this window has no record of writing. A deadline
					 * left behind by a cancelled window is the one outcome nobody would go looking
					 * for, and clearing a column that is already null costs one statement. */
					var result = target.Kind == MaintenanceTargetKinds.World
						? await worldServers.SetShutdownAsync(target.ServerID, null, cancellationToken).ConfigureAwait(false)
						: await sceneServers.SetShutdownAsync(target.ServerID, null, cancellationToken).ConfigureAwait(false);

					target.Status = MaintenanceStatus.Cancelled;
					target.ShutdownClearedUtc = now;
					target.Note = Clamp(result.IsSuccess
						? (target.ShutdownWrittenUtc == null
							? "Nothing had been written to this server by this window; its deadline was cleared anyway, in case it had been."
							: now >= operation.DeadlineUtc
								? "Deadline cleared, but it had already passed — if this server began stopping it will not come back. It is still LOCKED."
								: "Deadline cleared. This server is still LOCKED: scheduling locked it and cancelling does not lift that.")
						: $"The deadline could not be cleared: {result.ErrorMessage} This server may still stop on time.", 1024);
				}

				operation.Status = MaintenanceStatus.Cancelled;
				operation.CancelledBy = Clamp(cancelledBy.Trim(), 100);
				operation.CancelledUtc = now;
				operation.CancelReason = Clamp(reason.Trim(), 1024);
				operation.CompletedUtc = now;

				int stillLocked = operation.Targets.Count(t => t.LockWrittenUtc != null);
				operation.Outcome = Clamp(stillLocked > 0
					? $"Cancelled by {operation.CancelledBy}. {stillLocked} server(s) are STILL LOCKED — cancelling clears the deadline only. Unlock them separately."
					: $"Cancelled by {operation.CancelledBy}. Nothing had been written to any server.", 1024);

				return true;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!updated.IsSuccess)
			{
				return DatabaseResult<MaintenanceOperationData>.Failure(
					updated.ErrorCode, updated.ErrorMessage, updated.IsTransient);
			}

			return await FetchAsync(operationId, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<MaintenanceOperationData>>> ListAsync(
			int limit = 50,
			CancellationToken cancellationToken = default)
		{
			if (limit < 1 || limit > MaxListLimit)
			{
				limit = limit < 1 ? 50 : MaxListLimit;
			}

			// The listing is the moment somebody looks, so it is also the moment the record
			// catches up with the servers. See the interface's remarks on what this does not do.
			await AdvanceAsync(cancellationToken).ConfigureAwait(false);

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var rows = await dbContext.Set<MaintenanceOperationEntity>()
					.AsNoTracking()
					.Include(o => o.Targets)
					.OrderByDescending(o => o.StartedUtc)
					.ThenByDescending(o => o.ID)
					.Take(limit)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				// The ages on the reply are measured here, by the clock that wrote the stamps.
				DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);
				return (IReadOnlyList<MaintenanceOperationData>)rows.Select(row => ToData(row, now)).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<MaintenanceOperationData>> FetchAsync(
			long operationId,
			CancellationToken cancellationToken = default)
		{
			if (operationId <= 0)
			{
				return Failure("A maintenance window id is required.");
			}

			await AdvanceAsync(cancellationToken).ConfigureAwait(false);

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var row = await dbContext.Set<MaintenanceOperationEntity>()
					.AsNoTracking()
					.Include(o => o.Targets)
					.FirstOrDefaultAsync(o => o.ID == operationId, cancellationToken)
					.ConfigureAwait(false);
				if (row == null)
				{
					return null;
				}

				// The ages on the reply are measured here, by the clock that wrote the stamps.
				DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);
				return ToData(row, now);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return result;
			}
			if (result.Data == null)
			{
				return DatabaseResult<MaintenanceOperationData>.Failure(
					DatabaseErrorCodes.NotFound, $"There is no maintenance window {operationId}.");
			}
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> AdvanceAsync(CancellationToken cancellationToken = default)
		{
			// 1. Anything the start did not manage to write. Doing this first means a window that
			//    was interrupted mid-actuation completes rather than sitting there half applied.
			var pending = await ExecuteReadAsync(async dbContext =>
			{
				var rows = await dbContext.Set<MaintenanceTargetEntity>()
					.AsNoTracking()
					.Where(t => t.ShutdownWrittenUtc == null &&
						(t.Status == MaintenanceStatus.Draining || t.Status == MaintenanceStatus.ShuttingDown))
					.Select(t => new PendingWrite
					{
						TargetID = t.ID,
						Kind = t.Kind,
						ServerID = t.ServerID,
						ServerName = t.ServerName,
						Deadline = t.ScheduledShutdownUtc,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return (IReadOnlyList<PendingWrite>)rows;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!pending.IsSuccess)
			{
				return DatabaseResult<int>.Failure(pending.ErrorCode, pending.ErrorMessage, pending.IsTransient);
			}

			/* Each write and the record of it are ONE transaction that holds the target's row
			 * lock, and the derivation below takes the same locks before it reads. So a target is
			 * seen either before its write began or after the write was recorded, never between.
			 *
			 * They used to be two steps: every write first, then one transaction recording them.
			 * Another caller — the panel's 30-second advance, a listing, a second start — that
			 * derived in between found a deadline already passed (a zero-second drain passes it
			 * before the first write lands) with nothing recorded, and failed the target as never
			 * written while the server was locked and about to stop. The record this caller then
			 * tried to apply was dropped, because the window was no longer live (issue #267
			 * audit). */
			var writeFailures = new Dictionary<long, string>();
			foreach (var write in pending.Data)
			{
				if (write.Deadline == null)
				{
					continue;
				}

				var attempt = await WritePendingTargetAsync(write, cancellationToken).ConfigureAwait(false);
				string? failure = attempt.IsSuccess
					? attempt.Data
					: attempt.ErrorMessage ?? "The row could not be written.";
				if (failure != null)
				{
					// Still pending: the next pass tries again, and the derivation says why it waits.
					writeFailures[write.TargetID] = failure;
				}
			}

			// 2. Re-derive every live window from what the servers' own rows say. Re-derivation
			//    rather than stepping a state machine, so two callers racing reach the same answer.
			return await ExecuteTransactionAsync(async dbContext =>
			{
				/* Every live window's targets are locked before anything is read, in id order so
				 * two passes cannot deadlock. A write in flight holds one of these rows until its
				 * record commits, so this waits for it and then reads the result; a write that
				 * starts later re-checks, under the same lock, the status this pass decides. The
				 * reads below are later statements, so under READ COMMITTED they see everything
				 * that committed while this waited. */
				string targetTable = dbContext.GetTableName<MaintenanceTargetEntity>();
				string operationTable = dbContext.GetTableName<MaintenanceOperationEntity>();
				await ExecuteScalarLongAsync(dbContext,
					$@"SELECT COUNT(*) FROM (
						SELECT t.id FROM {targetTable} t
						JOIN {operationTable} o ON o.id = t.operation_id
						WHERE o.status IN ({{0}}, {{1}})
						ORDER BY t.id
						FOR UPDATE OF t) locked",
					new object[] { (int)MaintenanceStatus.Draining, (int)MaintenanceStatus.ShuttingDown },
					cancellationToken).ConfigureAwait(false);

				/* Taken after the locks were granted, which may have been a while, and by the
				 * database clock: every instant this derivation compares — the deadline, each
				 * server's last pulse — was written by it, so a panel whose own clock ran fast
				 * called a pulsing server silent, and failed a window early. */
				DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);

				var operations = await dbContext.Set<MaintenanceOperationEntity>()
					.Include(o => o.Targets)
					.Where(o => o.Status == MaintenanceStatus.Draining || o.Status == MaintenanceStatus.ShuttingDown)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (operations.Count == 0)
				{
					return 0;
				}

				var allTargets = operations.SelectMany(o => o.Targets).ToList();

				var ids = allTargets.Select(t => t.ServerID).Distinct().ToList();

				var worldObservations = await dbContext.WorldServers
					.AsNoTracking()
					.Where(s => ids.Contains(s.ID))
					.Select(s => new Observation
					{
						ServerID = s.ID,
						CharacterCount = s.CharacterCount,
						Locked = s.Locked,
						ShutdownAtUtc = s.ShutdownAtUtc,
						LastPulse = s.LastPulse,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var sceneObservations = await dbContext.SceneServers
					.AsNoTracking()
					.Where(s => ids.Contains(s.ID))
					.Select(s => new Observation
					{
						ServerID = s.ID,
						CharacterCount = s.CharacterCount,
						Locked = s.Locked,
						ShutdownAtUtc = s.ShutdownAtUtc,
						LastPulse = s.LastPulse,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var worldByID = worldObservations.ToDictionary(o => o.ServerID);
				var sceneByID = sceneObservations.ToDictionary(o => o.ServerID);

				foreach (var operation in operations)
				{
					foreach (var target in operation.Targets)
					{
						var lookup = target.Kind == MaintenanceTargetKinds.World ? worldByID : sceneByID;
						lookup.TryGetValue(target.ServerID, out Observation observation);
						writeFailures.TryGetValue(target.ID, out string? writeFailure);
						DeriveTarget(operation, target, observation, writeFailure, now);
					}

					DeriveOperation(operation, now);
				}

				return operations.Count;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Writes one target's lock and deadline and records that it did, in one transaction
		/// that holds the target's row lock throughout.
		/// </summary>
		/// <returns>
		/// Null when there is nothing to report — written, no longer wanted, or refused by the
		/// server tier and recorded as Failed. Otherwise the transient failure that left the
		/// target pending for the next pass.
		/// </returns>
		private async Task<DatabaseResult<string?>> WritePendingTargetAsync(PendingWrite write, CancellationToken cancellationToken)
		{
			return await ExecuteTransactionAsync<string?>(async dbContext =>
			{
				string targetTable = dbContext.GetTableName<MaintenanceTargetEntity>();

				/* The lock is taken first and the reasons to write are re-checked under it: since
				 * the pending read, another pass may have written this target, or a cancel or the
				 * derivation may have finished it. */
				long live = await ExecuteScalarLongAsync(dbContext,
					$@"SELECT COUNT(*) FROM (
						SELECT id FROM {targetTable}
						WHERE id = {{0}} AND shutdown_written_utc IS NULL AND status IN ({{1}}, {{2}})
						FOR UPDATE) locked",
					new object[] { write.TargetID, (int)MaintenanceStatus.Draining, (int)MaintenanceStatus.ShuttingDown },
					cancellationToken).ConfigureAwait(false);
				if (live == 0)
				{
					return null;
				}

				/* A deadline well in the past is NOT written. The retry exists for a write that
				 * failed seconds ago, and a zero-second drain is already a hair past by the time
				 * this runs — hence the grace — but writing a deadline from an hour ago stops
				 * that server the instant it reads the row, with no countdown and no warning to
				 * the players on it, long after the operator stopped watching. The derivation
				 * fails such a target instead, which is what actually happened to it. Judged
				 * under the lock, so no write can begin once the derivation may fail the target —
				 * and by the database clock the derivation reads, so the two agree on that instant
				 * whichever host each runs on. */
				DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);
				if (write.Deadline == null || now > write.Deadline.Value.AddSeconds(ShutdownGraceSeconds))
				{
					return null;
				}

				/* One statement per server: shutdown_at_utc AND locked = true, through the
				 * existing per-tier control, which joins this transaction. This is the whole
				 * actuation of a maintenance window — from here the server itself counts down to
				 * an absolute instant on its own row, warns its players, and stops. Nothing needs
				 * to be running for that. */
				var result = write.Kind == MaintenanceTargetKinds.World
					? await worldServers.SetShutdownAsync(write.ServerID, write.Deadline.Value, cancellationToken).ConfigureAwait(false)
					: await sceneServers.SetShutdownAsync(write.ServerID, write.Deadline.Value, cancellationToken).ConfigureAwait(false);

				if (result.IsSuccess)
				{
					/* Both columns, from one statement. The lock is recorded separately because
					 * it outlives the shutdown: cancelling clears one, not the other. */
					await dbContext.Database.ExecuteSqlRawAsync(
						$"UPDATE {targetTable} SET shutdown_written_utc = {{1}}, lock_written_utc = {{1}} WHERE id = {{0}}",
						new object[] { write.TargetID, now },
						cancellationToken).ConfigureAwait(false);
					return null;
				}

				string error = result.ErrorMessage ?? "The row could not be written.";
				if (result.IsTransient)
				{
					// Its savepoint rolled back, so nothing was written; the next pass tries again.
					return error;
				}

				await dbContext.Database.ExecuteSqlRawAsync(
					$"UPDATE {targetTable} SET status = {{1}}, note = {{2}} WHERE id = {{0}}",
					new object[] { write.TargetID, (int)MaintenanceStatus.Failed, Clamp($"The lock and shutdown could not be written: {error}", 1024) },
					cancellationToken).ConfigureAwait(false);
				return null;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Decides what one target's server rows now mean.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The two tiers end differently and the rules have to know it.</b> A world server
		/// deletes its <c>world_servers</c> row as it exits, so a missing row is success and a
		/// row that is still there did not stop. A scene server keeps its row and instead clears
		/// its own consumed deadline and lock on teardown — without that it would restart into
		/// the passed deadline and stop again forever — so for a scene target a cleared deadline
		/// <em>after</em> the deadline is success, while the same thing before it is somebody
		/// cancelling by hand.
		/// </para>
		/// <para>
		/// Every branch writes a note saying which rule fired. The columns alone cannot
		/// distinguish these cases later, and an operator reading a finished window a week
		/// afterwards deserves the reasoning rather than a bare word.
		/// </para>
		/// </remarks>
		private static void DeriveTarget(
			MaintenanceOperationEntity operation,
			MaintenanceTargetEntity target,
			Observation observation,
			string? writeFailure,
			DateTime now)
		{
			if (IsTerminal(target.Status))
			{
				// Its final snapshot is left exactly as it was when it finished.
				return;
			}

			target.ObservedUtc = now;
			target.ObservedRegistered = observation != null;
			if (observation == null)
			{
				target.ObservedCharacterCount = 0;
				target.ObservedLocked = false;
				target.ObservedShutdownUtc = null;
				target.ObservedLastPulseUtc = null;
			}
			else
			{
				target.ObservedCharacterCount = observation.CharacterCount;
				target.ObservedLocked = observation.Locked;
				target.ObservedShutdownUtc = observation.ShutdownAtUtc;
				target.ObservedLastPulseUtc = observation.LastPulse;
			}

			bool isWorld = target.Kind == MaintenanceTargetKinds.World;
			bool pastDeadline = now >= operation.DeadlineUtc;

			if (target.ShutdownWrittenUtc == null)
			{
				/* Nothing has been written yet, and every pass retries it. Failed only once the
				 * write step has stopped trying — ShutdownGraceSeconds past the deadline, not the
				 * deadline itself. A zero-second drain is past its deadline before its first
				 * write lands, and failing it there called a window that was about to lock its
				 * servers a failure (issue #267 audit). The write step judges the same instant
				 * under the same row lock, so the two can never both act on one target. */
				DateTime lastWrite = (target.ScheduledShutdownUtc ?? operation.DeadlineUtc).AddSeconds(ShutdownGraceSeconds);
				if (now > lastWrite)
				{
					target.Status = MaintenanceStatus.Failed;
					target.Note = Clamp(writeFailure == null
						? "The lock and shutdown were never written to this server, and the deadline has passed. Nothing happened to it."
						: $"The lock and shutdown were never written to this server, and the deadline has passed. Nothing happened to it. The last attempt failed: {writeFailure}", 1024);
				}
				else
				{
					target.Note = writeFailure == null
						? null
						: Clamp(string.Format(CultureInfo.InvariantCulture,
							"Not written yet: {0} Every pass retries it until {1:HH:mm:ss} UTC.", writeFailure, lastWrite), 1024);
				}
				return;
			}

			if (observation == null)
			{
				target.Status = MaintenanceStatus.Completed;
				target.Note = isWorld
					? "Its registration is gone, which is how a world server exits cleanly: it deletes its row on the way out."
					: "Its registration is gone, so the process is no longer registered with the shard.";
				return;
			}

			bool silent = IsSilent(AgeSeconds(now, observation.LastPulse));

			if (observation.ShutdownAtUtc == null)
			{
				if (!pastDeadline)
				{
					target.Status = MaintenanceStatus.Cancelled;
					target.Note = "The deadline was cleared on this server outside this window — by the board, an /admin command, or psql. It is still LOCKED.";
					return;
				}

				if (!isWorld)
				{
					target.Status = MaintenanceStatus.Completed;
					target.Note = "It cleared its own deadline and lock as it exited, which is what a consumed scene-server shutdown looks like. A scene server that has since been restarted leaves the same trace.";
					return;
				}

				target.Status = MaintenanceStatus.Cancelled;
				target.Note = "The deadline was cleared, but the registration is still here — a world server deletes its row when it stops, so this one did not stop.";
				return;
			}

			bool sameDeadline = target.ScheduledShutdownUtc.HasValue &&
				Math.Abs((observation.ShutdownAtUtc.Value - target.ScheduledShutdownUtc.Value).TotalSeconds) <= DeadlineToleranceSeconds;

			if (!sameDeadline)
			{
				target.Status = MaintenanceStatus.Cancelled;
				target.Note = string.Format(CultureInfo.InvariantCulture,
					"This server's deadline now reads {0:HH:mm:ss} UTC, not the {1:HH:mm:ss} UTC this window wrote. It was rescheduled elsewhere, so this window no longer owns it.",
					observation.ShutdownAtUtc.Value,
					target.ScheduledShutdownUtc ?? operation.DeadlineUtc);
				return;
			}

			if (!pastDeadline)
			{
				target.Status = MaintenanceStatus.Draining;
				target.Note = silent
					? "Locked and scheduled, but this server is not pulsing — nothing has read the row, and nothing will until the process is running."
					: null;
				return;
			}

			/* Silent since before the window was planned: the row was written for a process that
			 * was not listening, so it cannot be reported as a clean shutdown. This check comes
			 * before the "stopped pulsing" one below, which would otherwise read a server that
			 * was dead all along as one this window brought down. */
			if (!target.PulsingAtStart && silent)
			{
				target.Status = MaintenanceStatus.Failed;
				target.Note = "This server was not pulsing when the window was planned and has not pulsed since, so nothing ever read the row that was written for it.";
				return;
			}

			if (silent)
			{
				target.Status = MaintenanceStatus.Completed;
				target.Note = isWorld
					? "It stopped pulsing at the deadline but its registration is still here; a clean exit deletes the row, so this process was killed rather than shut down."
					: "It stopped pulsing at the deadline without clearing its own deadline. Clear scene_servers.shutdown_at_utc before restarting it, or it will read the passed deadline and stop again immediately.";
				return;
			}

			if (now >= operation.DeadlineUtc.AddSeconds(ShutdownGraceSeconds))
			{
				target.Status = MaintenanceStatus.Failed;
				target.Note = string.Format(CultureInfo.InvariantCulture,
					"The deadline passed {0:0} seconds ago and this server is still pulsing, with {1} player(s) on it. It has not acted on the row.",
					(now - operation.DeadlineUtc).TotalSeconds,
					observation.CharacterCount);
				return;
			}

			target.Status = MaintenanceStatus.ShuttingDown;
			target.Note = null;
		}

		/// <summary>Rolls the targets up into the window's own status.</summary>
		/// <remarks>
		/// Derived, never claimed: a window is finished when its servers are, not when a
		/// procedure says so. All-cancelled reads as cancelled because that is what happened to
		/// it, even though no operator cancelled it here — somebody cleared every deadline by
		/// hand.
		/// </remarks>
		private static void DeriveOperation(MaintenanceOperationEntity operation, DateTime now)
		{
			var targets = operation.Targets;
			if (targets.Count == 0)
			{
				return;
			}

			if (targets.Any(t => !IsTerminal(t.Status)))
			{
				operation.Status = now >= operation.DeadlineUtc
					? MaintenanceStatus.ShuttingDown
					: MaintenanceStatus.Draining;
				return;
			}

			int completed = targets.Count(t => t.Status == MaintenanceStatus.Completed);
			int cancelled = targets.Count(t => t.Status == MaintenanceStatus.Cancelled);
			int failed = targets.Count(t => t.Status == MaintenanceStatus.Failed);

			operation.Status = failed > 0
				? MaintenanceStatus.Failed
				: cancelled == targets.Count
					? MaintenanceStatus.Cancelled
					: MaintenanceStatus.Completed;

			operation.CompletedUtc = operation.CompletedUtc ?? now;

			int stillLocked = targets.Count(t => t.LockWrittenUtc != null && t.Status != MaintenanceStatus.Completed);
			string lockNote = stillLocked > 0
				? $" {stillLocked} server(s) did not stop and are STILL LOCKED — unlock them separately."
				: string.Empty;

			operation.Outcome = Clamp(
				$"{completed} stopped, {cancelled} cancelled, {failed} failed, of {targets.Count}.{lockNote}", 1024);
		}

		/// <summary>Whether a status is one nothing moves out of.</summary>
		private static bool IsTerminal(MaintenanceStatus status) =>
			status == MaintenanceStatus.Completed ||
			status == MaintenanceStatus.Cancelled ||
			status == MaintenanceStatus.Failed;

		/// <summary>One window as a reply, with its ages measured at <paramref name="databaseNowUtc"/>.</summary>
		/// <param name="operation">The window and its targets.</param>
		/// <param name="databaseNowUtc">The database's time at the read; see <see cref="AgeSeconds"/>.</param>
		private static MaintenanceOperationData ToData(MaintenanceOperationEntity operation, DateTime databaseNowUtc) => new MaintenanceOperationData
		{
			ID = operation.ID,
			Name = operation.Name,
			StartedBy = operation.StartedBy,
			Reason = operation.Reason,
			Status = operation.Status,
			DrainSeconds = operation.DrainSeconds,
			StartedUtc = operation.StartedUtc,
			DeadlineUtc = operation.DeadlineUtc,
			CompletedUtc = operation.CompletedUtc,
			CancelledBy = operation.CancelledBy,
			CancelledUtc = operation.CancelledUtc,
			CancelReason = operation.CancelReason,
			Outcome = operation.Outcome,
			// Not clamped: a deadline that has passed reads as negative, which is what it is.
			SecondsUntilDeadline = (operation.DeadlineUtc - databaseNowUtc).TotalSeconds,
			Targets = (operation.Targets ?? Array.Empty<MaintenanceTargetEntity>())
				.OrderBy(t => t.Kind, StringComparer.Ordinal)
				.ThenBy(t => t.ServerName, StringComparer.OrdinalIgnoreCase)
				.ThenBy(t => t.ServerID)
				.Select(t => new MaintenanceTargetData
				{
					ID = t.ID,
					OperationID = t.OperationID,
					Kind = t.Kind,
					ServerID = t.ServerID,
					ServerName = t.ServerName,
					Status = t.Status,
					ScheduledShutdownUtc = t.ScheduledShutdownUtc,
					LockWrittenUtc = t.LockWrittenUtc,
					ShutdownWrittenUtc = t.ShutdownWrittenUtc,
					ShutdownClearedUtc = t.ShutdownClearedUtc,
					PulsingAtStart = t.PulsingAtStart,
					ObservedUtc = t.ObservedUtc,
					ObservedCharacterCount = t.ObservedCharacterCount,
					ObservedLocked = t.ObservedLocked,
					ObservedShutdownUtc = t.ObservedShutdownUtc,
					ObservedLastPulseUtc = t.ObservedLastPulseUtc,
					ObservedPulseAgeSeconds = t.ObservedLastPulseUtc.HasValue
						? AgeSeconds(databaseNowUtc, t.ObservedLastPulseUtc.Value)
						: (double?)null,
					ObservedRegistered = t.ObservedRegistered,
					Note = t.Note,
				})
				.ToList(),
		};

		private static DatabaseResult<MaintenanceOperationData> Failure(string message) =>
			DatabaseResult<MaintenanceOperationData>.Failure(DatabaseErrorCodes.ValidationError, message);

		private static string Clamp(string value, int max) =>
			string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value.Substring(0, max));

		/// <summary>A server's control columns, as one read saw them.</summary>
		private sealed class Observation
		{
			public long ServerID { get; set; }
			public int CharacterCount { get; set; }
			public bool Locked { get; set; }
			public DateTime? ShutdownAtUtc { get; set; }
			public DateTime LastPulse { get; set; }
		}

		/// <summary>A target whose control columns have not been written yet.</summary>
		private sealed class PendingWrite
		{
			public long TargetID { get; set; }
			public string Kind { get; set; }
			public long ServerID { get; set; }
			public string ServerName { get; set; }
			public DateTime? Deadline { get; set; }
		}

		/// <summary>The outcome of planning a window: an id, or a refusal in the operator's words.</summary>
		private sealed class PlanResult
		{
			public long OperationID { get; set; }

			public string Refusal { get; set; }
		}
	}
}
