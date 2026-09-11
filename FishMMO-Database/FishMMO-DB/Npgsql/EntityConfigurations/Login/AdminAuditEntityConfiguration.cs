using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="AdminAuditEntity"/>.
	/// </summary>
	public class AdminAuditEntityConfiguration : IEntityTypeConfiguration<AdminAuditEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<AdminAuditEntity> builder)
		{
			builder.ToTable("admin_audit_log");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* No xmin concurrency token, unlike every other entity here. Rows are written once
			 * and never updated, so there is no competing writer for a token to mediate, and
			 * leaving it out states the append-only intent in the schema rather than only in a
			 * comment. */

			builder.Property(e => e.OccurredUtc)
				.IsRequired();

			builder.Property(e => e.ActorName)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.ActorAccessLevel)
				.IsRequired();

			builder.Property(e => e.ActorSessionID);

			builder.Property(e => e.Action)
				.IsRequired()
				.HasMaxLength(64);

			/* Explicitly optional. The project has nullable reference types enabled, which makes
			 * EF infer NOT NULL for every non-nullable string — and these genuinely are absent
			 * sometimes: an in-game command may carry no reason, a sign-in has no target, and
			 * most actions have no structured details. A NOT NULL column here would throw at the
			 * moment of recording, which is the worst possible moment to lose a row. */
			builder.Property(e => e.TargetType)
				.IsRequired(false)
				.HasMaxLength(32);

			builder.Property(e => e.TargetID)
				.IsRequired(false)
				.HasMaxLength(128);

			builder.Property(e => e.TargetName)
				.IsRequired(false)
				.HasMaxLength(128);

			/* Long enough that an operator explaining a difficult decision is never truncated,
			 * bounded so the column cannot be used as storage. */
			builder.Property(e => e.Reason)
				.IsRequired(false)
				.HasMaxLength(1024);

			builder.Property(e => e.Succeeded)
				.IsRequired();

			builder.Property(e => e.Outcome)
				.IsRequired(false)
				.HasMaxLength(512);

			builder.Property(e => e.Details)
				.IsRequired(false)
				.HasMaxLength(4096);

			builder.Property(e => e.IpAddress)
				.IsRequired(false)
				.HasMaxLength(64);

			builder.Property(e => e.Source)
				.IsRequired()
				.HasMaxLength(16);

			// The default view: newest first. ID breaks ties inside a timestamp.
			builder.HasIndex(e => new { e.OccurredUtc, e.ID })
				.HasDatabaseName("ix_admin_audit_log_occurred");

			// "What has this operator been doing?"
			builder.HasIndex(e => new { e.ActorName, e.OccurredUtc })
				.HasDatabaseName("ix_admin_audit_log_actor");

			// "What has been done to this character?" — the question asked in anger.
			builder.HasIndex(e => new { e.TargetType, e.TargetID, e.OccurredUtc })
				.HasDatabaseName("ix_admin_audit_log_target");

			/* No foreign key to accounts, by design. A cascade would delete the record of what
			 * an operator did when that operator's account is removed, which is the one case the
			 * log exists to survive. See the remarks on the entity. */
		}
	}
}
