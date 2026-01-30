using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPetEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPetEntityConfiguration : IEntityTypeConfiguration<CharacterPetEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterPetEntity> builder)
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

			builder.Property(e => e.Abilities)
				.IsRequired();

			builder.Property(e => e.Spawned)
				.IsRequired()
				.HasDefaultValue(false);

			// Unique constraint: one pet per character
			builder.HasIndex(e => e.CharacterID)
				.IsUnique()
				.HasDatabaseName("IX_CharacterPet_CharacterID_Unique");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithOne(c => c.Pet)
				.HasForeignKey<CharacterPetEntity>(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}