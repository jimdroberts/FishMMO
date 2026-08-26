using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CurrencyEscrowEntityConfiguration : IEntityTypeConfiguration<CurrencyEscrowEntity>
	{
		public void Configure(EntityTypeBuilder<CurrencyEscrowEntity> builder)
		{
			builder.ToTable("currency_escrow");

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

			/* Reconciliation reads every row still Held, across all characters, at startup. A
			 * filtered index keeps that scan proportional to the number of interrupted
			 * transactions rather than to the size of the ledger, which only ever grows —
			 * settled rows are kept as economy history. */
			builder.HasIndex(e => e.State)
				.HasFilter("state = 0")
				.HasDatabaseName("ix_currency_escrow_held");

			/* Per-character lookup, for answering what a character currently has held and for
			 * returning holds when their transactions are settled. */
			builder.HasIndex(e => new { e.CharacterID, e.State })
				.HasDatabaseName("ix_currency_escrow_character_state");
		}
	}
}
