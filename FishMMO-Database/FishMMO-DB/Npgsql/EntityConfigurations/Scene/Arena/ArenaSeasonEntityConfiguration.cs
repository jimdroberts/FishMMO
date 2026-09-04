using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="ArenaSeasonEntity"/>.</summary>
	public class ArenaSeasonEntityConfiguration : IEntityTypeConfiguration<ArenaSeasonEntity>
	{
		public void Configure(EntityTypeBuilder<ArenaSeasonEntity> builder)
		{
			builder.ToTable("arena_season");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();
			builder.Property(e => e.Name).IsRequired().HasMaxLength(64);
			builder.Property(e => e.StartsUtc).IsRequired();
			builder.Property(e => e.EndsUtc).IsRequired(false);
			builder.Property(e => e.Active).IsRequired().HasDefaultValue(false);
			// At most one active season; the partial unique index is what enforces it.
			builder.HasIndex(e => e.Active).IsUnique().HasFilter("active = TRUE");
		}
	}
}
