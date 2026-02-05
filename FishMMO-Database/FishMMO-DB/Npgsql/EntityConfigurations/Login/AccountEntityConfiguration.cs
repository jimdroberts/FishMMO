using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for AccountEntity with explicit indexes and constraints.
	/// </summary>
	public class AccountEntityConfiguration : IEntityTypeConfiguration<AccountEntity>
	{
		public void Configure(EntityTypeBuilder<AccountEntity> builder)
		{
			builder.ToTable("accounts");

			// Primary Key
			builder.HasKey(e => e.Name);

			// Required fields
			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.Salt)
				.IsRequired()
				.HasMaxLength(256);

			builder.Property(e => e.Verifier)
				.IsRequired()
				.HasMaxLength(512);

			builder.Property(e => e.AccessLevel)
				.IsRequired();

			builder.Property(e => e.LastLogin)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Indexes
			builder.HasIndex(e => e.AccessLevel);

			builder.HasIndex(e => e.TimeCreated);
		}
	}
}