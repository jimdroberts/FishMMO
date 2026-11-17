using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPetEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPetEntityConfiguration : IEntityTypeConfiguration<CharacterPetEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterPetEntity> builder)
		{
			builder.ToTable("character_pet");

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
				.IsRequired()
				.HasColumnType("integer[]")
				.HasDefaultValueSql("'{}'");

			builder.Property(e => e.Spawned)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.Slot)
				.IsRequired()
				.HasDefaultValue(0);

			// Unique constraint: one pet per slot. A character has one slot unless an archetype grants more.
			builder.HasIndex(e => new { e.CharacterID, e.Slot })
				.IsUnique();

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Pets)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}