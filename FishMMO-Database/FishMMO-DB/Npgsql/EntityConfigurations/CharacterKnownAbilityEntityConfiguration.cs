using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterKnownAbilityEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterKnownAbilityEntityConfiguration : IEntityTypeConfiguration<CharacterKnownAbilityEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterKnownAbilityEntity> builder)
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

			// Unique constraint: one known ability template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique()
				.HasDatabaseName("IX_CharacterKnownAbility_Character_Template_Unique");

			// Performance index for character known ability queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterKnownAbility_CharacterID");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.KnownAbilities)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}