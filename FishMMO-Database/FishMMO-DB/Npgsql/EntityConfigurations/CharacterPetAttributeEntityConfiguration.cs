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

			// Performance index for character pet attribute queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterPetAttribute_CharacterID");
		}
	}
}