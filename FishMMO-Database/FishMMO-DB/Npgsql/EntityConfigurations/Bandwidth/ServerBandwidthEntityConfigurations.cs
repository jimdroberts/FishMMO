using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="ServerBandwidthMinuteEntity"/>.</summary>
	public class ServerBandwidthMinuteEntityConfiguration : IEntityTypeConfiguration<ServerBandwidthMinuteEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<ServerBandwidthMinuteEntity> builder)
		{
			builder.ToTable("server_bandwidth_minute");

			/* The key is the upsert's conflict target, so it is load-bearing:
			 * ServerBandwidthService.RecordAsync names exactly these four columns in its
			 * ON CONFLICT, and changing them here breaks that statement at runtime.
			 *
			 * Column order serves the reads as well as the write. (kind, name, bucket) first makes
			 * the key the index for one server's history in time order — the page's per-server
			 * chart — and the instance id last keeps two processes of one server apart without
			 * splitting that range. */
			builder.HasKey(e => new { e.ServerKind, e.ServerName, e.BucketStart, e.InstanceID });

			/* No xmin concurrency token: each row has exactly one writer, the process named by its
			 * instance id, and that writer is single-flight. Its absence states the intent in the
			 * schema, as on admin_audit_log and daemon_app_events. */

			builder.Property(e => e.BucketStart).IsRequired();
			builder.Property(e => e.ServerKind).IsRequired();

			/* Copied as text with no foreign key to login_servers, world_servers or scene_servers:
			 * those rows are deleted when a server deregisters, and its history must outlive it.
			 * 100 matches the name column on all three. */
			builder.Property(e => e.ServerName).IsRequired().HasMaxLength(100);
			builder.Property(e => e.InstanceID).IsRequired();

			// Every counter is a measured bigint; the wire estimate is computed when read.
			builder.Property(e => e.IntervalMs).IsRequired();
			builder.Property(e => e.AppSentBytes).IsRequired();
			builder.Property(e => e.AppRecvBytes).IsRequired();
			builder.Property(e => e.UdpSentBytes).IsRequired();
			builder.Property(e => e.UdpRecvBytes).IsRequired();
			builder.Property(e => e.UdpSentDatagrams).IsRequired();
			builder.Property(e => e.UdpRecvDatagrams).IsRequired();
			builder.Property(e => e.SessionsActive).IsRequired();
			builder.Property(e => e.SessionsOpened).IsRequired();
			builder.Property(e => e.ConnectionsRefused).IsRequired();
			builder.Property(e => e.HandshakeFailures).IsRequired();

			/* Every other read is a time range over all servers: the page's last hour and last
			 * 24 hours, its chart, the rollup's window, and the retention job's "oldest hour"
			 * probe and whole-hour delete. */
			builder.HasIndex(e => e.BucketStart)
				.HasDatabaseName("ix_server_bandwidth_minute_bucket");
		}
	}

	/// <summary>Entity configuration for <see cref="ServerBandwidthHourEntity"/>.</summary>
	public class ServerBandwidthHourEntityConfiguration : IEntityTypeConfiguration<ServerBandwidthHourEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<ServerBandwidthHourEntity> builder)
		{
			builder.ToTable("server_bandwidth_hour");

			/* The rollup's conflict target (ServerBandwidthReportService), and the index for one
			 * server's history in time order. No instance id: an hour is summed over instances. */
			builder.HasKey(e => new { e.ServerKind, e.ServerName, e.BucketStart });

			builder.Property(e => e.BucketStart).IsRequired();
			builder.Property(e => e.ServerKind).IsRequired();
			builder.Property(e => e.ServerName).IsRequired().HasMaxLength(100);

			builder.Property(e => e.IntervalMs).IsRequired();
			builder.Property(e => e.AppSentBytes).IsRequired();
			builder.Property(e => e.AppRecvBytes).IsRequired();
			builder.Property(e => e.UdpSentBytes).IsRequired();
			builder.Property(e => e.UdpRecvBytes).IsRequired();
			builder.Property(e => e.UdpSentDatagrams).IsRequired();
			builder.Property(e => e.UdpRecvDatagrams).IsRequired();
			builder.Property(e => e.SessionsActiveMax).IsRequired();
			builder.Property(e => e.SessionsActiveSum).IsRequired();
			builder.Property(e => e.SessionsOpened).IsRequired();
			builder.Property(e => e.ConnectionsRefused).IsRequired();
			builder.Property(e => e.HandshakeFailures).IsRequired();
			builder.Property(e => e.MinutesSampled).IsRequired();
			builder.Property(e => e.Instances).IsRequired();

			// The 30-day window and chart over all servers, and the 13-month retention delete.
			builder.HasIndex(e => e.BucketStart)
				.HasDatabaseName("ix_server_bandwidth_hour_bucket");
		}
	}
}
