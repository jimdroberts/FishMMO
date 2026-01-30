using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPetAttributeEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPetAttributeEntityConfiguration : IEntityTypeConfiguration<CharacterPetAttributeEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterPetAttributeEntity> builder)
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

			builder.Property(e => e.CurrentValue)
				.IsRequired()
				.HasDefaultValue(0f);

			// Unique constraint: one attribute template per character pet
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique()
				.HasDatabaseName("IX_CharacterPetAttribute_Character_Template_Unique");

			builder.HasOne(e => e.Character)
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}