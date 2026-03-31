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
			builder.ToTable("character_quests");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Status)
				.IsRequired()
				.HasDefaultValue((byte)0);

			builder.Property(e => e.ObjectiveValues)
				.IsRequired(false)
				.HasMaxLength(500);

			// Unique constraint: one quest per template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character quest queries (hot path for character load)
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Quests)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}