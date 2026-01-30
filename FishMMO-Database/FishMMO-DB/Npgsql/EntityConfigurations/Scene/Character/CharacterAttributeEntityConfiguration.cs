using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterAttributeEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterAttributeEntityConfiguration : IEntityTypeConfiguration<CharacterAttributeEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterAttributeEntity> builder)
		{
			builder.ToTable("character_attributes");

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

			// Unique constraint: one attribute template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character attribute queries
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Attributes)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}