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

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// The daemon control plane. See <see cref="IDaemonService"/> for the security model.
	/// </summary>
	public sealed class DaemonService : BaseService<DaemonCommandEntity>, IDaemonService
	{
		/// <summary>The most commands one poll will take.</summary>
		private const int MaxClaimBatch = 10;

		/// <summary>The most history rows one page will return.</summary>
		private const int MaxPageSize = 200;

		public DaemonService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> HeartbeatAsync(
			string hostName,
			string daemonVersion,
			string osDescription,
			int processorCount,
			DateTime startedUtc,
			IReadOnlyList<DaemonAppReport> apps,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(hostName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A host name is required.");
			}

			string host = hostName.Trim();
			var reported = apps ?? Array.Empty<DaemonAppReport>();
			DateTime now = DateTime.UtcNow;

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var hostRow = await dbContext.DaemonHosts
					.FirstOrDefaultAsync(h => h.HostName == host, cancellationToken)
					.ConfigureAwait(false);

				if (hostRow == null)
				{
					hostRow = new DaemonHostEntity { HostName = host, StartedUtc = startedUtc };
					await dbContext.DaemonHosts.AddAsync(hostRow, cancellationToken).ConfigureAwait(false);
				}

				hostRow.DaemonVersion = Clamp(daemonVersion, 64);
				hostRow.OSDescription = Clamp(osDescription, 256);
				hostRow.ProcessorCount = processorCount;
				hostRow.StartedUtc = startedUtc;
				hostRow.LastHeartbeatUtc = now;

				// The host row must exist before its applications can point at it.
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

				/* Lock the host row, and hold it to the commit.
				 *
				 * What follows is read-compare-write: the stored applications are read, compared
				 * against this report, overwritten, and an event row is appended for each
				 * difference. Two heartbeats for one host that interleaved would compare against
				 * the same stored values, and one of the two transitions would simply not exist
				 * anywhere — which is the failure this whole table is meant to prevent.
				 *
				 * That is not hypothetical. A daemon restarted mid-poll beats as soon as it comes
				 * up, overlapping the last beat of the instance going away, and a restarted daemon
				 * is exactly when process ids change.
				 *
				 * The host row rather than the application rows, because a first sighting has no
				 * application row to lock yet, and a first sighting is precisely a case where two
				 * writers race to insert. */
				string hostTable = dbContext.GetTableName<DaemonHostEntity>();
				await dbContext.Database.ExecuteSqlRawAsync(
					$"SELECT id FROM {hostTable} WHERE id = {{0}} FOR UPDATE",
					new object[] { hostRow.ID },
					cancellationToken).ConfigureAwait(false);

				var existing = await dbContext.DaemonApps
					.Where(a => a.HostID == hostRow.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var byName = existing.ToDictionary(a => a.Name, StringComparer.Ordinal);
				var seen = new HashSet<string>(StringComparer.Ordinal);

				foreach (var report in reported)
				{
					if (string.IsNullOrWhiteSpace(report?.Name))
					{
						continue;
					}
					string name = report.Name.Trim();

					/* One report per name per beat. A configuration that named an application
					 * twice would otherwise insert a second row and violate the unique index,
					 * aborting the whole transaction — taking the heartbeat and every event
					 * already appended in it down with a duplicate that changes nothing. */
					if (!seen.Add(name))
					{
						continue;
					}

					bool isNew = !byName.TryGetValue(name, out var row);
					if (isNew)
					{
						row = new DaemonAppEntity { HostID = hostRow.ID, Name = Clamp(name, 128) };
						await dbContext.DaemonApps.AddAsync(row, cancellationToken).ConfigureAwait(false);
					}

					/* Read what was stored before the assignments below destroy it. This row is
					 * the only record of what was true a moment ago: it is overwritten on every
					 * beat, which is why a fault that has already been recovered from currently
					 * leaves no trace of any kind. */
					DaemonAppStatus? previousStatus = isNew ? (DaemonAppStatus?)null : row.Status;
					int? previousProcessID = isNew ? null : row.ProcessID;
					int? previousRestartAttempts = isNew ? (int?)null : row.RestartAttempts;

					row.Status = report.Status;
					row.ProcessID = report.ProcessID;
					row.RestartAttempts = report.RestartAttempts;
					row.MaxRestartAttempts = report.MaxRestartAttempts;
					row.MonitoredPort = report.MonitoredPort;
					row.LastReportedUtc = now;

					/* An event only when something an operator would care about actually moved.
					 *
					 * This runs for every supervised application on every poll — ten seconds by
					 * default — so a row per beat would be some 8,600 per application per day,
					 * all of them saying nothing happened, and the restart loop somebody came to
					 * this page to find would be buried in them. An unchanged heartbeat writes
					 * nothing at all.
					 *
					 * A process id that changed counts on its own, without a status change: a
					 * crash and a relaunch that both complete inside one poll interval read as
					 * Healthy on both sides, and the new process id is the only evidence left
					 * that the server an operator was looking at is gone.
					 *
					 * A restart counter that FELL counts too. The daemon zeroes it the moment the
					 * application checks out healthy again, so a fall is what a recovery looks
					 * like; treating it as noise would drop the end of every restart loop. */
					bool changed = isNew
						|| previousStatus != report.Status
						|| previousProcessID != report.ProcessID
						|| previousRestartAttempts != report.RestartAttempts;

					if (changed)
					{
						await dbContext.DaemonAppEvents.AddAsync(new DaemonAppEventEntity
						{
							/* Host and application copied as text rather than keyed to the rows
							 * above, so that decommissioning a machine does not delete the record
							 * of what it did. */
							HostName = Clamp(host, 128),
							AppName = Clamp(name, 128),
							PreviousStatus = previousStatus,
							Status = report.Status,
							ProcessID = report.ProcessID,
							PreviousProcessID = previousProcessID,
							RestartAttempts = report.RestartAttempts,
							PreviousRestartAttempts = previousRestartAttempts,
							/* The database's clock, not the reporting host's. A machine with a
							 * wrong clock would otherwise reorder the history of every other
							 * machine on the page, and a lying one could do it deliberately. */
							ObservedUtc = now,
						}, cancellationToken).ConfigureAwait(false);
					}
				}

				/* Anything the daemon no longer supervises is removed rather than left behind.
				 * A stale row is a target an operator can still aim a command at, and the
				 * command would be refused on that host — so the queue would carry work that
				 * cannot succeed, for an application that no longer exists. */
				var reportedNames = new HashSet<string>(
					reported.Where(r => !string.IsNullOrWhiteSpace(r?.Name)).Select(r => r.Name.Trim()),
					StringComparer.Ordinal);

				foreach (var row in existing.Where(a => !reportedNames.Contains(a.Name)))
				{
					dbContext.DaemonApps.Remove(row);
				}

				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<DaemonCommandData>>> ClaimCommandsAsync(
			string hostName,
			string instanceId,
			int maxCommands,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(hostName))
			{
				return DatabaseResult<IReadOnlyList<DaemonCommandData>>.Failure(
					DatabaseErrorCodes.ValidationError, "A host name is required.");
			}
			if (string.IsNullOrWhiteSpace(instanceId))
			{
				return DatabaseResult<IReadOnlyList<DaemonCommandData>>.Failure(
					DatabaseErrorCodes.ValidationError, "An instance id is required.");
			}
			if (maxCommands < 1 || maxCommands > MaxClaimBatch)
			{
				maxCommands = MaxClaimBatch;
			}

			string host = hostName.Trim();
			string instance = Clamp(instanceId.Trim(), 128);

			return await ExecuteReadAsync(async dbContext =>
			{
				/* One statement selects and claims. Written in raw SQL because EF cannot express
				 * FOR UPDATE SKIP LOCKED, and that clause is the whole point: two daemons
				 * polling at once take disjoint sets rather than blocking or — far worse — both
				 * returning the same row and restarting a server twice.
				 *
				 * The table is schema-qualified through GetTableName: an unqualified name
				 * resolves against the connection's search_path, and a claim that silently
				 * matched nothing would look exactly like an empty queue.
				 *
				 * host_name is a parameter, and it is the daemon's own. A daemon cannot ask for
				 * another host's work. */
				string table = dbContext.GetTableName<DaemonCommandEntity>();
				string sql = $@"
					UPDATE {table} SET claimed_utc = {{2}}, claimed_by = {{1}}
					WHERE id IN (
						SELECT id FROM {table}
						WHERE host_name = {{0}}
						  AND claimed_utc IS NULL
						  AND expires_utc > {{2}}
						ORDER BY requested_utc, id
						FOR UPDATE SKIP LOCKED
						LIMIT {{3}}
					)
					RETURNING id, host_name, app_name, verb, requested_by, requested_utc, reason,
					          expires_utc, claimed_utc, claimed_by, completed_utc, succeeded, outcome";

				var claimed = await dbContext.Set<DaemonCommandEntity>()
					.FromSqlRaw(sql, host, instance, DateTime.UtcNow, maxCommands)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return (IReadOnlyList<DaemonCommandData>)claimed.Select(MapCommand).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> CompleteCommandAsync(
			long commandId,
			string instanceId,
			bool succeeded,
			string outcome,
			CancellationToken cancellationToken = default)
		{
			if (commandId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A command id is required.");
			}
			if (string.IsNullOrWhiteSpace(instanceId))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "An instance id is required.");
			}

			string instance = instanceId.Trim();

			return await ExecuteWriteAsync(async dbContext =>
			{
				var command = await dbContext.DaemonCommands
					.FirstOrDefaultAsync(c => c.ID == commandId, cancellationToken)
					.ConfigureAwait(false);

				if (command == null)
				{
					throw new DatabaseEntityNotFoundException("DaemonCommand", commandId.ToString());
				}

				/* Only the claimant may report. Otherwise a second daemon — or anything else
				 * with the connection — could overwrite the outcome of work it did not do, and
				 * the history would say a restart succeeded on the word of a process that never
				 * ran it. */
				if (!string.Equals(command.ClaimedBy, instance, StringComparison.Ordinal))
				{
					throw new DatabaseException(
						"That command was claimed by another daemon instance.",
						errorCode: DatabaseErrorCodes.Forbidden);
				}

				command.CompletedUtc = DateTime.UtcNow;
				command.Succeeded = succeeded;
				command.Outcome = Clamp(outcome, 1024);

				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<DaemonHostData>>> FetchHostsAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				var hosts = await dbContext.DaemonHosts
					.AsNoTracking()
					.OrderBy(h => h.HostName)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var apps = await dbContext.DaemonApps
					.AsNoTracking()
					.OrderBy(a => a.Name)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var byHost = apps.GroupBy(a => a.HostID).ToDictionary(g => g.Key, g => g.ToList());

				return (IReadOnlyList<DaemonHostData>)hosts.Select(h => new DaemonHostData
				{
					ID = h.ID,
					HostName = h.HostName,
					DaemonVersion = h.DaemonVersion,
					OSDescription = h.OSDescription,
					ProcessorCount = h.ProcessorCount,
					StartedUtc = h.StartedUtc,
					LastHeartbeatUtc = h.LastHeartbeatUtc,
					Apps = byHost.TryGetValue(h.ID, out var list)
						? list.Select(a => MapApp(a, h.HostName)).ToList()
						: new List<DaemonAppData>(),
				}).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> EnqueueCommandAsync(
			string hostName,
			string appName,
			DaemonCommandVerb verb,
			string requestedBy,
			string reason,
			TimeSpan lifetime,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(hostName) || string.IsNullOrWhiteSpace(appName))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "A host and an application are required.");
			}
			if (string.IsNullOrWhiteSpace(requestedBy))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "A requesting account is required.");
			}
			if (string.IsNullOrWhiteSpace(reason))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "A reason is required.");
			}

			/* Bounded against the enum rather than trusted. The verb reaches here as an integer
			 * from an HTTP body, and a value outside the enum must not be stored — a daemon
			 * reading one would have to decide what to do with a command it cannot name. */
			if (!Enum.IsDefined(typeof(DaemonCommandVerb), verb))
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "That is not a daemon command.");
			}

			if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1))
			{
				// An hour is already generous for "somebody is waiting for this to happen".
				lifetime = TimeSpan.FromMinutes(5);
			}

			string host = hostName.Trim();
			string app = appName.Trim();
			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* The target must be something a daemon actually reported supervising. This is
				 * what stops the queue being used to invent a target: the set of things a
				 * command can name is exactly the set some daemon already runs, and that set
				 * comes from a configuration file on that host. */
				var hostRow = await dbContext.DaemonHosts
					.AsNoTracking()
					.FirstOrDefaultAsync(h => h.HostName == host, cancellationToken)
					.ConfigureAwait(false);

				if (hostRow == null)
				{
					throw new DatabaseException($"No daemon has reported a host named '{host}'.",
						errorCode: DatabaseErrorCodes.NotFound);
				}

				bool known = await dbContext.DaemonApps
					.AsNoTracking()
					.AnyAsync(a => a.HostID == hostRow.ID && a.Name == app, cancellationToken)
					.ConfigureAwait(false);

				if (!known)
				{
					throw new DatabaseException($"'{host}' does not supervise an application named '{app}'.",
						errorCode: DatabaseErrorCodes.NotFound);
				}

				var command = new DaemonCommandEntity
				{
					HostName = Clamp(host, 128),
					AppName = Clamp(app, 128),
					Verb = verb,
					RequestedBy = Clamp(requestedBy.Trim(), 100),
					RequestedUtc = now,
					Reason = Clamp(reason.Trim(), 1024),
					ExpiresUtc = now + lifetime,
				};

				await dbContext.DaemonCommands.AddAsync(command, cancellationToken).ConfigureAwait(false);
				await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return command.ID;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DaemonCommandPage>> FetchCommandsAsync(
			string hostName,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			if (page < 1)
			{
				page = 1;
			}
			if (pageSize < 1)
			{
				pageSize = 50;
			}
			if (pageSize > MaxPageSize)
			{
				pageSize = MaxPageSize;
			}

			string host = string.IsNullOrWhiteSpace(hostName) ? null : hostName.Trim();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<DaemonCommandEntity> q = dbContext.DaemonCommands.AsNoTracking();
				if (host != null)
				{
					q = q.Where(c => c.HostName == host);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					.OrderByDescending(c => c.RequestedUtc)
					.ThenByDescending(c => c.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new DaemonCommandPage
				{
					Items = rows.Select(MapCommand).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DaemonAppEventPage>> FetchAppEventsAsync(
			string hostName,
			string appName,
			DaemonAppStatus? status,
			DateTime? fromUtc,
			DateTime? toUtc,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			if (page < 1)
			{
				page = 1;
			}
			if (pageSize < 1)
			{
				pageSize = 50;
			}
			if (pageSize > MaxPageSize)
			{
				pageSize = MaxPageSize;
			}

			/* Bounded against the enumeration rather than passed through. The value arrives as an
			 * integer from a query string, and one outside the enum can match no row that this
			 * service ever wrote — so answering with an empty page would tell an operator there
			 * were no events when the question itself was meaningless. */
			if (status.HasValue && !Enum.IsDefined(typeof(DaemonAppStatus), status.Value))
			{
				return DatabaseResult<DaemonAppEventPage>.Failure(
					DatabaseErrorCodes.ValidationError, "That is not an application status a daemon reports.");
			}

			/* An inverted window matches nothing, for the same reason: silently returning an empty
			 * page reads as "nothing happened in that period", which is a different claim. */
			if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
			{
				return DatabaseResult<DaemonAppEventPage>.Failure(
					DatabaseErrorCodes.ValidationError, "That time window ends before it begins.");
			}

			string host = string.IsNullOrWhiteSpace(hostName) ? null : hostName.Trim();
			string app = string.IsNullOrWhiteSpace(appName) ? null : appName.Trim();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<DaemonAppEventEntity> q = dbContext.DaemonAppEvents.AsNoTracking();

				if (host != null)
				{
					q = q.Where(e => e.HostName == host);
				}
				if (app != null)
				{
					q = q.Where(e => e.AppName == app);
				}
				if (status.HasValue)
				{
					q = q.Where(e => e.Status == status.Value);
				}
				if (fromUtc.HasValue)
				{
					q = q.Where(e => e.ObservedUtc >= fromUtc.Value);
				}
				if (toUtc.HasValue)
				{
					q = q.Where(e => e.ObservedUtc <= toUtc.Value);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				/* ID breaks ties inside a timestamp. Every application on one host is recorded at
				 * the same instant — they share one heartbeat — so without it the order inside a
				 * beat is whatever the plan happened to produce, and a row could appear on two
				 * pages or on neither. */
				var rows = await q
					.OrderByDescending(e => e.ObservedUtc)
					.ThenByDescending(e => e.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new DaemonAppEventPage
				{
					Items = rows.Select(MapEvent).ToList(),
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static string Clamp(string value, int max) =>
			string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		private static DaemonAppData MapApp(DaemonAppEntity a, string hostName) => new DaemonAppData
		{
			ID = a.ID,
			HostName = hostName,
			Name = a.Name,
			Status = a.Status,
			ProcessID = a.ProcessID,
			RestartAttempts = a.RestartAttempts,
			MaxRestartAttempts = a.MaxRestartAttempts,
			MonitoredPort = a.MonitoredPort,
			LastReportedUtc = a.LastReportedUtc,
		};

		private static DaemonCommandData MapCommand(DaemonCommandEntity c) => new DaemonCommandData
		{
			ID = c.ID,
			HostName = c.HostName,
			AppName = c.AppName,
			Verb = c.Verb,
			RequestedBy = c.RequestedBy,
			RequestedUtc = c.RequestedUtc,
			Reason = c.Reason,
			ExpiresUtc = c.ExpiresUtc,
			ClaimedUtc = c.ClaimedUtc,
			ClaimedBy = c.ClaimedBy,
			CompletedUtc = c.CompletedUtc,
			Succeeded = c.Succeeded,
			Outcome = c.Outcome,
		};

		private static DaemonAppEventData MapEvent(DaemonAppEventEntity e) => new DaemonAppEventData
		{
			ID = e.ID,
			HostName = e.HostName,
			AppName = e.AppName,
			PreviousStatus = e.PreviousStatus,
			Status = e.Status,
			ProcessID = e.ProcessID,
			PreviousProcessID = e.PreviousProcessID,
			RestartAttempts = e.RestartAttempts,
			PreviousRestartAttempts = e.PreviousRestartAttempts,
			ObservedUtc = e.ObservedUtc,
		};
	}
}
