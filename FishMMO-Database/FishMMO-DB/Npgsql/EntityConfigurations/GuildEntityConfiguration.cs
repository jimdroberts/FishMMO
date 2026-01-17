using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for GuildEntity with explicit indexes and constraints.
	/// </summary>
	public class GuildEntityConfiguration : IEntityTypeConfiguration<GuildEntity>
	{
		public void Configure(EntityTypeBuilder<GuildEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.Notice)
				.HasMaxLength(500);

			// Unique constraint on guild name (case-insensitive)
			builder.HasIndex(e => e.Name)
				.IsUnique()
				.HasDatabaseName("IX_Guild_Name_Unique");

			// Performance index for guild lookups
			builder.HasIndex(e => e.Name)
				.HasDatabaseName("IX_Guild_Name");
		}
	}
}