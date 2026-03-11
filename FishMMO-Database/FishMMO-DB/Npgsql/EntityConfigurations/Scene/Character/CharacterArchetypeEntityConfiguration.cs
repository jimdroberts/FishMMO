using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterArchetypeEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterArchetypeEntityConfiguration : IEntityTypeConfiguration<CharacterArchetypeEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterArchetypeEntity> builder)
		{
			builder.ToTable("character_archetypes");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			// Unique constraint: one archetype per template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character archetype queries
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Archetypes)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}