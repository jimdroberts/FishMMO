using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="SupportTicketMessageEntity"/>.
	/// </summary>
	public class SupportTicketMessageEntityConfiguration : IEntityTypeConfiguration<SupportTicketMessageEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<SupportTicketMessageEntity> builder)
		{
			builder.ToTable("support_ticket_messages");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* No concurrency token: a message is written once and never edited. Editing a reply
			 * after the other party has read it is not a feature a support system should have. */

			builder.Property(e => e.TicketID).IsRequired();

			builder.Property(e => e.CreatedUtc).IsRequired();

			builder.Property(e => e.AuthorAccount)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.AuthorIsStaff)
				.IsRequired()
				.HasDefaultValue(false);

			/* Defaults to false, so a message is visible to the player unless it was explicitly
			 * marked otherwise. The dangerous default is the other way round: a bug that loses
			 * the flag would hide staff replies rather than leak internal notes. */
			builder.Property(e => e.Internal)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.Body)
				.IsRequired()
				.HasMaxLength(4000);

			// The conversation, in order, for one ticket.
			builder.HasIndex(e => new { e.TicketID, e.CreatedUtc })
				.HasDatabaseName("ix_support_ticket_messages_ticket");
		}
	}
}
