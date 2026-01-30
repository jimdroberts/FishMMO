using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPetBuffEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPetBuffEntityConfiguration : IEntityTypeConfiguration<CharacterPetBuffEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterPetBuffEntity> builder)
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

			builder.Property(e => e.RemainingTime)
				.IsRequired()
				.HasDefaultValue(0f);

			builder.Property(e => e.TickTime)
				.IsRequired()
				.HasDefaultValue(0f);

			builder.Property(e => e.Stacks)
				.IsRequired()
				.HasDefaultValue(1);

			// Unique constraint: one buff template per character pet
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique()
				.HasDatabaseName("IX_CharacterPetBuff_Character_Template_Unique");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.PetBuffs)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}