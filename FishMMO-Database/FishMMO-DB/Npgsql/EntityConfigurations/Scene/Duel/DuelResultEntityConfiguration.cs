using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="DuelResultEntity"/>.</summary>
	public class DuelResultEntityConfiguration : IEntityTypeConfiguration<DuelResultEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<DuelResultEntity> builder)
		{
			builder.ToTable("duel_result");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();
			builder.Property(e => e.RequestKey).IsRequired();
			builder.Property(e => e.WinnerID).IsRequired();
			builder.Property(e => e.LoserID).IsRequired();
			builder.Property(e => e.Draw).IsRequired().HasDefaultValue(false);
			builder.Property(e => e.Reason).IsRequired().HasDefaultValue((short)0);
			builder.Property(e => e.WinnerRatingChange).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.LoserRatingChange).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.CreatedUtc).IsRequired().HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");
			builder.HasIndex(e => e.RequestKey).IsUnique();
			// A character's recent duels, either side.
			builder.HasIndex(e => new { e.WinnerID, e.CreatedUtc });
			builder.HasIndex(e => new { e.LoserID, e.CreatedUtc });
			builder.HasOne<CharacterEntity>().WithMany().HasForeignKey(e => e.WinnerID).OnDelete(DeleteBehavior.Cascade);
			builder.HasOne<CharacterEntity>().WithMany().HasForeignKey(e => e.LoserID).OnDelete(DeleteBehavior.Cascade);
		}
	}
}
