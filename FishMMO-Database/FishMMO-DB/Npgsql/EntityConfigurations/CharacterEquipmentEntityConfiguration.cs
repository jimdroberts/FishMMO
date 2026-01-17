using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterEquipmentEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterEquipmentEntityConfiguration : IEntityTypeConfiguration<CharacterEquipmentEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterEquipmentEntity> builder)
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

			builder.Property(e => e.Slot)
				.IsRequired();

			builder.Property(e => e.Seed)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.Amount)
				.IsRequired()
				.HasDefaultValue(1u);

			// Unique constraint: one item per equipment slot per character
			builder.HasIndex(e => new { e.CharacterID, e.Slot })
				.IsUnique()
				.HasDatabaseName("IX_CharacterEquipment_Character_Slot_Unique");

			// Performance index for character equipment queries
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterEquipment_CharacterID");

			// Index for template-based queries
			builder.HasIndex(e => e.TemplateID)
				.HasDatabaseName("IX_CharacterEquipment_TemplateID");

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Equipment)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}