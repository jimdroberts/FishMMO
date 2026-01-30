using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterBankEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterBankEntityConfiguration : IEntityTypeConfiguration<CharacterBankEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterBankEntity> builder)
		{
			builder.ToTable("character_bank");

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

			// Unique constraint: one item per bank slot per character
			builder.HasIndex(e => new { e.CharacterID, e.Slot })
				.IsUnique();

			// Performance index for character bank queries (hot path for bank access)
			builder.HasIndex(e => e.CharacterID);

			// Index for template-based queries (e.g., "find all characters with item X in bank")
			builder.HasIndex(e => e.TemplateID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Bank)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}