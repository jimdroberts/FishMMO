using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="SmsQueueEntity"/>. Mirrors <c>email_queue</c>.
	/// </summary>
	public class SmsQueueEntityConfiguration : IEntityTypeConfiguration<SmsQueueEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<SmsQueueEntity> builder)
		{
			builder.ToTable("sms_queue");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* Version is configured by NpgsqlDbContext.ApplyLogicalVersionConventions, as it is
			 * for email_queue: bigint NOT NULL DEFAULT 1, concurrency token. */

			// E.164: "+" and up to 15 digits.
			builder.Property(e => e.RecipientPhone)
				.IsRequired()
				.HasMaxLength(16);

			builder.Property(e => e.RecipientUsername)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.Body)
				.IsRequired()
				.HasMaxLength(480);

			/* Stored as the enum's integer, NOT NULL DEFAULT 0, so an INSERT that does not name the
			 * column means "verification" — the same contract as email_queue.kind. */
			builder.Property(e => e.Kind)
				.IsRequired()
				.HasConversion<int>()
				.HasDefaultValue(SmsKind.Verification);

			builder.Property(e => e.CreatedAt)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.Property(e => e.SentAt);

			builder.Property(e => e.Attempts)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.LastError);

			builder.Property(e => e.ClaimedBy)
				.HasMaxLength(100);

			builder.Property(e => e.ClaimedAt);

			// Index for the background sender: fetch oldest unsent messages first
			builder.HasIndex(e => new { e.SentAt, e.CreatedAt });

			// Index for looking up by recipient
			builder.HasIndex(e => e.RecipientUsername);

			// Index for finding stale claims (claimed but never sent)
			builder.HasIndex(e => new { e.ClaimedAt, e.SentAt });

			/* One row per request. A write retried after its reply was lost (the connection dropped
			 * after the commit) carries the same key and lands on the row its first attempt made,
			 * instead of writing a second (issue #267). Filtered, so rows written without a key
			 * never collide. */
			builder.HasIndex(e => e.RequestKey)
				.IsUnique()
				.HasFilter("request_key IS NOT NULL");
		}
	}
}
