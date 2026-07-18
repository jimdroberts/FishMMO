using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterItemCooldownEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterItemCooldownEntityConfiguration : IEntityTypeConfiguration<CharacterItemCooldownEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterItemCooldownEntity> builder)
		{
			builder.ToTable("character_itemcooldowns");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Category)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.CooldownEnd)
				.IsRequired()
				.HasDefaultValue(0d);

			// Unique constraint: one cooldown category per character
			builder.HasIndex(e => new { e.CharacterID, e.Category })
				.IsUnique();

			// Performance index for character item cooldown queries
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.ItemCooldowns)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}