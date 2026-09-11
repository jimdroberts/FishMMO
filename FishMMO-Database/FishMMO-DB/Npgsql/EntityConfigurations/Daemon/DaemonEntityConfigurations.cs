using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="DaemonHostEntity"/>.</summary>
	public class DaemonHostEntityConfiguration : IEntityTypeConfiguration<DaemonHostEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DaemonHostEntity> builder)
		{
			builder.ToTable("daemon_hosts");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.HostName).IsRequired().HasMaxLength(128);

			// The identity a command is addressed to, so it must be unique or two daemons will
			// claim each other's work.
			builder.HasIndex(e => e.HostName).IsUnique().HasDatabaseName("ix_daemon_hosts_name");

			builder.Property(e => e.DaemonVersion).IsRequired(false).HasMaxLength(64);
			builder.Property(e => e.OSDescription).IsRequired(false).HasMaxLength(256);
			builder.Property(e => e.ProcessorCount).IsRequired();
			builder.Property(e => e.StartedUtc).IsRequired();
			builder.Property(e => e.LastHeartbeatUtc).IsRequired();

			builder.HasMany(e => e.Apps)
				.WithOne(a => a.Host)
				.HasForeignKey(a => a.HostID)
				// An application row means nothing without the host that reported it.
				.OnDelete(DeleteBehavior.Cascade);
		}
	}

	/// <summary>Entity configuration for <see cref="DaemonAppEntity"/>.</summary>
	public class DaemonAppEntityConfiguration : IEntityTypeConfiguration<DaemonAppEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DaemonAppEntity> builder)
		{
			builder.ToTable("daemon_apps");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.HostID).IsRequired();
			builder.Property(e => e.Name).IsRequired().HasMaxLength(128);
			builder.Property(e => e.Status).IsRequired();
			builder.Property(e => e.ProcessID).IsRequired(false);
			builder.Property(e => e.RestartAttempts).IsRequired();
			builder.Property(e => e.MaxRestartAttempts).IsRequired();
			builder.Property(e => e.MonitoredPort).IsRequired();
			builder.Property(e => e.LastReportedUtc).IsRequired();

			// One row per application per host; the daemon upserts against this.
			builder.HasIndex(e => new { e.HostID, e.Name })
				.IsUnique()
				.HasDatabaseName("ix_daemon_apps_host_name");

			/* No executable path, arguments or working directory, deliberately. See the
			 * entity's remarks: a row that carried them would let the database decide what runs
			 * on a game host. */
		}
	}

	/// <summary>Entity configuration for <see cref="DaemonCommandEntity"/>.</summary>
	public class DaemonCommandEntityConfiguration : IEntityTypeConfiguration<DaemonCommandEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DaemonCommandEntity> builder)
		{
			builder.ToTable("daemon_commands");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.HostName).IsRequired().HasMaxLength(128);
			builder.Property(e => e.AppName).IsRequired().HasMaxLength(128);
			builder.Property(e => e.Verb).IsRequired();
			builder.Property(e => e.RequestedBy).IsRequired().HasMaxLength(100);
			builder.Property(e => e.RequestedUtc).IsRequired();
			builder.Property(e => e.Reason).IsRequired().HasMaxLength(1024);
			builder.Property(e => e.ExpiresUtc).IsRequired();
			builder.Property(e => e.ClaimedUtc).IsRequired(false);
			builder.Property(e => e.ClaimedBy).IsRequired(false).HasMaxLength(128);
			builder.Property(e => e.CompletedUtc).IsRequired(false);
			builder.Property(e => e.Succeeded).IsRequired(false);
			builder.Property(e => e.Outcome).IsRequired(false).HasMaxLength(1024);

			/* The daemon's poll: my host, not yet claimed, not yet expired, oldest first. A
			 * partial index on exactly that predicate, so the poll stays cheap as completed
			 * commands accumulate — the queue is also the history and is not pruned. */
			builder.HasIndex(e => new { e.HostName, e.ClaimedUtc })
				.HasDatabaseName("ix_daemon_commands_pending");

			// The panel's read: what has been asked of this host lately.
			builder.HasIndex(e => new { e.HostName, e.RequestedUtc })
				.HasDatabaseName("ix_daemon_commands_history");

			/* No foreign key from RequestedBy to accounts, for the audit log's reason: the
			 * record of who asked for a restart must outlive the account that asked. */
		}
	}

	/// <summary>Entity configuration for <see cref="DaemonAppEventEntity"/>.</summary>
	public class DaemonAppEventEntityConfiguration : IEntityTypeConfiguration<DaemonAppEventEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DaemonAppEventEntity> builder)
		{
			builder.ToTable("daemon_app_events");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			/* No xmin concurrency token, matching admin_audit_log and unlike the rest of this
			 * file. Rows are written once and never updated, so there is no competing writer for
			 * a token to mediate, and its absence states the append-only intent in the schema
			 * rather than only in a comment. */

			/* Copied as text, with no foreign key to daemon_hosts or daemon_apps. Those rows are
			 * mutable current state: an application dropped from a daemon's configuration is
			 * deleted on the next heartbeat, and a decommissioned host takes its applications
			 * with it. A key here would make the record of what a machine did disappear at
			 * exactly the moment somebody removed the machine for doing it. */
			builder.Property(e => e.HostName).IsRequired().HasMaxLength(128);
			builder.Property(e => e.AppName).IsRequired().HasMaxLength(128);

			// Null on a first sighting: there was no row to compare against.
			builder.Property(e => e.PreviousStatus).IsRequired(false);
			builder.Property(e => e.Status).IsRequired();
			builder.Property(e => e.ProcessID).IsRequired(false);
			builder.Property(e => e.PreviousProcessID).IsRequired(false);
			builder.Property(e => e.RestartAttempts).IsRequired();
			builder.Property(e => e.PreviousRestartAttempts).IsRequired(false);
			builder.Property(e => e.ObservedUtc).IsRequired();

			/* The question this table exists to answer: what happened to that application, on
			 * that host, newest first. A restart loop is a burst of rows inside a few minutes for
			 * a single host and name, so the index that answers the question is also the one that
			 * makes the burst cheap to pull back out. */
			builder.HasIndex(e => new { e.HostName, e.AppName, e.ObservedUtc })
				.HasDatabaseName("ix_daemon_app_events_app");

			// The unfiltered page: everything every supervisor saw, newest first.
			builder.HasIndex(e => new { e.ObservedUtc, e.ID })
				.HasDatabaseName("ix_daemon_app_events_observed");

			/* No free-text column, and that is the invariant this table is built around. Every
			 * value is an enum member or an integer derived server-side by comparing one
			 * heartbeat against the stored row; the daemon supplies none of it. A message or
			 * detail column would let a compromised supervisor put text of its choosing on an
			 * operator's screen, which is the reason shipping process output was rejected. */
		}
	}
}
