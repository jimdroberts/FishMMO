using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="MaintenanceOperationEntity"/>.</summary>
	public class MaintenanceOperationEntityConfiguration : IEntityTypeConfiguration<MaintenanceOperationEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<MaintenanceOperationEntity> builder)
		{
			builder.ToTable("maintenance_operations");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.Name).IsRequired().HasMaxLength(128);
			builder.Property(e => e.StartedBy).IsRequired().HasMaxLength(100);
			builder.Property(e => e.Reason).IsRequired().HasMaxLength(1024);
			builder.Property(e => e.Status).IsRequired();
			builder.Property(e => e.DrainSeconds).IsRequired();
			builder.Property(e => e.StartedUtc).IsRequired();
			builder.Property(e => e.DeadlineUtc).IsRequired();
			builder.Property(e => e.CompletedUtc).IsRequired(false);
			builder.Property(e => e.CancelledBy).IsRequired(false).HasMaxLength(100);
			builder.Property(e => e.CancelledUtc).IsRequired(false);
			builder.Property(e => e.CancelReason).IsRequired(false).HasMaxLength(1024);
			builder.Property(e => e.Outcome).IsRequired(false).HasMaxLength(1024);

			/* The advance pass's read: windows that are not finished, oldest deadline first. A
			 * shard has a handful of these at a time, but the pass runs on every listing, so it
			 * is worth an index rather than a sequential scan over the whole history — this
			 * table is never pruned. */
			builder.HasIndex(e => new { e.Status, e.DeadlineUtc })
				.HasDatabaseName("ix_maintenance_operations_active");

			// The panel's listing: newest first.
			builder.HasIndex(e => e.StartedUtc)
				.HasDatabaseName("ix_maintenance_operations_started");

			builder.HasMany(e => e.Targets)
				.WithOne(t => t.Operation)
				.HasForeignKey(t => t.OperationID)
				// A target means nothing without the window that planned it.
				.OnDelete(DeleteBehavior.Cascade);

			/* No foreign key from StartedBy or CancelledBy to accounts, for the audit log's
			 * reason: the record of who took a shard down must outlive the account. */
		}
	}

	/// <summary>Entity configuration for <see cref="MaintenanceTargetEntity"/>.</summary>
	public class MaintenanceTargetEntityConfiguration : IEntityTypeConfiguration<MaintenanceTargetEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<MaintenanceTargetEntity> builder)
		{
			builder.ToTable("maintenance_targets");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.OperationID).IsRequired();
			builder.Property(e => e.Kind).IsRequired().HasMaxLength(16);
			builder.Property(e => e.ServerID).IsRequired();
			// Matches world_servers.name and scene_servers.name, which are both 100.
			builder.Property(e => e.ServerName).IsRequired().HasMaxLength(100);
			builder.Property(e => e.Status).IsRequired();
			builder.Property(e => e.ScheduledShutdownUtc).IsRequired(false);
			builder.Property(e => e.LockWrittenUtc).IsRequired(false);
			builder.Property(e => e.ShutdownWrittenUtc).IsRequired(false);
			builder.Property(e => e.ShutdownClearedUtc).IsRequired(false);
			builder.Property(e => e.PulsingAtStart).IsRequired();
			builder.Property(e => e.ObservedUtc).IsRequired(false);
			builder.Property(e => e.ObservedCharacterCount).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.ObservedLocked).IsRequired().HasDefaultValue(false);
			builder.Property(e => e.ObservedShutdownUtc).IsRequired(false);
			builder.Property(e => e.ObservedLastPulseUtc).IsRequired(false);
			builder.Property(e => e.ObservedRegistered).IsRequired().HasDefaultValue(false);
			builder.Property(e => e.Note).IsRequired(false).HasMaxLength(1024);

			// One row per server per window; the same server twice in one window is a mistake.
			builder.HasIndex(e => new { e.OperationID, e.Kind, e.ServerID })
				.IsUnique()
				.HasDatabaseName("ix_maintenance_targets_unique");

			/* "Is this server already inside a live window": asked before every new window is
			 * accepted, so two overlapping plans cannot fight over one deadline column. */
			builder.HasIndex(e => new { e.Kind, e.ServerID, e.Status })
				.HasDatabaseName("ix_maintenance_targets_server");

			/* No foreign key to world_servers or scene_servers. A world server DELETES its
			 * registration as it exits, and that deletion is how this row knows the shutdown
			 * worked — a foreign key would either stop the server exiting or cascade away the
			 * evidence that it did. */
		}
	}
}
