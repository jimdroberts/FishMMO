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
			builder.ToTable("guilds");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.Name)
				.IsRequired();

			builder.Property(e => e.NameLowercase)
				.HasComputedColumnSql("LOWER(\"name\")", stored: true);

			builder.Property(e => e.Notice)
				.HasMaxLength(500);

			// Case-insensitive uniqueness via normalized computed column.
			builder.HasIndex(e => e.NameLowercase)
				.IsUnique();

		}
	}
}