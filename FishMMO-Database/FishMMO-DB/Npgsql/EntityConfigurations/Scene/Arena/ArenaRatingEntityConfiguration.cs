using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="ArenaRatingEntity"/>.</summary>
	public class ArenaRatingEntityConfiguration : IEntityTypeConfiguration<ArenaRatingEntity>
	{
		public void Configure(EntityTypeBuilder<ArenaRatingEntity> builder)
		{
			builder.ToTable("arena_rating");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();
			builder.Property(e => e.SeasonID).IsRequired();
			builder.Property(e => e.CharacterID).IsRequired();
			builder.Property(e => e.Rating).IsRequired().HasDefaultValue(1500);
			builder.Property(e => e.PeakRating).IsRequired().HasDefaultValue(1500);
			builder.Property(e => e.Games).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.Wins).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.Losses).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.LastUpdated).IsRequired().HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");
			builder.HasIndex(e => new { e.SeasonID, e.CharacterID }).IsUnique();
			// Leaderboard: top ratings within a season.
			builder.HasIndex(e => new { e.SeasonID, e.Rating });
			builder.HasOne<ArenaSeasonEntity>().WithMany().HasForeignKey(e => e.SeasonID).OnDelete(DeleteBehavior.Cascade);
			builder.HasOne<CharacterEntity>().WithMany().HasForeignKey(e => e.CharacterID).OnDelete(DeleteBehavior.Cascade);
		}
	}
}
