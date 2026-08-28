using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CurrencyLedgerEntityConfiguration : IEntityTypeConfiguration<CurrencyLedgerEntity>
	{
		public void Configure(EntityTypeBuilder<CurrencyLedgerEntity> builder)
		{
			builder.ToTable("currency_ledger");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Amount)
				.IsRequired();

			builder.Property(e => e.Reason)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.State)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* Per-character history, newest first — "what did this character spend, and when".
			 * The support case: a player disputes a balance and someone has to reconstruct it. */
			builder.HasIndex(e => new { e.CharacterID, e.TimeCreated })
				.HasDatabaseName("ix_currency_ledger_character_time");

			/* Per-sink aggregation over a window — "which sinks drained currency last week, and
			 * at what rate". This is the question the table exists to answer, and without the
			 * index it is a full scan of a table that only ever grows. */
			builder.HasIndex(e => new { e.Reason, e.TimeCreated })
				.HasDatabaseName("ix_currency_ledger_reason_time");
		}
	}
}
