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

			DateTime now = DateTime.UtcNow;
			DateTime deadline = now.AddSeconds(drainSeconds);
			string windowName = string.IsNullOrWhiteSpace(name) ? "Maintenance" : Clamp(name.Trim(), 128);

			/* The plan is committed before a single server is touched. If this process dies
			 * between the two, what is left is a window whose targets say "nothing written yet",
			 * which the advance pass finishes — rather than a shard that is locked with no record
			 * of who locked it or why. */
			var created = await ExecuteTransactionAsync(async dbContext =>
			{
				var ids = requested.Select(t => t.ServerID).Distinct().ToList();

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
						PulsingAtStart = (now - lastPulse).TotalSeconds <= StaleAfterSeconds,
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

			DateTime now = DateTime.UtcNow;

			/* Clearing the deadline, one target at a time, through the same primitive the
			 * single-server control uses. It nulls shutdown_at_utc and TOUCHES NOTHING ELSE — the
			 * lock that scheduling set stays set. That is deliberate upstream (halting a shutdown
			 * and reopening a world are separate decisions) and it is the single most
			 * misunderstood thing about this feature, so every layer above repeats it. */
			var cleared = new Dictionary<long, string>();
			foreach (var target in loaded.Data.Targets.Where(t => !IsTerminal(t.Status)))
			{
				/* Cleared even for a target this window has no record of writing. The advance pass
				 * could have written it a moment ago and not yet recorded the fact, and a deadline
				 * left behind by a cancelled window is the one outcome nobody would go looking
				 * for. Clearing a column that is already null costs one statement. */
				var result = target.Kind == MaintenanceTargetKinds.World
					? await worldServers.SetShutdownAsync(target.ServerID, null, cancellationToken).ConfigureAwait(false)
					: await sceneServers.SetShutdownAsync(target.ServerID, null, cancellationToken).ConfigureAwait(false);

				cleared[target.ID] = result.IsSuccess
					? (target.ShutdownWrittenUtc == null
						? "Nothing had been written to this server by this window; its deadline was cleared anyway, in case it had been."
						: now >= loaded.Data.DeadlineUtc
							? "Deadline cleared, but it had already passed — if this server began stopping it will not come back. It is still LOCKED."
							: "Deadline cleared. This server is still LOCKED: scheduling locked it and cancelling does not lift that.")
					: $"The deadline could not be cleared: {result.ErrorMessage} This server may still stop on time.";
			}

			var updated = await ExecuteTransactionAsync(async dbContext =>
			{
				var operation = await dbContext.Set<MaintenanceOperationEntity>()
					.Include(o => o.Targets)
					.FirstOrDefaultAsync(o => o.ID == operationId, cancellationToken)
					.ConfigureAwait(false);

				if (operation == null || IsTerminal(operation.Status))
				{
					// Somebody else finished it between the read and here; theirs stands.
					return false;
				}

				foreach (var target in operation.Targets.Where(t => !IsTerminal(t.Status)))
				{
					target.Status = MaintenanceStatus.Cancelled;
					target.ShutdownClearedUtc = now;
					if (cleared.TryGetValue(target.ID, out string note))
					{
						target.Note = Clamp(note, 1024);
					}
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

				return (IReadOnlyList<MaintenanceOperationData>)rows.Select(ToData).ToList();
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

				return row == null ? null : ToData(row);
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

			DateTime now = DateTime.UtcNow;
			var writes = new Dictionary<long, WriteOutcome>();

			foreach (var write in pending.Data)
			{
				if (write.Deadline == null)
				{
					continue;
				}

				/* A deadline well in the past is NOT written. The retry exists for a write that
				 * failed seconds ago, and a zero-second drain is already a hair past by the time
				 * this runs — hence the grace — but writing a deadline from an hour ago stops
				 * that server the instant it reads the row, with no countdown and no warning to
				 * the players on it, long after the operator stopped watching. Those targets are
				 * failed below instead, which is what actually happened to them. */
				if (now > write.Deadline.Value.AddSeconds(ShutdownGraceSeconds))
				{
					continue;
				}

				/* One statement per server: shutdown_at_utc AND locked = true, through the
				 * existing per-tier control. This is the whole actuation of a maintenance
				 * window — from here the server itself counts down to an absolute instant on its
				 * own row, warns its players, and stops. Nothing needs to be running for that. */
				var result = write.Kind == MaintenanceTargetKinds.World
					? await worldServers.SetShutdownAsync(write.ServerID, write.Deadline.Value, cancellationToken).ConfigureAwait(false)
					: await sceneServers.SetShutdownAsync(write.ServerID, write.Deadline.Value, cancellationToken).ConfigureAwait(false);

				writes[write.TargetID] = result.IsSuccess
					? new WriteOutcome { WrittenUtc = now }
					: new WriteOutcome { Error = result.ErrorMessage ?? "The row could not be written." };
			}

			// 2. Apply those outcomes and re-derive every live window from what the servers' own
			//    rows say. Re-derivation rather than stepping a state machine, so two callers
			//    racing reach the same answer.
			return await ExecuteTransactionAsync(async dbContext =>
			{
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

				foreach (var target in allTargets)
				{
					if (!writes.TryGetValue(target.ID, out var outcome))
					{
						continue;
					}
					if (outcome.Error == null)
					{
						/* Both columns, from one statement. The lock is recorded separately
						 * because it outlives the shutdown: cancelling clears one, not the other. */
						target.ShutdownWrittenUtc = outcome.WrittenUtc;
						target.LockWrittenUtc = outcome.WrittenUtc;
					}
					else
					{
						target.Status = MaintenanceStatus.Failed;
						target.Note = Clamp($"The lock and shutdown could not be written: {outcome.Error}", 1024);
					}
				}

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
						DeriveTarget(operation, target, observation, now);
					}

					DeriveOperation(operation, now);
				}

				return operations.Count;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
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
				// Nothing has been written yet. The next advance pass retries it; once the
				// deadline has gone by, retrying would schedule a shutdown in the past.
				if (pastDeadline)
				{
					target.Status = MaintenanceStatus.Failed;
					target.Note = "The lock and shutdown were never written to this server, and the deadline has passed. Nothing happened to it.";
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

			double pulseAgeSeconds = (now - observation.LastPulse).TotalSeconds;
			bool silent = pulseAgeSeconds > StaleAfterSeconds;

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

		private static MaintenanceOperationData ToData(MaintenanceOperationEntity operation) => new MaintenanceOperationData
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

		/// <summary>What happened when one target's columns were written.</summary>
		private sealed class WriteOutcome
		{
			public DateTime WrittenUtc { get; set; }
			public string Error { get; set; }
		}

		/// <summary>The outcome of planning a window: an id, or a refusal in the operator's words.</summary>
		private sealed class PlanResult
		{
			public long OperationID { get; set; }

			public string Refusal { get; set; }
		}
	}
}
