using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="DuelRecordEntity"/>.</summary>
	public class DuelRecordEntityConfiguration : IEntityTypeConfiguration<DuelRecordEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DuelRecordEntity> builder)
		{
			builder.ToTable("duel_record");
			builder.HasKey(e => e.CharacterID);
			builder.Property(e => e.CharacterID).ValueGeneratedNever();
			builder.Property(e => e.Rating).IsRequired().HasDefaultValue(DuelRating.DefaultRating);
			builder.Property(e => e.PeakRating).IsRequired().HasDefaultValue(DuelRating.DefaultRating);
			builder.Property(e => e.Wins).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.Losses).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.Draws).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.LastUpdated).IsRequired().HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");
			// The two duel boards: top ratings, most wins.
			builder.HasIndex(e => e.Rating);
			builder.HasIndex(e => e.Wins);
			builder.HasOne<CharacterEntity>().WithMany().HasForeignKey(e => e.CharacterID).OnDelete(DeleteBehavior.Cascade);
		}
	}
}
