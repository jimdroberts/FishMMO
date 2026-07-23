using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterAchievementEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterAchievementEntityConfiguration : IEntityTypeConfiguration<CharacterAchievementEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterAchievementEntity> builder)
		{
			builder.ToTable("character_achievements");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Tier)
				.IsRequired()
				.HasDefaultValue((byte)0);

			builder.Property(e => e.Value)
				.IsRequired()
				.HasDefaultValue(0u);

			// Unique constraint: one achievement template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character achievement queries (hot path for character load)
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Achievements)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}