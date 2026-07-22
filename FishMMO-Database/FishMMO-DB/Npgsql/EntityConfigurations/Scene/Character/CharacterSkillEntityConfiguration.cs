using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterSkillEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterSkillEntityConfiguration : IEntityTypeConfiguration<CharacterSkillEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterSkillEntity> builder)
		{
			builder.ToTable("character_skills");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Level)
				.IsRequired()
				.HasDefaultValue(1);

			builder.Property(e => e.Experience)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.CastTimeEnd)
				.IsRequired()
				.HasDefaultValue(0d);

			builder.Property(e => e.CooldownEnd)
				.IsRequired()
				.HasDefaultValue(0d);

			// Unique constraint: one skill template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character skill queries
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Skills)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}