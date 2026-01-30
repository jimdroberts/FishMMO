using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterAbilityEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterAbilityEntityConfiguration : IEntityTypeConfiguration<CharacterAbilityEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterAbilityEntity> builder)
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

			builder.Property(e => e.AbilityEvents)
				.IsRequired()
				.HasDefaultValue(new System.Collections.Generic.List<int>());

			builder.Property(e => e.Cooldown)
				.IsRequired()
				.HasDefaultValue(0f);

			// Unique constraint: one ability template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique()
				.HasDatabaseName("IX_CharacterAbility_Character_Template_Unique");

			// Performance index for character ability queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterAbility_CharacterID");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Abilities)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}