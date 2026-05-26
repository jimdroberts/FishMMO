using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for TwoFactorRecoveryCodeEntity with indexes and constraints.
	/// </summary>
	public class TwoFactorRecoveryCodeEntityConfiguration : IEntityTypeConfiguration<TwoFactorRecoveryCodeEntity>
	{
		public void Configure(EntityTypeBuilder<TwoFactorRecoveryCodeEntity> builder)
		{
			builder.ToTable("two_factor_recovery_codes");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// AccountName — FK to accounts (string PK)
			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(100);

			builder.HasIndex(e => e.AccountName);

			// CodeHash — hashed recovery code, required
			builder.Property(e => e.CodeHash)
				.IsRequired()
				.HasMaxLength(128);

			// UsedAt — null until consumed
			builder.Property(e => e.UsedAt);

			// Filtered UNIQUE index: unused codes per account for fast lookup. Uniqueness
			// is defence-in-depth so an accidental duplicate insert of the same hash for the
			// same account is rejected at the DB layer — the application layer must already
			// dedupe but a slip cannot create two redeemable copies of one code.
			builder.HasIndex(e => new { e.AccountName, e.CodeHash })
				.IsUnique()
				.HasFilter("used_at IS NULL");

			// Foreign key to accounts
			builder.HasOne(e => e.Account)
				.WithMany()
				.HasForeignKey(e => e.AccountName)
				.HasPrincipalKey(a => a.Name)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}