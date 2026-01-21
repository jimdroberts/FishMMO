using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterHotkeyEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterHotkeyEntityConfiguration : IEntityTypeConfiguration<CharacterHotkeyEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterHotkeyEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Type)
				.IsRequired();

			builder.Property(e => e.Slot)
				.IsRequired();

			builder.Property(e => e.ReferenceID)
				.IsRequired();

			// Unique constraint: one hotkey per slot per character
			builder.HasIndex(e => new { e.CharacterID, e.Slot })
				.IsUnique()
				.HasDatabaseName("IX_CharacterHotkey_Character_Slot_Unique");

			// Performance index for character hotkey queries (hot path for UI load)
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterHotkey_CharacterID");

			// Performance index for slot-based queries
			builder.HasIndex(e => e.Slot)
				.HasDatabaseName("IX_CharacterHotkey_Slot");

			// Foreign key relationship (prevent orphan hotkey rows)
			builder.HasOne(e => e.Character)
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}