using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for AccountEntity with explicit indexes and constraints.
	/// </summary>
	public class AccountEntityConfiguration : IEntityTypeConfiguration<AccountEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<AccountEntity> builder)
		{
			builder.ToTable("accounts");

			// Primary Key
			builder.HasKey(e => e.Name);

			// Required fields
			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(50);

			// Case-insensitive lookup column. PostgreSQL stores LOWER(name) and a UNIQUE index
			// on this column prevents two accounts that differ only in case.
			builder.Property(e => e.NameLowercase)
				.HasColumnName("name_lowercase")
				.HasComputedColumnSql("LOWER(name)", stored: true);

			// xmin concurrency token — PostgreSQL system column updated on every row change.
			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			builder.Property(e => e.Salt)
				.IsRequired()
				.HasMaxLength(256);

			builder.Property(e => e.Verifier)
				.IsRequired()
				.HasMaxLength(512);

			builder.Property(e => e.AccessLevel)
				.IsRequired();

			builder.Property(e => e.Email)
				.HasMaxLength(320);

			builder.Property(e => e.Age)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.TotpEnabled)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.TotpSecret)
				.HasMaxLength(256);

			builder.Property(e => e.TotpVerifiedAt);

			builder.Property(e => e.LastTotpWindow)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.DiscordLinkCode)
				.HasMaxLength(64);

			builder.Property(e => e.Verified)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.VerifyCode)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.LastLogin)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Indexes
			builder.HasIndex(e => e.AccessLevel);

			builder.HasIndex(e => e.TimeCreated);

			// Case-insensitive uniqueness for account names via the computed name_lowercase column.
			builder.HasIndex(e => e.NameLowercase)
				.IsUnique();

			// Unique index on email when provided
			builder.HasIndex(e => e.Email)
				.IsUnique()
				.HasFilter("email IS NOT NULL");

			// Index on discord link code for verification lookups
			builder.HasIndex(e => e.DiscordLinkCode)
				.HasFilter("discord_link_code IS NOT NULL");
		}
	}
}