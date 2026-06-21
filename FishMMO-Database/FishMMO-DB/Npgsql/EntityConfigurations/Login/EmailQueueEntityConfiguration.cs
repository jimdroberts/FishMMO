using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for EmailQueueEntity.
	/// </summary>
	public class EmailQueueEntityConfiguration : IEntityTypeConfiguration<EmailQueueEntity>
	{
		public void Configure(EntityTypeBuilder<EmailQueueEntity> builder)
		{
			builder.ToTable("email_queue");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.RecipientEmail)
				.IsRequired()
				.HasMaxLength(320);

			builder.Property(e => e.RecipientUsername)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.Subject)
				.IsRequired()
				.HasMaxLength(256);

			builder.Property(e => e.Body)
				.IsRequired();

			builder.Property(e => e.CreatedAt)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.SentAt);

			builder.Property(e => e.Attempts)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.LastError);

			builder.Property(e => e.ClaimedBy)
				.HasMaxLength(100);

			builder.Property(e => e.ClaimedAt);

			// Index for the background processor: fetch oldest unsent emails first
			builder.HasIndex(e => new { e.SentAt, e.CreatedAt });

			// Index for looking up by recipient
			builder.HasIndex(e => e.RecipientUsername);

			// Index for finding stale claims (claimed but never sent)
			builder.HasIndex(e => new { e.ClaimedAt, e.SentAt });
		}
	}
}