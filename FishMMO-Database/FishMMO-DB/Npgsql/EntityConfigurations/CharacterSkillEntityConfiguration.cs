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
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Hash)
				.IsRequired();

			builder.Property(e => e.Level)
				.IsRequired()
				.HasDefaultValue(1);

			builder.Property(e => e.CastTimeEnd)
				.IsRequired()
				.HasDefaultValue(0f);

			builder.Property(e => e.CooldownEnd)
				.IsRequired()
				.HasDefaultValue(0f);

			// Unique constraint: one skill hash per character
			builder.HasIndex(e => new { e.CharacterID, e.Hash })
				.IsUnique()
				.HasDatabaseName("IX_CharacterSkill_Character_Hash_Unique");

			// Performance index for character skill queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterSkill_CharacterID");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Skills)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}