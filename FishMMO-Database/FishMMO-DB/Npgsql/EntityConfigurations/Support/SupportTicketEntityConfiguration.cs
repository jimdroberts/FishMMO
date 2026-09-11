using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="SupportTicketEntity"/>.
	/// </summary>
	public class SupportTicketEntityConfiguration : IEntityTypeConfiguration<SupportTicketEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<SupportTicketEntity> builder)
		{
			builder.ToTable("support_tickets");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* An xmin token here, unlike the audit log. A ticket has many writers over its life
			 * — two game masters can open the same one, and both can press Assign — and losing
			 * one of those writes silently is how two people work the same report without
			 * either knowing. */
			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			builder.Property(e => e.CreatedUtc).IsRequired();
			builder.Property(e => e.LastActivityUtc).IsRequired();

			builder.Property(e => e.ReporterAccount)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.ReporterCharacterName)
				.IsRequired(false)
				.HasMaxLength(64);

			builder.Property(e => e.ReporterCharacterID).IsRequired();

			builder.Property(e => e.Category).IsRequired();
			builder.Property(e => e.Status).IsRequired();
			builder.Property(e => e.Priority).IsRequired();

			builder.Property(e => e.Subject)
				.IsRequired()
				.HasMaxLength(160);

			/* Long enough for a player to explain themselves properly. A support form that
			 * truncates the description produces a second ticket saying "as I was saying". */
			builder.Property(e => e.Body)
				.IsRequired()
				.HasMaxLength(4000);

			builder.Property(e => e.TargetAccount)
				.IsRequired(false)
				.HasMaxLength(100);

			builder.Property(e => e.TargetCharacterName)
				.IsRequired(false)
				.HasMaxLength(64);

			builder.Property(e => e.TargetCharacterID).IsRequired();

			builder.Property(e => e.SceneName)
				.IsRequired(false)
				.HasMaxLength(128);

			builder.Property(e => e.AssignedTo)
				.IsRequired(false)
				.HasMaxLength(100);

			builder.Property(e => e.Resolution)
				.IsRequired(false)
				.HasMaxLength(2000);

			builder.Property(e => e.ClosedUtc).IsRequired(false);

			builder.Property(e => e.ClosedBy)
				.IsRequired(false)
				.HasMaxLength(100);

			// The queue: unfinished work, oldest activity first.
			builder.HasIndex(e => new { e.Status, e.LastActivityUtc })
				.HasDatabaseName("ix_support_tickets_status_activity");

			// "What am I working on?"
			builder.HasIndex(e => new { e.AssignedTo, e.Status })
				.HasDatabaseName("ix_support_tickets_assigned");

			// "What has this player filed?" — and the open-ticket cap that rate-limits them.
			builder.HasIndex(e => new { e.ReporterAccount, e.CreatedUtc })
				.HasDatabaseName("ix_support_tickets_reporter");

			// "Has anybody else been reported for this?"
			builder.HasIndex(e => new { e.TargetAccount, e.CreatedUtc })
				.HasDatabaseName("ix_support_tickets_target");

			/* No foreign key to accounts on any of these names, deliberately. A report must
			 * outlive the account it is about, and that account is the one most likely to be
			 * deleted. See the entity's remarks. */

			builder.HasMany(e => e.Messages)
				.WithOne(m => m.Ticket)
				.HasForeignKey(m => m.TicketID)
				// The messages ARE the ticket; there is nothing meaningful in one whose ticket
				// is gone. This is the one cascade that belongs here.
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
