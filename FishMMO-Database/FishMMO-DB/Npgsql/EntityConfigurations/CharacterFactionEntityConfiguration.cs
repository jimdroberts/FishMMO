using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterFactionEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterFactionEntityConfiguration : IEntityTypeConfiguration<CharacterFactionEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterFactionEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Value)
				.IsRequired()
				.HasDefaultValue(0);

			// Unique constraint: one faction template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique()
				.HasDatabaseName("IX_CharacterFaction_Character_Template_Unique");

			// Performance index for character faction queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterFaction_CharacterID");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Faction)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}