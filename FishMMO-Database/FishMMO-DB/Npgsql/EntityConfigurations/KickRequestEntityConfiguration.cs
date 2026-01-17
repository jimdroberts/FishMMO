using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for KickRequestEntity with explicit indexes and constraints.
	/// </summary>
	public class KickRequestEntityConfiguration : IEntityTypeConfiguration<KickRequestEntity>
	{
		public void Configure(EntityTypeBuilder<KickRequestEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Performance index for kick request lookups
			builder.HasIndex(e => e.AccountName)
				.HasDatabaseName("IX_KickRequest_AccountName");

			// Index for creation time (sorting/filtering old requests)
			builder.HasIndex(e => e.TimeCreated)
				.HasDatabaseName("IX_KickRequest_TimeCreated");
		}
	}
}
