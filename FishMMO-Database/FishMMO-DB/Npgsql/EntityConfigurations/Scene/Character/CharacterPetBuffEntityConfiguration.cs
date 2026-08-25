using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPetBuffEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPetBuffEntityConfiguration : IEntityTypeConfiguration<CharacterPetBuffEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterPetBuffEntity> builder)
		{
			builder.ToTable("character_pet_buffs");

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
				.HasDefaultValue(0d);

			builder.Property(e => e.TickTime)
				.IsRequired()
				.HasDefaultValue(0d);

			builder.Property(e => e.Stacks)
				.IsRequired()
				.HasDefaultValue(1);

			builder.Property(e => e.TickCount)
				.IsRequired()
				.HasDefaultValue(0);

			// Unique constraint: one buff template per character pet
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.PetBuffs)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}