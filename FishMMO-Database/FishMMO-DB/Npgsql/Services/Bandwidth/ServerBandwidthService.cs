using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Writes a server process's own minute rows to <c>server_bandwidth_minute</c>.
	/// </summary>
	/// <remarks>See <see cref="IServerBandwidthService"/> for why the key arrives finished.</remarks>
	public sealed class ServerBandwidthService : BaseService<ServerBandwidthMinuteEntity>, IServerBandwidthService
	{
		/// <summary>
		/// The most rows one write takes: every minute a process may hold for retry, plus the one
		/// it has just sampled. A caller holding more has a bug, not a backlog.
		/// </summary>
		public const int MaxSamplesPerWrite = ServerBandwidthMath.MaxPendingMinutes + 1;

		/// <summary>The longest server name the column takes, matching the three server tables.</summary>
		public const int MaxServerNameLength = 100;

		public ServerBandwidthService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime>> FetchDatabaseUtcNowAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(
				dbContext => ReadDatabaseUtcNowAsync(dbContext, cancellationToken),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> RecordAsync(
			int serverKind,
			string serverName,
			Guid instanceId,
			IReadOnlyList<ServerBandwidthSample> samples,
			CancellationToken cancellationToken = default)
		{
			string? invalid = Validate(serverKind, serverName, instanceId, samples);
			if (invalid != null)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, invalid);
			}

			int count = samples.Count;
			var buckets = new DateTime[count];
			var intervals = new long[count];
			var appSent = new long[count];
			var appRecv = new long[count];
			var udpSent = new long[count];
			var udpRecv = new long[count];
			var datagramsSent = new long[count];
			var datagramsRecv = new long[count];
			var sessionsActive = new long[count];
			var sessionsOpened = new long[count];
			var refused = new long[count];
			var handshakeFailures = new long[count];
			for (int i = 0; i < count; i++)
			{
				ServerBandwidthSample s = samples[i];
				// Unspecified, as every timestamp column here holds UTC without a zone (Npgsql 5).
				buckets[i] = DateTime.SpecifyKind(s.BucketStartUtc, DateTimeKind.Unspecified);
				intervals[i] = s.IntervalMs;
				appSent[i] = s.AppSentBytes;
				appRecv[i] = s.AppRecvBytes;
				udpSent[i] = s.UdpSentBytes;
				udpRecv[i] = s.UdpRecvBytes;
				datagramsSent[i] = s.UdpSentDatagrams;
				datagramsRecv[i] = s.UdpRecvDatagrams;
				sessionsActive[i] = s.SessionsActive;
				sessionsOpened[i] = s.SessionsOpened;
				refused[i] = s.ConnectionsRefused;
				handshakeFailures[i] = s.HandshakeFailures;
			}

			/* Built once, outside the retried delegate, like the key it carries: a retry resends
			 * exactly these values. */
			object[] parameters =
			{
				serverKind, serverName, instanceId,
				buckets, intervals, appSent, appRecv, udpSent, udpRecv, datagramsSent, datagramsRecv,
				sessionsActive, sessionsOpened, refused, handshakeFailures,
			};

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* SET, never ADD. A retry after a lost commit reply rewrites identical values, and a
				 * second write for the same minute carries that minute's running total, which
				 * contains the first. Adding would count both.
				 *
				 * The WHERE keeps the running total from ever moving backwards: one minute's
				 * interval only grows as its samples accumulate, so a row with a shorter interval
				 * than the stored one is an older write arriving after a newer one. The recorder is
				 * single-flight and should never produce that; the guard makes it harmless if a
				 * future caller does. The alias is required: an unqualified interval_ms inside
				 * DO UPDATE is ambiguous with EXCLUDED's (42702). */
				string sql = $@"INSERT INTO {TableName} AS t
						(server_kind, server_name, instance_id, bucket_start, interval_ms,
						 app_sent_bytes, app_recv_bytes, udp_sent_bytes, udp_recv_bytes,
						 udp_sent_datagrams, udp_recv_datagrams,
						 sessions_active, sessions_opened, connections_refused, handshake_failures)
					SELECT {{0}}, {{1}}, {{2}}, u.bucket_start, u.interval_ms,
						u.app_sent_bytes, u.app_recv_bytes, u.udp_sent_bytes, u.udp_recv_bytes,
						u.udp_sent_datagrams, u.udp_recv_datagrams,
						u.sessions_active, u.sessions_opened, u.connections_refused, u.handshake_failures
					FROM UNNEST(
						{{3}}::timestamp[], {{4}}::bigint[],
						{{5}}::bigint[], {{6}}::bigint[], {{7}}::bigint[], {{8}}::bigint[],
						{{9}}::bigint[], {{10}}::bigint[],
						{{11}}::bigint[], {{12}}::bigint[], {{13}}::bigint[], {{14}}::bigint[])
						AS u(bucket_start, interval_ms,
							app_sent_bytes, app_recv_bytes, udp_sent_bytes, udp_recv_bytes,
							udp_sent_datagrams, udp_recv_datagrams,
							sessions_active, sessions_opened, connections_refused, handshake_failures)
					ON CONFLICT (server_kind, server_name, bucket_start, instance_id)
					DO UPDATE SET
						interval_ms = EXCLUDED.interval_ms,
						app_sent_bytes = EXCLUDED.app_sent_bytes,
						app_recv_bytes = EXCLUDED.app_recv_bytes,
						udp_sent_bytes = EXCLUDED.udp_sent_bytes,
						udp_recv_bytes = EXCLUDED.udp_recv_bytes,
						udp_sent_datagrams = EXCLUDED.udp_sent_datagrams,
						udp_recv_datagrams = EXCLUDED.udp_recv_datagrams,
						sessions_active = EXCLUDED.sessions_active,
						sessions_opened = EXCLUDED.sessions_opened,
						connections_refused = EXCLUDED.connections_refused,
						handshake_failures = EXCLUDED.handshake_failures
					WHERE EXCLUDED.interval_ms >= t.interval_ms";

				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, parameters, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Why a write cannot be attempted, or null. Pure, so the rules are testable.</summary>
		public static string? Validate(int serverKind, string serverName, Guid instanceId, IReadOnlyList<ServerBandwidthSample> samples)
		{
			if (!ServerBandwidthKind.IsValid(serverKind))
			{
				return $"{serverKind} is not a server tier.";
			}
			if (string.IsNullOrWhiteSpace(serverName) || serverName.Length > MaxServerNameLength)
			{
				return $"A server name of 1 to {MaxServerNameLength} characters is required.";
			}
			if (instanceId == Guid.Empty)
			{
				return "An instance id is required.";
			}
			if (samples == null || samples.Count == 0)
			{
				return "There is nothing to record.";
			}
			if (samples.Count > MaxSamplesPerWrite)
			{
				return $"At most {MaxSamplesPerWrite} minutes may be written at once.";
			}

			/* One row per minute. Two rows for one key in a single INSERT … ON CONFLICT is an error
			 * in PostgreSQL ("cannot affect row a second time"), and the refusal here names the
			 * cause instead. */
			var seen = new HashSet<DateTime>();
			for (int i = 0; i < samples.Count; i++)
			{
				string? why = samples[i].Invalid();
				if (why != null)
				{
					return $"The minute at {samples[i].BucketStartUtc:u} cannot be recorded: {why}.";
				}
				if (!seen.Add(samples[i].BucketStartUtc))
				{
					return $"The minute at {samples[i].BucketStartUtc:u} appears twice.";
				}
			}
			return null;
		}
	}
}
