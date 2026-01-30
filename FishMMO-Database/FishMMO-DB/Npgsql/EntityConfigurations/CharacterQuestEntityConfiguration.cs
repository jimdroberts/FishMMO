using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterQuestEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterQuestEntityConfiguration : IEntityTypeConfiguration<CharacterQuestEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterQuestEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.Progress)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.Completed)
				.IsRequired()
				.HasDefaultValue(false);

			// Unique constraint: one quest per name per character
			builder.HasIndex(e => new { e.CharacterID, e.Name })
				.IsUnique()
				.HasDatabaseName("IX_CharacterQuest_Character_Name_Unique");

			// Performance index for character quest queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterQuest_CharacterID");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Quests)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}